[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?$')][string]$Version,
    [string]$ApkPath,
    [string]$Repository = 'Blackskydk/FrameEarthVR'
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $ApkPath) { $ApkPath = Join-Path $projectRoot 'Builds/SteamFrame/FrameEarthVR.apk' }
$resolvedApk = (Resolve-Path -LiteralPath $ApkPath).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
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

$outputFolder = Join-Path $projectRoot "Builds/Public/$Version"
New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
$publicApk = Join-Path $outputFolder 'FrameEarthVR.apk'
Copy-Item -LiteralPath $resolvedApk -Destination $publicApk
$hash = (Get-FileHash -LiteralPath $publicApk -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    schema = 'framedrop.install/v1'
    name = 'Frame Earth VR'
    files = @([ordered]@{ url = "https://github.com/$Repository/releases/download/$Version/FrameEarthVR.apk"; sha256 = $hash })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputFolder 'FrameEarthVR.framedrop.json') -Encoding utf8
"$hash  FrameEarthVR.apk" | Set-Content -LiteralPath (Join-Path $outputFolder 'SHA256SUMS.txt') -Encoding ascii
$manifestUrl = "https://github.com/$Repository/releases/download/$Version/FrameEarthVR.framedrop.json"
$installUrl = 'https://framedropvr.com/install?manifest=' + [Uri]::EscapeDataString($manifestUrl)
@"
# Frame Earth VR $Version

[Install with FrameDrop]($installUrl) | [Download APK](https://github.com/$Repository/releases/download/$Version/FrameEarthVR.apk)

Standalone Steam Frame build. Pair your Frame in Developer Mode, install with
FrameDrop (or Valve's SteamOS Devkit Client with Lepton), then launch it from
your Steam library. Unity is not needed by players.

On first launch, provide your own Cesium ion token with assets:read access to
Google Photorealistic 3D Tiles asset 2275207. No publisher token or separate
Google API key is included. Tokens are stored on your device, outside the APK.

Paste uses the headset's clipboard, not your PC clipboard. A controller
keyboard is available. Your Cesium account's terms and usage limits apply.

Files: FrameEarthVR.apk, FrameEarthVR.framedrop.json, SHA256SUMS.txt.
"@ | Set-Content -LiteralPath (Join-Path $outputFolder 'RELEASE_NOTES.md') -Encoding utf8
Write-Host "Prepared release files in $outputFolder"
Write-Host 'This command does not publish a GitHub release.'
