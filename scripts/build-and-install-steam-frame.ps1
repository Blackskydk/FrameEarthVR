[CmdletBinding()]
param(
    [string]$DeviceHost = 'frame',
    [switch]$Usb,
    [switch]$Release,
    [switch]$NoLaunch,
    [switch]$KeepEditorClosed,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectVersionPath = Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'
$projectVersionText = Get-Content -LiteralPath $projectVersionPath -Raw
$unityVersionMatch = [regex]::Match($projectVersionText, 'm_EditorVersion:\s*([^\r\n]+)')

if (-not $unityVersionMatch.Success) {
    throw "Could not read the Unity version from '$projectVersionPath'."
}

$unityVersion = $unityVersionMatch.Groups[1].Value.Trim()
$unityEditor = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
$androidPlayer = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion\Editor\Data\PlaybackEngines\AndroidPlayer"
$apkPath = Join-Path $projectRoot 'Builds\SteamFrame\FrameEarthVR.apk'
$buildLog = Join-Path $projectRoot 'Logs\SteamFrameBuild.log'
$projectLock = Join-Path $projectRoot 'Temp\UnityLockfile'
$installScript = Join-Path $PSScriptRoot 'install-steam-frame.ps1'

if (-not (Test-Path -LiteralPath $unityEditor)) {
    throw "Unity $unityVersion was not found at '$unityEditor'."
}

if (-not (Test-Path -LiteralPath $androidPlayer)) {
    throw "Android Build Support is not installed for Unity $unityVersion."
}

if (-not (Test-Path -LiteralPath $installScript)) {
    throw "The install helper was not found at '$installScript'."
}

if ($ValidateOnly) {
    Write-Host "Ready: Unity $unityVersion, Android Build Support, and the install helper were found."
    return
}

$reopenEditor = $false
if (Test-Path -LiteralPath $projectLock) {
    $reopenEditor = -not $KeepEditorClosed
    Write-Host 'The project is currently open in Unity.' -ForegroundColor Yellow
    Write-Host 'Save your work and close Unity, then return to this window.'
    Read-Host 'Press Enter after Unity has closed' | Out-Null

    $closeDeadline = (Get-Date).AddSeconds(30)
    while ((Test-Path -LiteralPath $projectLock) -and (Get-Date) -lt $closeDeadline) {
        Start-Sleep -Milliseconds 500
    }
    if (Test-Path -LiteralPath $projectLock) {
        throw 'Unity is still using the project. Close it completely and run this script again.'
    }
}

$logDirectory = Split-Path -Parent $buildLog
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

$previousApk = Get-Item -LiteralPath $apkPath -ErrorAction SilentlyContinue
$previousWriteTime = if ($previousApk) { $previousApk.LastWriteTimeUtc } else { $null }
$buildMethod = if ($Release) {
    'EarthVR.Editor.SteamFrameBuild.BuildReleaseApk'
}
else {
    'EarthVR.Editor.SteamFrameBuild.BuildDevelopmentApk'
}
$buildLabel = if ($Release) { 'release' } else { 'development' }

Write-Host "Building the Steam Frame $buildLabel APK with Unity $unityVersion..."
Write-Host "Build log: $buildLog"

$unityArguments = @(
    '-batchmode'
    '-nographics'
    '-projectPath'
    "`"$projectRoot`""
    '-executeMethod'
    $buildMethod
    '-quit'
    '-logFile'
    "`"$buildLog`""
)

$buildProcess = Start-Process -FilePath $unityEditor -ArgumentList $unityArguments -Wait -PassThru
if ($buildProcess.ExitCode -ne 0) {
    throw "Unity build failed with exit code $($buildProcess.ExitCode). See '$buildLog'."
}

$builtApk = Get-Item -LiteralPath $apkPath -ErrorAction SilentlyContinue
if (-not $builtApk) {
    throw "Unity exited successfully, but no APK was created at '$apkPath'. See '$buildLog'."
}

if ($previousWriteTime -and $builtApk.LastWriteTimeUtc -le $previousWriteTime) {
    throw "The APK was not updated, so it will not be installed. See '$buildLog'."
}

Write-Host ("Build complete: {0:N1} MiB" -f ($builtApk.Length / 1MB))

$installArguments = @{
    ApkPath = $builtApk.FullName
}
if ($Usb) {
    $installArguments.Usb = $true
}
else {
    $installArguments.DeviceHost = $DeviceHost
}
if ($NoLaunch) {
    $installArguments.NoLaunch = $true
}

& $installScript @installArguments
if ($LASTEXITCODE -ne 0) {
    throw "Steam Frame installation failed with exit code $LASTEXITCODE."
}

if ($reopenEditor) {
    Write-Host 'Reopening the project in Unity...'
    Start-Process -FilePath $unityEditor -ArgumentList @('-projectPath', "`"$projectRoot`"") | Out-Null
}

Write-Host 'Steam Frame build and installation completed successfully.' -ForegroundColor Green
