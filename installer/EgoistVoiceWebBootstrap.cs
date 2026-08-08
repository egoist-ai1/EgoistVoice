using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed class PayloadFile
{
    internal readonly string Name;
    internal readonly long Length;
    internal readonly string Sha256;

    internal PayloadFile(string name, long length, string sha256)
    {
        Name = name;
        Length = length;
        Sha256 = sha256;
    }
}

internal static class EgoistVoiceWebBootstrap
{
    private const long FreeSpaceReserve = 512L * 1024L * 1024L;
    private const int BufferSize = 1024 * 1024;
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "EgoistVoiceWebSetup.log");

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [STAThread]
    private static int Main(string[] args)
    {
        bool verificationOnly = ContainsArgument(args, "--verify-only");
        bool downloadOnly = ContainsArgument(args, "--download-only");
        bool offlineOnly = ContainsArgument(args, "--offline");
        bool silent = ContainsArgument(args, "/SILENT") || ContainsArgument(args, "/VERYSILENT");
        try
        {
            try { SetProcessDPIAware(); } catch { }
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ValidateReleaseManifest();

            string executablePath = Process.GetCurrentProcess().MainModule.FileName;
            string bootstrapDirectory = Path.GetDirectoryName(executablePath);
            string payloadOverride = GetArgumentValue(args, "--payload-dir");
            if (!string.IsNullOrWhiteSpace(payloadOverride))
                bootstrapDirectory = Path.GetFullPath(payloadOverride);

            if (verificationOnly)
            {
                VerifyDirectory(bootstrapDirectory, null);
                return 0;
            }
            if (downloadOnly)
            {
                string downloadedDirectory = PrepareOnlinePayload(null, bootstrapDirectory);
                VerifyDirectory(downloadedDirectory, null);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            BootstrapForm form = new BootstrapForm(
                bootstrapDirectory,
                FilterBootstrapArguments(args),
                offlineOnly,
                silent);
            Application.Run(form);
            return form.ExitCode;
        }
        catch (Exception error)
        {
            WriteLog(error);
            if (!verificationOnly && !silent)
            {
                MessageBox.Show(
                    "Не удалось подготовить установку Egoist Voice. Проверьте интернет, свободное место и целостность файлов.\n\n" +
                    "Подробности: " + LogPath,
                    "Egoist Voice — установка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            return 1603;
        }
    }

    private static void ValidateReleaseManifest()
    {
        Uri baseUri = new Uri(EgoistVoiceReleaseManifest.ReleaseBaseUrl, UriKind.Absolute);
        bool validReleaseOrigin =
            string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(baseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase);
#if TEST
        validReleaseOrigin = validReleaseOrigin ||
            (string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             IPAddress.IsLoopback(baseUri.HostNameType == UriHostNameType.IPv4 || baseUri.HostNameType == UriHostNameType.IPv6
                 ? IPAddress.Parse(baseUri.Host)
                 : IPAddress.Loopback) &&
             string.Equals(baseUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase));
#endif
        if (!validReleaseOrigin)
            throw new InvalidDataException("Release URL must use HTTPS on github.com.");

        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool launchFound = false;
        long total = 0;
        foreach (PayloadFile file in EgoistVoiceReleaseManifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Name) ||
                !string.Equals(Path.GetFileName(file.Name), file.Name, StringComparison.Ordinal) ||
                file.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !names.Add(file.Name) || file.Length <= 0 ||
                file.Sha256 == null || file.Sha256.Length != 64 || !IsHex(file.Sha256))
                throw new InvalidDataException("Release manifest contains an unsafe payload entry.");
            string extension = Path.GetExtension(file.Name);
            if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Release manifest contains an unexpected payload type.");
            if (string.Equals(file.Name, EgoistVoiceReleaseManifest.LaunchFile, StringComparison.OrdinalIgnoreCase))
                launchFound = true;
            total = checked(total + file.Length);
        }
        if (!launchFound || EgoistVoiceReleaseManifest.Files.Length < 2 || total <= 0)
            throw new InvalidDataException("Release manifest identity is incomplete.");
    }

    private static bool IsHex(string value)
    {
        foreach (char character in value)
        {
            if (!((character >= '0' && character <= '9') ||
                  (character >= 'a' && character <= 'f') ||
                  (character >= 'A' && character <= 'F')))
                return false;
        }
        return true;
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

    private static string GetArgumentValue(string[] args, string expected)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], expected, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }
        return null;
    }

    private static string[] FilterBootstrapArguments(string[] args)
    {
        List<string> forwarded = new List<string>();
        for (int index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--verify-only", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[index], "--download-only", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[index], "--offline", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(args[index], "--payload-dir", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }
            forwarded.Add(args[index]);
        }
        return forwarded.ToArray();
    }

    private static long TotalPayloadBytes()
    {
        long total = 0;
        foreach (PayloadFile file in EgoistVoiceReleaseManifest.Files)
            total = checked(total + file.Length);
        return total;
    }

    private static bool HasCompletePayloadShape(string directory)
    {
        foreach (PayloadFile file in EgoistVoiceReleaseManifest.Files)
        {
            string path = Path.Combine(directory, file.Name);
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Length)
                return false;
        }
        return true;
    }

    private static void VerifyDirectory(string directory, Action<long, long, string> progress)
    {
        long total = TotalPayloadBytes();
        long completed = 0;
        foreach (PayloadFile file in EgoistVoiceReleaseManifest.Files)
        {
            string path = Path.Combine(directory, file.Name);
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Length)
                throw new InvalidDataException("Payload file is missing or has the wrong size: " + file.Name);
            string actual = ComputeHash(path, delegate(long current)
            {
                if (progress != null) progress(completed + current, total, "Проверяю " + file.Name + "…");
            });
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Payload checksum mismatch: " + file.Name);
            completed += file.Length;
        }
    }

    private static string ComputeHash(string path, Action<long> progress)
    {
        byte[] buffer = new byte[BufferSize];
        long completed = 0;
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (SHA256 sha = SHA256.Create())
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                completed += read;
                if (progress != null) progress(completed);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private static string PrepareOnlinePayload(Action<long, long, string> progress, string cacheOverride)
    {
        string cache = string.IsNullOrWhiteSpace(cacheOverride)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EgoistVoice",
                "InstallerCache",
                EgoistVoiceReleaseManifest.ReleaseTag)
            : Path.GetFullPath(cacheOverride);
        Directory.CreateDirectory(cache);

        long total = TotalPayloadBytes();
        long alreadyComplete = 0;
        foreach (PayloadFile file in EgoistVoiceReleaseManifest.Files)
        {
            string finalPath = Path.Combine(cache, file.Name);
            if (File.Exists(finalPath) && new FileInfo(finalPath).Length == file.Length &&
                string.Equals(ComputeHash(finalPath, null), file.Sha256, StringComparison.OrdinalIgnoreCase))
                alreadyComplete += file.Length;
            else if (File.Exists(finalPath))
                File.Delete(finalPath);
        }

        long required = checked(total - alreadyComplete + FreeSpaceReserve);
        string root = Path.GetPathRoot(Path.GetFullPath(cache));
        if (new DriveInfo(root).AvailableFreeSpace < required)
            throw new IOException("Недостаточно места для загрузки компонентов Egoist Voice.");

        long completedBeforeFile = 0;
        foreach (PayloadFile file in EgoistVoiceReleaseManifest.Files)
        {
            string finalPath = Path.Combine(cache, file.Name);
            if (File.Exists(finalPath))
            {
                completedBeforeFile += file.Length;
                if (progress != null) progress(completedBeforeFile, total, "Использую проверенный кэш…");
                continue;
            }
            DownloadWithResume(file, finalPath, completedBeforeFile, total, progress);
            string actual = ComputeHash(finalPath, null);
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(finalPath);
                throw new InvalidDataException("Downloaded payload checksum mismatch: " + file.Name);
            }
            completedBeforeFile += file.Length;
        }

        VerifyDirectory(cache, progress);
        return cache;
    }

    private static void DownloadWithResume(
        PayloadFile file,
        string finalPath,
        long completedBeforeFile,
        long total,
        Action<long, long, string> progress)
    {
        string partPath = finalPath + ".part";
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
                if (existing > file.Length)
                {
                    File.Delete(partPath);
                    existing = 0;
                }
                if (existing == file.Length)
                {
                    if (string.Equals(ComputeHash(partPath, null), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(partPath, finalPath);
                        return;
                    }
                    File.Delete(partPath);
                    existing = 0;
                }

                Uri uri = new Uri(EgoistVoiceReleaseManifest.ReleaseBaseUrl + Uri.EscapeDataString(file.Name));
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = "GET";
                request.UserAgent = "EgoistVoiceWebSetup/" + EgoistVoiceReleaseManifest.ApplicationVersion;
                request.AllowAutoRedirect = true;
                request.MaximumAutomaticRedirections = 8;
                request.Timeout = 30000;
                request.ReadWriteTimeout = 60000;
                request.KeepAlive = true;
                if (existing > 0) request.AddRange(existing);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    ValidateDownloadResponse(response.ResponseUri);
                    bool append = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                    if (response.StatusCode != HttpStatusCode.OK && !append)
                        throw new WebException("GitHub returned HTTP " + (int)response.StatusCode + ".");
                    if (!append) existing = 0;

                    using (FileStream output = new FileStream(
                        partPath,
                        FileMode.OpenOrCreate,
                        FileAccess.Write,
                        FileShare.None))
                    using (Stream input = response.GetResponseStream())
                    {
                        if (append) output.Position = existing;
                        else output.SetLength(0);
                        byte[] buffer = new byte[BufferSize];
                        long downloaded = existing;
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                            downloaded += read;
                            if (downloaded > file.Length)
                                throw new InvalidDataException("Downloaded payload exceeds the declared size.");
                            if (progress != null)
                                progress(
                                    completedBeforeFile + downloaded,
                                    total,
                                    "Скачиваю " + file.Name + "…");
                        }
                        output.Flush(true);
                    }
                }

                if (new FileInfo(partPath).Length != file.Length)
                    throw new EndOfStreamException("GitHub download ended before the declared payload size.");
                File.Move(partPath, finalPath);
                return;
            }
            catch (Exception error)
            {
                WriteLog(error);
                if (attempt == 3) throw;
                Thread.Sleep(attempt * 1000);
            }
        }
    }

    private static void ValidateDownloadResponse(Uri responseUri)
    {
        if (responseUri == null)
            throw new WebException("GitHub download returned no final URL.");
#if TEST
        if (string.Equals(responseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(responseUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
            return;
#endif
        if (!string.Equals(responseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new WebException("GitHub download redirected outside HTTPS.");
        string host = responseUri.Host;
        bool allowed = string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
            throw new WebException("GitHub download redirected to an unexpected host.");
    }

    private static int LaunchInnerInstaller(string directory, string[] args)
    {
        ProcessStartInfo start = new ProcessStartInfo();
        start.FileName = Path.Combine(directory, EgoistVoiceReleaseManifest.LaunchFile);
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
            if (character == '\\') slashes++;
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

    private static void WriteLog(Exception error)
    {
        try
        {
            File.AppendAllText(
                LogPath,
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

    private sealed class BootstrapForm : Form
    {
        private readonly string bootstrapDirectory;
        private readonly string[] arguments;
        private readonly bool offlineOnly;
        private readonly bool silent;
        private readonly ProgressBar progress;
        private readonly Label status;
        private readonly Label percent;
        internal int ExitCode = 1603;

        internal BootstrapForm(string bootstrapDirectory, string[] arguments, bool offlineOnly, bool silent)
        {
            this.bootstrapDirectory = bootstrapDirectory;
            this.arguments = arguments;
            this.offlineOnly = offlineOnly;
            this.silent = silent;

            Text = "Egoist Voice — установка";
            ClientSize = new Size(520, 156);
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
            title.Text = "Egoist Voice 2.2 — подготовка";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold, GraphicsUnit.Point);
            title.AutoSize = false;
            title.SetBounds(24, 18, 470, 28);
            Controls.Add(title);

            status = new Label();
            status.Text = "Ищу проверенные компоненты рядом с установщиком…";
            status.ForeColor = Color.FromArgb(190, 196, 206);
            status.AutoEllipsis = true;
            status.AutoSize = false;
            status.SetBounds(24, 52, 470, 22);
            Controls.Add(status);

            progress = new ProgressBar();
            progress.Style = ProgressBarStyle.Continuous;
            progress.Minimum = 0;
            progress.Maximum = 1000;
            progress.SetBounds(24, 86, 408, 18);
            Controls.Add(progress);

            percent = new Label();
            percent.Text = "0%";
            percent.ForeColor = Color.White;
            percent.TextAlign = ContentAlignment.MiddleRight;
            percent.SetBounds(438, 84, 55, 22);
            Controls.Add(percent);

            Label detail = new Label();
            detail.Text = "Скачивание возобновляется после обрыва · SHA-256 проверяется до запуска";
            detail.ForeColor = Color.FromArgb(122, 130, 143);
            detail.AutoSize = false;
            detail.SetBounds(24, 116, 470, 20);
            Controls.Add(detail);

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
            string payloadDirectory = null;
            bool downloaded = false;
            try
            {
                if (HasCompletePayloadShape(bootstrapDirectory))
                {
                    VerifyDirectory(bootstrapDirectory, ReportProgress);
                    payloadDirectory = bootstrapDirectory;
                }
                else
                {
                    if (offlineOnly)
                        throw new InvalidDataException("Offline payload files are missing next to the bootstrapper.");
                    payloadDirectory = PrepareOnlinePayload(ReportProgress, null);
                    downloaded = true;
                }

                Invoke((MethodInvoker)delegate
                {
                    status.Text = "Запускаю проверенную установку…";
                    progress.Value = progress.Maximum;
                    percent.Text = "100%";
                    Hide();
                });
                ExitCode = LaunchInnerInstaller(payloadDirectory, arguments);
                if (ExitCode == 0 && downloaded)
                    CleanupDirectory(payloadDirectory);
            }
            catch (Exception error)
            {
                WriteLog(error);
                if (!silent)
                {
                    Invoke((MethodInvoker)delegate
                    {
                        MessageBox.Show(
                            this,
                            "Не удалось подготовить установку Egoist Voice. Загрузка может быть продолжена при следующем запуске.\n\n" +
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
                BeginInvoke((MethodInvoker)Close);
            }
        }

        private void ReportProgress(long current, long total, string message)
        {
            int value = total <= 0 ? 0 : (int)Math.Min(1000L, current * 1000L / total);
            BeginInvoke((MethodInvoker)delegate
            {
                status.Text = message;
                progress.Value = value;
                percent.Text = (value / 10).ToString() + "%";
            });
        }
    }
}
