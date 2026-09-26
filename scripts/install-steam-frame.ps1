[CmdletBinding()]
param(
    [string]$ApkPath = (Join-Path $PSScriptRoot '..\Builds\SteamFrame\FrameEarthVR.apk'),
    [string]$DeviceHost = 'frame',
    [switch]$Usb,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectVersionPath = Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt'
$projectVersionText = Get-Content -LiteralPath $projectVersionPath -Raw
$unityVersionMatch = [regex]::Match($projectVersionText, 'm_EditorVersion:\s*([^\r\n]+)')
$unityVersion = if ($unityVersionMatch.Success) { $unityVersionMatch.Groups[1].Value.Trim() } else { '' }

$adbCandidates = @()
if ($unityVersion) {
    $adbCandidates += Join-Path $env:ProgramFiles "Unity\Hub\Editor\$unityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
}
$pathAdb = Get-Command adb -ErrorAction SilentlyContinue
if ($pathAdb) {
    $adbCandidates += $pathAdb.Source
}
$adb = $adbCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $adb) {
    throw "adb was not found. Add Android Build Support, Android SDK & NDK Tools, and OpenJDK to Unity $unityVersion in Unity Hub."
}

$resolvedApk = (Resolve-Path -LiteralPath $ApkPath -ErrorAction SilentlyContinue).Path
if (-not $resolvedApk) {
    throw "APK not found at '$ApkPath'. In Unity, run EarthVR > Steam Frame > Build Development APK first."
}

& $adb start-server | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not start adb.'
}

if ($Usb) {
    & $adb forward tcp:5555 tcp:5555
    if ($LASTEXITCODE -ne 0) {
        throw 'USB forwarding failed. Confirm the headset is connected and Lepton Development is running.'
    }
    $serial = 'localhost:5555'
}
else {
    $serial = "$DeviceHost`:5555"
}

Write-Host "Connecting to Steam Frame at $serial..."
& $adb connect $serial
if ($LASTEXITCODE -ne 0) {
    throw 'adb could not connect. Enable Developer Mode and launch Lepton Development on the headset.'
}

& $adb -s $serial get-state | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Steam Frame is not available as adb device '$serial'. Run 'adb devices' to inspect the connection."
}

Write-Host "Installing $resolvedApk..."
& $adb -s $serial install -r $resolvedApk
if ($LASTEXITCODE -ne 0) {
    throw 'APK installation failed. Check the headset prompt and adb output above.'
}

if (-not $NoLaunch) {
    Write-Host 'Launching Frame Earth VR...'
    $resolvedActivities = & $adb -s $serial shell cmd package resolve-activity --brief `
        -c android.intent.category.LAUNCHER com.frameearthvr.app
    $resolveExitCode = $LASTEXITCODE
    $launcherActivity = $resolvedActivities |
        Where-Object { $_ -match '^com\.frameearthvr\.app/.+' } |
        Select-Object -Last 1

    if ($resolveExitCode -eq 0 -and $launcherActivity) {
        & $adb -s $serial shell am start -W -n $launcherActivity
    }
    else {
        & $adb -s $serial shell monkey -p com.frameearthvr.app -c android.intent.category.LAUNCHER 1 | Out-Null
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'The APK installed, but automatic launch failed. Start Frame Earth VR from the Lepton library.'
    }
}

Write-Host "Done. For live logs: `"$adb`" -s $serial logcat -s Unity"
