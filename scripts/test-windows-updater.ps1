$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$fixtureRoot = Join-Path $projectRoot ('Logs/Verification/UpdaterTests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$script:checks = 0
function Invoke-RestMethod { param($Uri, $Headers, $TimeoutSec) return $global:earthvrFixtureRelease }
function Start-Process { param($FilePath, $WorkingDirectory, $WindowStyle) }
function Move-Item {
    param($LiteralPath, $Destination, [switch]$Force)
    if ($global:earthvrMoveFail -and $Destination -eq $global:earthvrFailTarget) {
        $global:earthvrMoveFail = $false
        throw 'Simulated file move failure'
    }
    Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force
}
function Assert-Update($condition, $message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
foreach ($mode in @('valid', 'compressed', 'corrupt', 'unsafe', 'missing', 'existingStage', 'rollback')) {
    $case = Join-Path $fixtureRoot $mode
    $game = Join-Path $case 'game'
    $inbox = Join-Path $case 'inbox'
    New-Item -ItemType Directory -Path $game, $inbox -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $game 'FrameEarthVR.exe') -Value 'old' -NoNewline
    Set-Content -LiteralPath (Join-Path $game 'user-file.txt') -Value 'preserve' -NoNewline
    $payload = Join-Path $inbox 'payload.zip'
    $zip = [IO.Compression.ZipFile]::Open($payload, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $names = @('FrameEarthVR.exe', 'UnityPlayer.dll', 'FrameEarthVR_Data/globalgamemanagers')
        if ($mode -eq 'compressed') { $names = @('FrameEarthVR.exe', 'UnityPlayer.dll', 'FrameEarthVR_Data/data.unity3d') }
        if ($mode -eq 'unsafe') { $names += '../escape.txt' }
        if ($mode -eq 'missing') { $names = @('FrameEarthVR.exe') }
        foreach ($name in $names) {
            $entry = $zip.CreateEntry($name)
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write('new') } finally { $writer.Dispose() }
        }
    }
    finally { $zip.Dispose() }
    $digest = 'sha256:' + (Get-FileHash -LiteralPath $payload).Hash.ToLowerInvariant()
    $global:earthvrFixtureRelease = @{ draft = $false; tag_name = 'v1.0.0-preview.4'; assets = @(@{
        name = 'FrameEarthVR-Windows.zip'; state = 'uploaded'; digest = $digest; size = (Get-Item $payload).Length
    }) }
    @{ schema = 1; id = ('a' * 32); platform = 'windows'; version = 'v1.0.0-preview.4'; digest = $digest } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $inbox 'request.json') -Encoding UTF8
    if ($mode -eq 'corrupt') { Add-Content -LiteralPath $payload 'corruption' }
    if ($mode -eq 'existingStage') {
        $existingStage = Join-Path $case ('.earthvr-stage-' + ('a' * 32))
        New-Item -ItemType Directory -Path $existingStage | Out-Null
        Set-Content -LiteralPath (Join-Path $existingStage 'sentinel') -Value 'keep'
    }
    $global:earthvrMoveFail = $mode -eq 'rollback'
    $global:earthvrFailTarget = Join-Path $game 'UnityPlayer.dll'
    & (Join-Path $projectRoot 'Assets/StreamingAssets/EarthVR/ApplyWindowsUpdate.ps1') -Inbox $inbox -InstallRoot $game -GamePid 2147483000
    $result = Get-Content -LiteralPath (Join-Path $inbox 'result.json') -Raw | ConvertFrom-Json
    $gameContent = Get-Content -LiteralPath (Join-Path $game 'FrameEarthVR.exe') -Raw
    if ($mode -in @('valid', 'compressed')) {
        Assert-Update ($result.state -eq 'installed' -and $gameContent -eq 'new') 'Valid update was not installed'
        Assert-Update ((Get-Content -LiteralPath (Join-Path $game '.earthvr-previous/FrameEarthVR.exe') -Raw) -eq 'old') 'Old build was not backed up'
    }
    else { Assert-Update ($result.state -eq 'error' -and $gameContent -eq 'old') "Invalid $mode update changed the game" }
    Assert-Update ((Get-Content -LiteralPath (Join-Path $game 'user-file.txt') -Raw) -eq 'preserve') 'User file changed'
    Assert-Update (-not (Test-Path -LiteralPath (Join-Path $inbox 'request.json'))) 'Update request was not consumed'
    if ($mode -eq 'existingStage') { Assert-Update (Test-Path -LiteralPath (Join-Path $existingStage 'sentinel')) 'Preexisting staging folder was deleted' }
}
Write-Host "$script:checks Windows updater checks passed. Fixtures: $fixtureRoot"
