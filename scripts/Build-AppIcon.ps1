[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourcePng, [switch]$Export)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$Export) { [pscustomobject]@{planOnly=$true;source=$SourcePng;destination=(Join-Path $root 'assets');frames=@(16,20,24,32,40,48,64,128,256)} | ConvertTo-Json; exit 0 }
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class VoiceIconExport {
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    public static void VerifyFrame(string iconPath, string output, int size) {
        var handle=LoadImage(IntPtr.Zero,iconPath,1,size,size,16);
        if(handle==IntPtr.Zero) throw new InvalidOperationException("Windows icon decode failed.");
        try {
            using(var icon=Icon.FromHandle(handle)) {
                if(icon.Width!=size || icon.Height!=size) throw new InvalidOperationException("Windows icon frame dimensions differ.");
                using(var bitmap=icon.ToBitmap()) bitmap.Save(output,ImageFormat.Png);
            }
        } finally { DestroyIcon(handle); }
    }
    public static void Save(string source, string iconPath, string previewPath) {
        int[] sizes = {16,20,24,32,40,48,64,128,256};
        var frames = new List<byte[]>();
        using (var original = new Bitmap(source)) {
            if (original.Width != original.Height || original.GetPixel(0,0).A != 0) throw new InvalidOperationException("Expected square PNG with transparent padding.");
            foreach (int size in sizes) {
                using (var bitmap = new Bitmap(size,size,PixelFormat.Format32bppArgb)) {
                    using (var graphics = Graphics.FromImage(bitmap)) {
                        graphics.Clear(Color.Transparent);
                        graphics.CompositingMode=CompositingMode.SourceCopy;
                        graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                        using (var attributes = new ImageAttributes()) {
                            attributes.SetWrapMode(WrapMode.TileFlipXY);
                            graphics.DrawImage(original,new Rectangle(0,0,size,size),0,0,original.Width,original.Height,GraphicsUnit.Pixel,attributes);
                        }
                    }
                    using (var stream = new MemoryStream())
                    using (var dib = new BinaryWriter(stream)) {
                        int maskStride = ((size+31)/32)*4;
                        dib.Write(40); dib.Write(size); dib.Write(size*2); dib.Write((ushort)1); dib.Write((ushort)32);
                        dib.Write(0); dib.Write(size*size*4+maskStride*size); dib.Write(0); dib.Write(0); dib.Write(0); dib.Write(0);
                        for (int y=size-1;y>=0;y--) for (int x=0;x<size;x++) {
                            Color pixel=bitmap.GetPixel(x,y);
                            dib.Write(pixel.B); dib.Write(pixel.G); dib.Write(pixel.R); dib.Write(pixel.A);
                        }
                        for (int y=size-1;y>=0;y--) {
                            byte[] mask=new byte[maskStride];
                            for (int x=0;x<size;x++) if(bitmap.GetPixel(x,y).A==0) mask[x/8] |= (byte)(128>>(x%8));
                            dib.Write(mask);
                        }
                        dib.Flush(); frames.Add(stream.ToArray());
                    }
                    if (size == 256) bitmap.Save(previewPath, ImageFormat.Png);
                }
            }
        }
        using (var writer = new BinaryWriter(File.Create(iconPath))) {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for (int i=0;i<sizes.Length;i++) {
                writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
    }
}
'@
$backup=Join-Path $root ('artifacts\EV-2223\qa\icon-backup-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($name in @('EgoistVoice.ico','EgoistVoice.png','EgoistVoice-icon-master.png')) { Copy-Item -LiteralPath (Join-Path $root ('assets\'+$name)) -Destination $backup }
[VoiceIconExport]::Save($SourcePng,(Join-Path $root 'assets\EgoistVoice.ico'),(Join-Path $root 'assets\EgoistVoice.png'))
Copy-Item -LiteralPath $SourcePng -Destination (Join-Path $root 'assets\EgoistVoice-icon-master.png')
$checks=foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
    [VoiceIconExport]::VerifyFrame((Join-Path $root 'assets\EgoistVoice.ico'),(Join-Path $root ('artifacts\EV-2223\qa\icon-'+$size+'.png')),$size)
    [pscustomobject]@{size=$size;nativeDecode=$true}
}
[ordered]@{passed=$true;sourceSha256=(Get-FileHash -LiteralPath $SourcePng).Hash.ToLowerInvariant();iconSha256=(Get-FileHash -LiteralPath (Join-Path $root 'assets\EgoistVoice.ico')).Hash.ToLowerInvariant();frames=$checks;backup=$backup} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'artifacts\EV-2223\qa\icon-export.json') -Encoding utf8
Get-Content -LiteralPath (Join-Path $root 'artifacts\EV-2223\qa\icon-export.json')
