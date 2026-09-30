[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?$')][string]$Version,
    [string]$ApkPath,
    [string]$WindowsFolder,
    [string]$Repository = 'Blackskydk/FrameEarthVR'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $ApkPath) { $ApkPath = Join-Path $projectRoot 'Builds/SteamFrame/FrameEarthVR.apk' }
$resolvedApk = (Resolve-Path -LiteralPath $ApkPath).Path
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedApk)
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '(?i)\.local\.json(?:\.meta)?$') {
            throw 'This APK contains local configuration. Rebuild with EarthVR > Steam Frame > Build Release APK.'
        }
    }
    if (-not ($archive.Entries | Where-Object { $_.FullName -eq 'lib/arm64-v8a/libunity.so' })) {
        throw 'The APK is missing the ARM64 Unity player.'
    }
    $policyEntry = $archive.GetEntry('assets/EarthVR/public-build-policy.json')
    if (-not $policyEntry) { throw 'This APK predates first-run credential setup. Rebuild it before distributing.' }
    $reader = [System.IO.StreamReader]::new($policyEntry.Open())
    try { $policy = $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $reader.Dispose() }
    if ($policy.schema -ne 'earthvr.credentials/v1' -or $policy.firstRunSetup -ne $true) {
        throw 'The APK does not declare the expected first-run credential policy.'
    }
}
finally { $archive.Dispose() }

$editorVersion = [regex]::Match((Get-Content (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Raw), 'm_EditorVersion:\s*([^\r\n]+)').Groups[1].Value.Trim()
$buildToolsRoot = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$editorVersion/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/build-tools"
$aapt = Get-ChildItem -LiteralPath $buildToolsRoot -Filter aapt.exe -Recurse | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $aapt) { throw 'Android build tools are required to verify that the APK is not debuggable.' }
$badging = & $aapt.FullName dump badging $resolvedApk
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the APK.' }
if (($badging -join "`n") -match 'application-debuggable') { throw 'Do not distribute the development APK. Build Release APK first.' }
$apkVersion = [regex]::Match(($badging -join "`n"), "versionName='([^']+)'").Groups[1].Value
if ($apkVersion -ne $Version.Substring(1)) {
    throw "APK version '$apkVersion' does not match release '$Version'. Set ReleaseBuildStamp and rebuild first."
}

$outputFolder = Join-Path $projectRoot "Builds/Public/$Version"
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$publicApk = Join-Path $outputFolder 'FrameEarthVR.apk'
Copy-Item -LiteralPath $resolvedApk -Destination $publicApk
$hash = (Get-FileHash -LiteralPath $publicApk -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  FrameEarthVR.apk" | Set-Content -LiteralPath (Join-Path $outputFolder 'SHA256SUMS.txt') -Encoding ascii
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'frame-updater.py') -Destination (Join-Path $outputFolder 'frame-updater.py')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'setup-token.ps1') -Destination (Join-Path $outputFolder 'setup-token.ps1')
$setupHash = (Get-FileHash -LiteralPath (Join-Path $outputFolder 'setup-token.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()
"$setupHash  setup-token.ps1" | Add-Content -LiteralPath (Join-Path $outputFolder 'SHA256SUMS.txt') -Encoding ascii
$helperHash = (Get-FileHash -LiteralPath (Join-Path $outputFolder 'frame-updater.py') -Algorithm SHA256).Hash.ToLowerInvariant()
"$helperHash  frame-updater.py" | Add-Content -LiteralPath (Join-Path $outputFolder 'SHA256SUMS.txt') -Encoding ascii
if ($WindowsFolder) {
    $windowsRoot = (Resolve-Path -LiteralPath $WindowsFolder).Path
    foreach ($required in @('FrameEarthVR.exe', 'UnityPlayer.dll', 'FrameEarthVR_Data/data.unity3d', 'earthvr-version.txt',
            'FrameEarthVR_Data/StreamingAssets/EarthVR/ApplyWindowsUpdate.ps1', 'FrameEarthVR_Data/StreamingAssets/EarthVR/public-build-policy.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $windowsRoot $required))) { throw "Windows release is missing $required" }
    }
    if ((Get-Content -LiteralPath (Join-Path $windowsRoot 'earthvr-version.txt') -Raw).Trim() -ne $Version.Substring(1)) {
        throw 'Windows build version does not match the release tag'
    }
    if (Get-ChildItem -LiteralPath $windowsRoot -File -Recurse | Where-Object { $_.Name -match '(?i)\.local\.json(?:\.meta)?$' }) {
        throw 'Windows build contains local credentials; rebuild with EarthVR > Windows > Build Release'
    }
    $windowsZip = Join-Path $outputFolder 'FrameEarthVR-Windows.zip'
    if (Test-Path -LiteralPath $windowsZip) { Remove-Item -LiteralPath $windowsZip }
    $archive = [IO.Compression.ZipFile]::Open($windowsZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $allowed = @('FrameEarthVR.exe', 'FrameEarthVR_Data', 'UnityPlayer.dll', 'GameAssembly.dll', 'MonoBleedingEdge',
                     'UnityCrashHandler64.exe', 'D3D12', 'earthvr-version.txt')
        foreach ($name in $allowed) {
            $entryRoot = Join-Path $windowsRoot $name
            if (-not (Test-Path -LiteralPath $entryRoot)) { continue }
            $files = if ((Get-Item -LiteralPath $entryRoot).PSIsContainer) { Get-ChildItem -LiteralPath $entryRoot -File -Recurse } else { Get-Item -LiteralPath $entryRoot }
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($windowsRoot.Length + 1).Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
    }
    finally { $archive.Dispose() }
    $windowsHash = (Get-FileHash -LiteralPath $windowsZip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$windowsHash  FrameEarthVR-Windows.zip" | Add-Content -LiteralPath (Join-Path $outputFolder 'SHA256SUMS.txt') -Encoding ascii
}
@"
# Frame Earth VR $Version

[Download APK](https://github.com/$Repository/releases/download/$Version/FrameEarthVR.apk)

Standalone Steam Frame build. Pair your Frame in Developer Mode, install with
Valve's SteamOS Devkit Client with Lepton, then launch it from
your Steam library. Unity is not needed by players.

On first launch, provide your own Cesium ion token with assets:read access to
Google Photorealistic 3D Tiles asset 2275207. No publisher token or separate
Google API key is included. Tokens are stored on your device, outside the APK.

Download setup-token.ps1 to your Windows PC, right-click it and choose Run with
PowerShell. Paste your own token into the masked dialog and choose Windows,
Frame APK, or Frame Windows/Proton. Frame must already be paired in Devkit Client
and the game launched once, then closed. Reopen it after saving. The token stays
in its persistent app-data folder across updates. No token is passed on a command
line. The APK avoids Lepton's clipboard API; a controller keyboard remains available.
Your Cesium account's terms and usage limits apply.

From preview.4, select Download & Apply Update in the hand menu. Downloads
are verified and applied after the game closes. On a Windows PC, the updater
is included. Frame needs the one-time helper setup for APK and Proton builds:
download frame-updater.py to the headset's Downloads folder and run
python3 ~/Downloads/frame-updater.py --setup in a Frame terminal. Reopen the
game from Steam after a Frame update. Public GitHub releases are required.
The APK helper passed a Frame replacement/relaunch test using its local inbox.
The in-game button and a live Windows/Proton update still need verification.

Files: FrameEarthVR.apk, optional FrameEarthVR-Windows.zip, frame-updater.py, setup-token.ps1,
SHA256SUMS.txt. Use the APK for Lepton, or the ZIP for Windows/Proton.
"@ | Set-Content -LiteralPath (Join-Path $outputFolder 'RELEASE_NOTES.md') -Encoding utf8
Write-Host "Prepared release files in $outputFolder"
Write-Host 'This command does not publish a GitHub release.'
