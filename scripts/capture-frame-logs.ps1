<#
.SYNOPSIS
Captures a self-describing report from the Steam Frame for debugging.

.DESCRIPTION
Connects over adb, optionally pushes a settings-override.json (or removes it) and
grants the game's pending runtime permissions, relaunches Frame Earth VR, waits, then
writes ONE report to the Logs folder:

  frame-report-<time>-<label>.txt

The report records the exact parameters used, the override file on the headset, the
installed version, the game's own session log (the settings it booted with, XR runtime
facts and a performance line every 10 seconds) and the relevant device log lines.
Attach that single file to the chat. The complete device log is saved alongside it.

.PARAMETER Label
A short name for the test, e.g. london-flat-lighting. It is written into the report.

.PARAMETER Override
A JSON object of settings to push before launching, e.g. '{"standaloneMsaa": 1}'.

.PARAMETER GrantPermissions
Grants the game's pending runtime permissions over adb so no dialog blocks startup.

.EXAMPLE
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -Label baseline

.EXAMPLE
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -Label flat-light -Override '{"standaloneFlatTileLighting": true}'

.EXAMPLE
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -ClearOverride -Label baseline
#>
[CmdletBinding()]
param(
    [string]$DeviceHost = 'frame',
    [switch]$Usb,
    [int]$Seconds = 120,
    [string]$Label,
    [string]$Override,
    [switch]$ClearOverride,
    [switch]$GrantPermissions,
    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'

$package = 'com.frameearthvr.app'
$dataFolder = "/sdcard/Android/data/$package/files/EarthVR"
$overridePath = "$dataFolder/settings-override.json"
$sessionLogPath = "$dataFolder/session-log.txt"

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

function Get-ThermalSnapshot {
    # Best effort: the headset may hide some of these. A hot device slows down, which
    # makes later runs look worse regardless of the settings under test.
    $snapshot = @()
    try {
        $battery = @(& $adb -s $serial shell dumpsys battery) | Where-Object { $_ -match 'level|temperature' } | ForEach-Object { $_.Trim() }
        $snapshot += "battery: $($battery -join '; ')  (temperature is in tenths of a degree C)"
    }
    catch {
        $snapshot += 'battery: unavailable'
    }
    try {
        $zones = @(& $adb -s $serial shell 'for z in /sys/class/thermal/thermal_zone*; do echo $(cat $z/type) $(cat $z/temp); done' 2>$null)
        if ($zones.Count -gt 0) {
            $snapshot += 'thermal zones (type, millidegrees C):'
            $snapshot += ($zones | ForEach-Object { "  $_" })
        }
    }
    catch {
        $snapshot += 'thermal zones: unavailable'
    }
    return $snapshot
}

# Which build is installed, and which runtime permissions does it hold?
$packageInfo = @(& $adb -s $serial shell dumpsys package $package)
$packageLines = @($packageInfo | Where-Object { $_ -match 'versionName=|versionCode=|lastUpdateTime=' } | Select-Object -First 3 | ForEach-Object { $_.Trim() })
if ($packageLines.Count -eq 0) {
    Write-Warning "$package does not appear to be installed."
}
else {
    $packageLines | ForEach-Object { Write-Host "Installed: $_" }
}
$permissionLines = @($packageInfo | Where-Object { $_ -match '^\s*[A-Za-z0-9_.]+:\s*granted=' } | ForEach-Object { $_.Trim() })
if ($GrantPermissions) {
    foreach ($line in $permissionLines) {
        if ($line -match '^([A-Za-z0-9_.]+):\s*granted=false') {
            $permissionName = $Matches[1]
            Write-Host "Granting $permissionName"
            & $adb -s $serial shell pm grant $package $permissionName | Out-Null
        }
    }
    $packageInfo = @(& $adb -s $serial shell dumpsys package $package)
    $permissionLines = @($packageInfo | Where-Object { $_ -match '^\s*[A-Za-z0-9_.]+:\s*granted=' } | ForEach-Object { $_.Trim() })
}

# Optional settings override (read by the game at launch).
if ($ClearOverride) {
    & $adb -s $serial shell rm -f $overridePath | Out-Null
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
    Write-Host "Pushed settings-override.json: $Override"
}

$thermalBefore = Get-ThermalSnapshot

if (-not $NoRestart) {
    # A larger log buffer: the default one can overwrite the startup lines.
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

Write-Host "Collecting for $Seconds seconds. Put the headset on, accept any permission dialog (the game waits for it), and go to the place with the problem."
Start-Sleep -Seconds $Seconds

$thermalAfter = Get-ThermalSnapshot
$stillRunning = (@(& $adb -s $serial shell pidof $package) -join '').Trim()
if (-not $stillRunning) {
    Write-Warning 'The game is not running any more: it exited or crashed during the capture.'
}

$logDirectory = Join-Path $projectRoot 'Logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$safeLabel = if ($Label) { '-' + ($Label -replace '[^A-Za-z0-9_-]', '_') } else { '' }
$reportPath = Join-Path $logDirectory "frame-report-$stamp$safeLabel.txt"
$fullPath = Join-Path $logDirectory "frame-logcat-$stamp$safeLabel.txt"
$crashPath = Join-Path $logDirectory "frame-logcat-$stamp$safeLabel-crash.txt"

& $adb -s $serial logcat -d -v threadtime | Out-File -FilePath $fullPath -Encoding utf8
& $adb -s $serial logcat -b crash -d -v threadtime | Out-File -FilePath $crashPath -Encoding utf8

# Facts about the device and the override that is actually on it now.
$model = (@(& $adb -s $serial shell getprop ro.product.model) -join '').Trim()
$androidRelease = (@(& $adb -s $serial shell getprop ro.build.version.release) -join '').Trim()
$deviceOverride = 'none'
try {
    $overrideOnDevice = (@(& $adb -s $serial shell cat $overridePath 2>$null) -join ' ').Trim()
    if ($overrideOnDevice -and $overrideOnDevice -notmatch 'No such file') { $deviceOverride = $overrideOnDevice }
}
catch {
    $deviceOverride = 'none'
}

# The game's own log for this launch.
$sessionLocal = Join-Path ([IO.Path]::GetTempPath()) "frame-session-log-$stamp.txt"
& $adb -s $serial pull $sessionLogPath $sessionLocal | Out-Null
if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $sessionLocal)) {
    $sessionLines = @(Get-Content -LiteralPath $sessionLocal)
}
else {
    $sessionLines = @('(the session log could not be pulled; the installed build may predate it, or the game never got past the permission dialog)')
}

$pattern = 'EarthVR|\[XR\]|OpenXR|XR_|foveat|gaze|FDM|VALVE|Valve|Lepton|libVkLayer|AndroidRuntime|FATAL|Fatal signal|SIGSEGV|SIGABRT|backtrace|am_crash|am_proc_died|ANR|REQUEST_PERMISSIONS'
$filteredLines = @(Select-String -Path $fullPath -Pattern $pattern | ForEach-Object { $_.Line })

$labelText = if ($Label) { $Label } else { '(none)' }
$overrideText = if ($Override) { $Override } else { '(none)' }
$runningText = if ($stillRunning) { 'yes' } else { 'NO - exited or crashed' }
$report = @()
$report += '=== FRAME EARTH VR CAPTURE REPORT ==='
$report += "Label: $labelText"
$report += "Captured: $stamp (PC time)"
$report += "Script parameters: DeviceHost=$DeviceHost Usb=$($Usb.IsPresent) Seconds=$Seconds Override=$overrideText ClearOverride=$($ClearOverride.IsPresent) GrantPermissions=$($GrantPermissions.IsPresent) NoRestart=$($NoRestart.IsPresent)"
$report += "Override file on the headset at capture time: $deviceOverride"
$report += "Installed: $($packageLines -join ' | ')"
$report += "Device: $model, Android $androidRelease"
$report += "Game still running at the end: $runningText"
$report += 'Runtime permissions:'
$report += ($permissionLines | ForEach-Object { "  $_" })
$report += ''
$report += '=== DEVICE THERMAL STATE ==='
$report += 'Before launch:'
$report += ($thermalBefore | ForEach-Object { "  $_" })
$report += 'After the capture:'
$report += ($thermalAfter | ForEach-Object { "  $_" })
$report += ''
$report += '=== SESSION LOG (written by the game) ==='
$report += $sessionLines
$report += ''
$report += '=== DEVICE LOG (filtered) ==='
$report += $filteredLines
$report | Set-Content -Path $reportPath -Encoding UTF8

Write-Host ''
Write-Host "Report: $reportPath"
Write-Host "Full device log: $fullPath"
$crashLines = @(Select-String -Path $fullPath -Pattern 'FATAL EXCEPTION|Fatal signal|SIGSEGV|SIGABRT|am_crash' | ForEach-Object { $_.Line })
if ($crashLines.Count -gt 0) {
    Write-Host ''
    Write-Warning 'The device log contains crash markers. First lines:'
    $crashLines | Select-Object -First 6 | ForEach-Object { Write-Host $_ }
    Write-Host "Crash buffer: $crashPath (attach it too)"
}
Write-Host ''
Write-Host 'Last performance lines from the game:'
$sessionLines | Where-Object { $_ -match 'perf avg' } | Select-Object -Last 3 | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host 'Attach the report file to the chat.'
