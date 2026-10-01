<#
.SYNOPSIS
Collects debugging logs from the Steam Frame in one step.

.DESCRIPTION
Connects over adb, optionally pushes a settings-override.json (or removes it),
relaunches Frame Earth VR, waits, then saves the device log to the Logs folder:
  frame-logcat-<time>.txt            the complete log
  frame-logcat-<time>-filtered.txt   only lines about EarthVR, Unity, OpenXR, foveation,
                                     eye/gaze, Valve/Lepton and crashes (attach this one)

.EXAMPLE
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138

.EXAMPLE
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -Override '{"standaloneMsaa": 1}'

.EXAMPLE
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -ClearOverride
#>
[CmdletBinding()]
param(
    [string]$DeviceHost = 'frame',
    [switch]$Usb,
    [int]$Seconds = 120,
    [string]$Override,
    [switch]$ClearOverride,
    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'

$package = 'com.frameearthvr.app'
$dataFolder = "/sdcard/Android/data/$package/files/EarthVR"
$overridePath = "$dataFolder/settings-override.json"

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectVersionText = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt') -Raw
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
    throw "adb was not found. Add Android Build Support to Unity $unityVersion in Unity Hub."
}

if ($Override) {
    try {
        $null = $Override | ConvertFrom-Json
    }
    catch {
        throw "-Override is not valid JSON: $($_.Exception.Message)"
    }
}

& $adb start-server | Out-Null

if ($Usb) {
    & $adb forward tcp:5555 tcp:5555 | Out-Null
    $serial = 'localhost:5555'
}
else {
    $serial = "$DeviceHost`:5555"
}

Write-Host "Connecting to Steam Frame at $serial..."
$connectOutput = @(& $adb connect $serial 2>&1)
$connectOutput | ForEach-Object { Write-Host $_ }
if (($connectOutput -join "`n") -notmatch '(?im)^(already )?connected to ') {
    throw "Could not connect to '$serial'. Launch Lepton Development on the headset and check the address."
}

# Which build is installed?
$packageInfo = @(& $adb -s $serial shell dumpsys package $package)
$packageLines = @($packageInfo | Where-Object { $_ -match 'versionName=|versionCode=|lastUpdateTime=' } | Select-Object -First 3 | ForEach-Object { $_.Trim() })
if ($packageLines.Count -eq 0) {
    Write-Warning "$package does not appear to be installed."
}
else {
    $packageLines | ForEach-Object { Write-Host "Installed: $_" }
}

# Optional settings override (read by the game at launch).
$overrideState = 'unchanged'
if ($ClearOverride) {
    & $adb -s $serial shell rm -f $overridePath | Out-Null
    $overrideState = 'cleared'
    Write-Host 'Removed settings-override.json from the headset.'
}
if ($Override) {
    $temporary = Join-Path ([IO.Path]::GetTempPath()) 'settings-override.json'
    # ASCII without a byte-order mark: a BOM makes the game's JSON parser reject the file.
    [IO.File]::WriteAllText($temporary, $Override, (New-Object System.Text.ASCIIEncoding))
    & $adb -s $serial shell mkdir -p $dataFolder | Out-Null
    & $adb -s $serial push $temporary $overridePath
    if ($LASTEXITCODE -ne 0) {
        throw "Could not push the override to $overridePath. Tell Claude the exact error above."
    }
    $overrideState = $Override
    Write-Host "Pushed settings-override.json: $Override"
}

if (-not $NoRestart) {
    # A larger log buffer: the game logs hundreds of warnings a minute and the default
    # buffer overwrites the startup lines. Ignored if the device refuses.
    & $adb -s $serial logcat -G 16M | Out-Null
    & $adb -s $serial logcat -c
    & $adb -s $serial shell am force-stop $package
    Start-Sleep -Seconds 2
    # Launch exactly as install-steam-frame.ps1 does. (monkey injects a random
    # input event and can close the game.)
    $resolvedActivities = & $adb -s $serial shell cmd package resolve-activity --brief `
        -c android.intent.category.LAUNCHER $package
    $launcherActivity = $resolvedActivities |
        Where-Object { $_ -match '^com\.frameearthvr\.app/.+' } |
        Select-Object -Last 1
    if (-not $launcherActivity) {
        throw "Could not find the launcher activity for $package."
    }
    & $adb -s $serial shell am start -W -n $launcherActivity | Out-Null
    Write-Host "Relaunched $launcherActivity."
}

Write-Host "Collecting for $Seconds seconds. Put the headset on, ACCEPT any permission dialog that appears (the game waits for it), and go to the place with the problem."
Start-Sleep -Seconds $Seconds

$stillRunning = @(& $adb -s $serial shell pidof $package) -join ''
if (-not $stillRunning.Trim()) {
    Write-Warning 'The game is not running any more: it exited or crashed during the capture.'
}

$logDirectory = Join-Path $projectRoot 'Logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$fullPath = Join-Path $logDirectory "frame-logcat-$stamp.txt"
$filteredPath = Join-Path $logDirectory "frame-logcat-$stamp-filtered.txt"

& $adb -s $serial logcat -d -v threadtime | Out-File -FilePath $fullPath -Encoding utf8
$crashPath = Join-Path $logDirectory "frame-logcat-$stamp-crash.txt"
& $adb -s $serial logcat -b crash -d -v threadtime | Out-File -FilePath $crashPath -Encoding utf8

$pattern = 'EarthVR|Unity|OpenXR|XR_|foveat|gaze|FDM|VALVE|Valve|Lepton|libVkLayer|AndroidRuntime|FATAL|crash|DEBUG|Fatal signal|SIGSEGV|SIGABRT|backtrace|am_crash|am_proc_died|ANR'
$header = @(
    "Captured: $stamp",
    "Package: $($packageLines -join ' | ')",
    "Settings override: $overrideState",
    ''
)
$filteredLines = @(Select-String -Path $fullPath -Pattern $pattern | ForEach-Object { $_.Line })
($header + $filteredLines) | Set-Content -Path $filteredPath -Encoding utf8

Write-Host ''
Write-Host "Saved: $filteredPath ($($filteredLines.Count) lines)"
Write-Host "Saved: $fullPath"
$crashLines = @(Select-String -Path $fullPath -Pattern 'FATAL EXCEPTION|Fatal signal|SIGSEGV|SIGABRT|am_crash' | ForEach-Object { $_.Line })
if ($crashLines.Count -gt 0) {
    Write-Host ''
    Write-Warning 'The log contains crash markers. First lines:'
    $crashLines | Select-Object -First 6 | ForEach-Object { Write-Host $_ }
    Write-Host "Crash buffer saved: $crashPath (attach it too)"
}
Write-Host 'Attach the -filtered file to the chat. Key lines start with "EarthVR".'
Write-Host ''
Select-String -Path $filteredPath -Pattern 'EarthVR' | Select-Object -First 12 | ForEach-Object { Write-Host $_.Line }
