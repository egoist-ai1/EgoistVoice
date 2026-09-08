Add-Type -AssemblyName System.Net.Http
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Cred {
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);
    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    public static extern void CredFree(IntPtr credential);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENTIAL {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
    public static string GetToken() {
        IntPtr credPtr;
        if (CredRead("git:https://github.com", 1, 0, out credPtr)) {
            try {
                var cred = (CREDENTIAL)Marshal.PtrToStructure(credPtr, typeof(CREDENTIAL));
                if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero) {
                    byte[] bytes = new byte[cred.CredentialBlobSize];
                    Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
                    return Encoding.Unicode.GetString(bytes);
                }
            } finally {
                CredFree(credPtr);
            }
        }
        return null;
    }
}
"@

$token = [Cred]::GetToken()
if ([string]::IsNullOrEmpty($token)) { throw "GitHub token not found in Windows Credential Manager." }

$repo = "egoist-ai1/EgoistVoice"
$tag = "v2.2.0"
$releaseTitle = "Egoist Voice 2.2.0 — Obsidian Glass, 240 FPS & Compact Installer"
$notesPath = "C:\Users\Egoist\AppData\Local\EgoistVoice\Source\EgoistVoice-main\docs\releases\2.2.0.md"
$notes = [System.IO.File]::ReadAllText($notesPath, [System.Text.Encoding]::UTF8)

$headers = @{
    "Authorization" = "token $token"
    "User-Agent" = "EgoistVoiceRelease/2.2.0"
    "Accept" = "application/vnd.github+json"
}

$release = $null
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/tags/$tag" -Headers $headers -Method Get
    Write-Output "Existing release found: ID $($release.id), URL $($release.html_url)"
} catch {
    Write-Output "Release not found for $tag, creating new release..."
}

if ($null -eq $release) {
    $payload = @{
        tag_name = [string]$tag
        target_commitish = "main"
        name = [string]$releaseTitle
        body = [string]$notes
        draft = $false
        prerelease = $false
    }
    $bodyJson = $payload | ConvertTo-Json -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)
    
    $req = [System.Net.HttpWebRequest]::Create("https://api.github.com/repos/$repo/releases")
    $req.Method = "POST"
    $req.Headers.Add("Authorization", "token $token")
    $req.UserAgent = "EgoistVoiceRelease/2.2.0"
    $req.Accept = "application/vnd.github+json"
    $req.ContentType = "application/json; charset=utf-8"
    $req.ContentLength = $bytes.Length
    $reqStream = $req.GetRequestStream()
    $reqStream.Write($bytes, 0, $bytes.Length)
    $reqStream.Close()
    
    $resp = $req.GetResponse()
    $reader = [System.IO.StreamReader]::new($resp.GetResponseStream())
    $respText = $reader.ReadToEnd()
    $reader.Close()
    $resp.Close()
    
    $release = $respText | ConvertFrom-Json
    Write-Output "Created release ID $($release.id): $($release.html_url)"
}

# Upload Asset
$assetFile = "C:\Users\Egoist\Desktop\EgoistVoice-Setup-Compact-RU-2.2.0-win-x64.exe"
if (!(Test-Path $assetFile)) { throw "Asset file not found: $assetFile" }
$assetName = [System.IO.Path]::GetFileName($assetFile)

$existingAsset = $release.assets | Where-Object { $_.name -eq $assetName }
if ($existingAsset) {
    Write-Output "Asset $assetName already present in release."
} else {
    Write-Output "Uploading $assetName ($((Get-Item $assetFile).Length) bytes)..."
    $uploadUrl = "https://uploads.github.com/repos/$repo/releases/$($release.id)/assets?name=$assetName"
    
    $httpClient = [System.Net.Http.HttpClient]::new()
    $httpClient.Timeout = [System.TimeSpan]::FromMinutes(20)
    $httpClient.DefaultRequestHeaders.Add("Authorization", "token $token")
    $httpClient.DefaultRequestHeaders.Add("User-Agent", "EgoistVoiceRelease/2.2.0")
    $httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json")
    
    $fileStream = [System.IO.File]::OpenRead($assetFile)
    $streamContent = [System.Net.Http.StreamContent]::new($fileStream)
    $streamContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new("application/octet-stream")
    
    $response = $httpClient.PostAsync($uploadUrl, $streamContent).GetAwaiter().GetResult()
    $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $fileStream.Close()
    $httpClient.Dispose()
    
    if (!$response.IsSuccessStatusCode) {
        throw "Asset upload failed: $($response.StatusCode) - $responseBody"
    }
    Write-Output "Asset uploaded successfully!"
}

Write-Output "SUCCESS! Release published: $($release.html_url)"
