using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class EgoistVoiceBootstrap
{
    private const string FooterMagic = "EGOISTVOICEPKG01";
    private const int FooterSize = 60;
    private const long FreeSpaceReserve = 512L * 1024L * 1024L;
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "EgoistVoiceSetup.log");

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [STAThread]
    private static int Main(string[] args)
    {
        bool verificationOnly = ContainsArgument(args, "--verify-only");
        bool silent = ContainsArgument(args, "/SILENT") || ContainsArgument(args, "/VERYSILENT");
        try
        {
            try { SetProcessDPIAware(); } catch { }
            Package package = ReadAndVerifyManifest(Process.GetCurrentProcess().MainModule.FileName);

            if (verificationOnly)
            {
                VerifyPayload(package, null);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            BootstrapForm form = new BootstrapForm(package, FilterBootstrapArguments(args), silent);
            Application.Run(form);
            return form.ExitCode;
        }
        catch (Exception error)
        {
            WriteLog(error);
            if (!verificationOnly && !silent)
            {
                MessageBox.Show(
                    "Не удалось подготовить установку Egoist Voice. Файл повреждён или на диске недостаточно места.\n\n" +
                    "Подробности: " + LogPath,
                    "Egoist Voice — установка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            return 1603;
        }
    }

    private static bool ContainsArgument(string[] args, string expected)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string[] FilterBootstrapArguments(string[] args)
    {
        List<string> forwarded = new List<string>();
        foreach (string arg in args)
        {
            if (!string.Equals(arg, "--verify-only", StringComparison.OrdinalIgnoreCase))
                forwarded.Add(arg);
        }
        return forwarded.ToArray();
    }

    private static Package ReadAndVerifyManifest(string executablePath)
    {
        FileStream stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length < FooterSize)
                throw new InvalidDataException("Bootstrap footer is missing.");

            stream.Position = stream.Length - FooterSize;
            BinaryReader footer = new BinaryReader(stream, Encoding.UTF8, true);
            string magic = Encoding.ASCII.GetString(ReadExact(footer, 16));
            long manifestOffset = footer.ReadInt64();
            int manifestLength = footer.ReadInt32();
            byte[] expectedManifestHash = ReadExact(footer, 32);
            long footerOffset = stream.Length - FooterSize;
            if (!string.Equals(magic, FooterMagic, StringComparison.Ordinal) ||
                manifestOffset <= 0 || manifestLength <= 0 ||
                manifestOffset > footerOffset - manifestLength)
                throw new InvalidDataException("Bootstrap footer is invalid.");

            stream.Position = manifestOffset;
            byte[] manifestBytes = ReadExact(stream, manifestLength);
            byte[] actualManifestHash;
            using (SHA256 sha = SHA256.Create())
                actualManifestHash = sha.ComputeHash(manifestBytes);
            if (!FixedEquals(actualManifestHash, expectedManifestHash))
                throw new InvalidDataException("Bootstrap manifest checksum mismatch.");

            Package package = ParseManifest(executablePath, manifestOffset, manifestBytes);
            ValidateEntries(package);
            return package;
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static Package ParseManifest(string executablePath, long manifestOffset, byte[] manifestBytes)
    {
        MemoryStream memory = new MemoryStream(manifestBytes, false);
        BinaryReader reader = new BinaryReader(memory, Encoding.UTF8);
        try
        {
            if (reader.ReadInt32() != 1)
                throw new InvalidDataException("Unsupported bootstrap manifest version.");
            Package package = new Package();
            package.ExecutablePath = executablePath;
            package.ManifestOffset = manifestOffset;
            package.LaunchFile = ReadUtf8String(reader);
            int count = reader.ReadInt32();
            if (count < 2 || count > 32)
                throw new InvalidDataException("Invalid embedded file count.");
            for (int i = 0; i < count; i++)
            {
                Entry entry = new Entry();
                entry.Name = ReadUtf8String(reader);
                entry.Offset = reader.ReadInt64();
                entry.Length = reader.ReadInt64();
                entry.Sha256 = ReadExact(reader, 32);
                package.Entries.Add(entry);
            }
            if (memory.Position != memory.Length)
                throw new InvalidDataException("Unexpected bootstrap manifest data.");
            return package;
        }
        finally
        {
            reader.Dispose();
        }
    }

    private static void ValidateEntries(Package package)
    {
        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<Entry> ordered = new List<Entry>(package.Entries);
        ordered.Sort(delegate(Entry left, Entry right) { return left.Offset.CompareTo(right.Offset); });
        long previousEnd = 0;
        int executableCount = 0;
        bool launchFound = false;
        foreach (Entry entry in ordered)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 180 ||
                !string.Equals(Path.GetFileName(entry.Name), entry.Name, StringComparison.Ordinal) ||
                entry.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !names.Add(entry.Name))
                throw new InvalidDataException("Unsafe embedded file name.");
            if (entry.Offset < previousEnd || entry.Length <= 0 || entry.Offset > package.ManifestOffset - entry.Length)
                throw new InvalidDataException("Invalid embedded file bounds.");
            previousEnd = entry.Offset + entry.Length;
            string extension = Path.GetExtension(entry.Name);
            if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                executableCount++;
            else if (!string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected embedded file type.");
            if (string.Equals(entry.Name, package.LaunchFile, StringComparison.OrdinalIgnoreCase))
                launchFound = true;
        }
        if (!launchFound || executableCount != 1 ||
            !string.Equals(Path.GetExtension(package.LaunchFile), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded installer identity is invalid.");
    }

    private static void VerifyPayload(Package package, Action<long, long> progress)
    {
        long total = package.TotalBytes;
        long completed = 0;
        byte[] buffer = new byte[1024 * 1024];
        FileStream source = new FileStream(package.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            foreach (Entry entry in package.Entries)
            {
                source.Position = entry.Offset;
                using (SHA256 sha = SHA256.Create())
                {
                    long remaining = entry.Length;
                    while (remaining > 0)
                    {
                        int wanted = (int)Math.Min(buffer.Length, remaining);
                        int read = source.Read(buffer, 0, wanted);
                        if (read <= 0)
                            throw new EndOfStreamException("Embedded payload is truncated.");
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        remaining -= read;
                        completed += read;
                        if (progress != null) progress(completed, total);
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    if (!FixedEquals(sha.Hash, entry.Sha256))
                        throw new InvalidDataException("Embedded payload checksum mismatch: " + entry.Name);
                }
            }
        }
        finally
        {
            source.Dispose();
        }
    }

    private static void ExtractPayload(Package package, string destination, Action<long, long> progress)
    {
        long required = checked(package.TotalBytes + FreeSpaceReserve);
        string root = Path.GetPathRoot(Path.GetFullPath(destination));
        if (new DriveInfo(root).AvailableFreeSpace < required)
            throw new IOException("Insufficient free space for embedded installer extraction.");

        Directory.CreateDirectory(destination);
        long total = package.TotalBytes;
        long completed = 0;
        byte[] buffer = new byte[1024 * 1024];
        FileStream source = new FileStream(package.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            foreach (Entry entry in package.Entries)
            {
                string target = Path.Combine(destination, entry.Name);
                source.Position = entry.Offset;
                FileStream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                try
                {
                    using (SHA256 sha = SHA256.Create())
                    {
                        long remaining = entry.Length;
                        while (remaining > 0)
                        {
                            int wanted = (int)Math.Min(buffer.Length, remaining);
                            int read = source.Read(buffer, 0, wanted);
                            if (read <= 0)
                                throw new EndOfStreamException("Embedded payload is truncated.");
                            output.Write(buffer, 0, read);
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            remaining -= read;
                            completed += read;
                            if (progress != null) progress(completed, total);
                        }
                        sha.TransformFinalBlock(new byte[0], 0, 0);
                        if (!FixedEquals(sha.Hash, entry.Sha256))
                            throw new InvalidDataException("Embedded payload checksum mismatch: " + entry.Name);
                    }
                    output.Flush(true);
                }
                finally
                {
                    output.Dispose();
                }
            }
        }
        finally
        {
            source.Dispose();
        }
    }

    private static int LaunchInnerInstaller(Package package, string directory, string[] args)
    {
        ProcessStartInfo start = new ProcessStartInfo();
        start.FileName = Path.Combine(directory, package.LaunchFile);
        start.WorkingDirectory = directory;
        start.UseShellExecute = false;
        start.Arguments = JoinArguments(args);
        using (Process process = Process.Start(start))
        {
            if (process == null) throw new InvalidOperationException("Inner installer did not start.");
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    private static string JoinArguments(string[] args)
    {
        StringBuilder result = new StringBuilder();
        foreach (string arg in args)
        {
            if (result.Length > 0) result.Append(' ');
            result.Append(QuoteArgument(arg));
        }
        return result.ToString();
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            return value;
        StringBuilder quoted = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                slashes++;
            }
            else if (character == '"')
            {
                quoted.Append('\\', slashes * 2 + 1);
                quoted.Append('"');
                slashes = 0;
            }
            else
            {
                quoted.Append('\\', slashes);
                quoted.Append(character);
                slashes = 0;
            }
        }
        quoted.Append('\\', slashes * 2);
        quoted.Append('"');
        return quoted.ToString();
    }

    private static string ReadUtf8String(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length <= 0 || length > 512)
            throw new InvalidDataException("Invalid manifest string length.");
        return new UTF8Encoding(false, true).GetString(ReadExact(reader, length));
    }

    private static byte[] ReadExact(BinaryReader reader, int count)
    {
        return ReadExact(reader.BaseStream, count);
    }

    private static byte[] ReadExact(Stream stream, int count)
    {
        byte[] bytes = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(bytes, offset, count - offset);
            if (read <= 0) throw new EndOfStreamException();
            offset += read;
        }
        return bytes;
    }

    private static bool FixedEquals(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        int difference = 0;
        for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
        return difference == 0;
    }

    private static void WriteLog(Exception error)
    {
        try
        {
            File.AppendAllText(LogPath,
                DateTime.UtcNow.ToString("o") + " " + error + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch { }
    }

    private static void CleanupDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch
            {
                if (attempt == 4) return;
                Thread.Sleep(200);
            }
        }
    }

    private sealed class Package
    {
        internal string ExecutablePath;
        internal long ManifestOffset;
        internal string LaunchFile;
        internal readonly List<Entry> Entries = new List<Entry>();
        internal long TotalBytes
        {
            get
            {
                long total = 0;
                foreach (Entry entry in Entries) total = checked(total + entry.Length);
                return total;
            }
        }
    }

    private sealed class Entry
    {
        internal string Name;
        internal long Offset;
        internal long Length;
        internal byte[] Sha256;
    }

    private sealed class BootstrapForm : Form
    {
        private readonly Package package;
        private readonly string[] arguments;
        private readonly bool silent;
        private readonly ProgressBar progress;
        private readonly Label status;
        private readonly Label percent;
        internal int ExitCode = 1603;

        internal BootstrapForm(Package package, string[] arguments, bool silent)
        {
            this.package = package;
            this.arguments = arguments;
            this.silent = silent;

            Text = "Egoist Voice — подготовка";
            ClientSize = new Size(460, 142);
            MinimumSize = Size;
            MaximumSize = Size;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = !silent;
            BackColor = Color.FromArgb(18, 21, 26);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Opacity = silent ? 0 : 1;

            Label title = new Label();
            title.Text = "Подготовка установки Egoist Voice";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold, GraphicsUnit.Point);
            title.AutoSize = false;
            title.SetBounds(24, 18, 410, 28);
            Controls.Add(title);

            status = new Label();
            status.Text = "Проверяю и распаковываю встроенные компоненты…";
            status.ForeColor = Color.FromArgb(190, 196, 206);
            status.AutoSize = false;
            status.SetBounds(24, 52, 410, 22);
            Controls.Add(status);

            progress = new ProgressBar();
            progress.Style = ProgressBarStyle.Continuous;
            progress.Minimum = 0;
            progress.Maximum = 1000;
            progress.SetBounds(24, 84, 360, 18);
            Controls.Add(progress);

            percent = new Label();
            percent.Text = "0%";
            percent.ForeColor = Color.White;
            percent.TextAlign = ContentAlignment.MiddleRight;
            percent.SetBounds(390, 82, 45, 22);
            Controls.Add(percent);

            Shown += delegate { BeginWork(); };
        }

        private void BeginWork()
        {
            Thread worker = new Thread(Work);
            worker.IsBackground = true;
            worker.Start();
        }

        private void Work()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "EgoistVoiceSetup-" + Guid.NewGuid().ToString("N"));
            try
            {
                ExtractPayload(package, tempDirectory, ReportProgress);
                Invoke((MethodInvoker)delegate
                {
                    status.Text = "Запускаю установку…";
                    progress.Value = progress.Maximum;
                    percent.Text = "100%";
                    Hide();
                });
                ExitCode = LaunchInnerInstaller(package, tempDirectory, arguments);
            }
            catch (Exception error)
            {
                WriteLog(error);
                if (!silent)
                {
                    Invoke((MethodInvoker)delegate
                    {
                        MessageBox.Show(this,
                            "Не удалось подготовить установку Egoist Voice. Файл повреждён или на диске недостаточно места.\n\n" +
                            "Подробности: " + LogPath,
                            "Egoist Voice — установка",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    });
                }
                ExitCode = 1603;
            }
            finally
            {
                CleanupDirectory(tempDirectory);
                BeginInvoke((MethodInvoker)Close);
            }
        }

        private void ReportProgress(long current, long total)
        {
            int value = total <= 0 ? 0 : (int)Math.Min(1000L, current * 1000L / total);
            BeginInvoke((MethodInvoker)delegate
            {
                progress.Value = value;
                percent.Text = (value / 10).ToString() + "%";
            });
        }
    }
}
