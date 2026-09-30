[CmdletBinding()]
param([Parameter(Mandatory)][string]$Inbox, [Parameter(Mandatory)][string]$InstallRoot,
      [Parameter(Mandatory)][int]$GamePid)
$ErrorActionPreference = 'Stop'
$identity = ''
$inboxRoot = (Resolve-Path -LiteralPath $Inbox).Path
function Write-Result($state, $message) {
    $resultPath = Join-Path $inboxRoot 'result.json'
    @{ id = $identity; state = $state; message = $message } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath ($resultPath + '.tmp') -Encoding UTF8
    Move-Item -LiteralPath ($resultPath + '.tmp') -Destination $resultPath -Force
}
$stage = $null
$stageCreated = $false
try {
    $root = (Resolve-Path -LiteralPath $InstallRoot).Path.TrimEnd('\')
    if (-not (Test-Path -LiteralPath (Join-Path $root 'FrameEarthVR.exe')) -or
        $root -eq [IO.Path]::GetPathRoot($root).TrimEnd('\') -or
        ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Invalid game installation folder'
    }
    $requestPath = Join-Path $inboxRoot 'request.json'
    $request = Get-Content -LiteralPath $requestPath -Raw | ConvertFrom-Json
    $identity = $request.id
    if ($identity -notmatch '\A[0-9a-f]{32}\z' -or $request.schema -ne 1 -or $request.platform -ne 'windows' -or
        $request.version -notmatch '\Av?[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?\z') { throw 'Invalid update request' }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $release = Invoke-RestMethod -Uri ('https://api.github.com/repos/Blackskydk/FrameEarthVR/releases/tags/' + $request.version) -Headers @{ 'User-Agent' = 'FrameEarthVR-Updater' } -TimeoutSec 15
    $asset = @($release.assets | Where-Object { $_.name -eq 'FrameEarthVR-Windows.zip' -and $_.state -eq 'uploaded' })
    if ($release.draft -or $release.tag_name -ne $request.version -or $asset.Count -ne 1 -or
        $asset[0].digest -notmatch '\Asha256:[0-9a-f]{64}\z' -or $asset[0].digest -ne $request.digest) {
        throw 'Published Windows update could not be verified'
    }
    $payload = Join-Path $inboxRoot 'payload.zip'
    if ((Get-Item -LiteralPath $payload).Length -ne $asset[0].size -or
        'sha256:' + (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset[0].digest) {
        throw 'Download checksum mismatch'
    }
    $allowed = @('FrameEarthVR.exe', 'FrameEarthVR_Data', 'UnityPlayer.dll', 'GameAssembly.dll',
                 'MonoBleedingEdge', 'UnityCrashHandler64.exe', 'D3D12', 'earthvr-version.txt')
    $stage = Join-Path (Split-Path -Parent $root) ('.earthvr-stage-' + $identity)
    if (Test-Path -LiteralPath $stage) { throw 'Update staging folder already exists' }
    New-Item -ItemType Directory -Path $stage | Out-Null
    $stageCreated = $true
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($payload)
    try {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $total = 0L
        if ($archive.Entries.Count -gt 50000) { throw 'Too many update files' }
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName
            $parts = $name.TrimEnd('/').Split('/')
            $total += $entry.Length
            if ($name.StartsWith('/') -or $name.Contains('\') -or $name.Contains(':') -or
                $parts[0] -notin $allowed -or ($parts | Where-Object { $_ -in @('', '.', '..') -or $_ -match '[ .]$|(?i)\.local\.json$' }) -or
                (($entry.ExternalAttributes -shr 16) -band 61440) -eq 40960 -or
                -not $seen.Add($name.TrimEnd('/')) -or $total -gt 8GB) { throw 'Unsafe update archive' }
        }
        foreach ($required in @('FrameEarthVR.exe', 'UnityPlayer.dll')) {
            if (-not $seen.Contains($required)) { throw 'Incomplete Windows update' }
        }
        if (-not $seen.Contains('FrameEarthVR_Data/globalgamemanagers') -and -not $seen.Contains('FrameEarthVR_Data/data.unity3d')) {
            throw 'Windows update is missing its Unity data'
        }
        foreach ($entry in $archive.Entries) {
            $destination = Join-Path $stage ($entry.FullName.Replace('/', '\'))
            if ($entry.FullName.EndsWith('/')) { New-Item -ItemType Directory -Force -Path $destination | Out-Null }
            else {
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination)
            }
        }
    }
    finally { $archive.Dispose() }
    $gameProcess = Get-Process -Id $GamePid -ErrorAction SilentlyContinue
    if ($gameProcess -and $gameProcess.Path -ne (Join-Path $root 'FrameEarthVR.exe')) { throw 'Game process does not match installation' }
    Write-Result 'ready' 'Verified update; waiting for game to close'
    if ($gameProcess -and -not $gameProcess.WaitForExit(120000)) { throw 'Game did not exit; nothing installed' }
    if (-not (Test-Path -LiteralPath $requestPath)) { throw 'Update request was cancelled' }
    $backup = Join-Path $root '.earthvr-previous'
    # Absolute backup stays within the already-validated installation folder.
    if (Test-Path -LiteralPath $backup) {
        if ((Get-Item -LiteralPath $backup).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unsafe backup folder' }
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
    New-Item -ItemType Directory -Path $backup | Out-Null
    $oldFiles = @()
    $newFiles = @()
    try {
        foreach ($name in $allowed) {
            $target = Join-Path $root $name
            if (Test-Path -LiteralPath $target) {
                Move-Item -LiteralPath $target -Destination (Join-Path $backup $name)
                $oldFiles += $name
            }
        }
        foreach ($item in Get-ChildItem -LiteralPath $stage) {
            Move-Item -LiteralPath $item.FullName -Destination (Join-Path $root $item.Name)
            $newFiles += $item.Name
        }
    }
    catch {
        foreach ($name in $newFiles) { Remove-Item -LiteralPath (Join-Path $root $name) -Recurse -Force }
        foreach ($name in $oldFiles) { Move-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $root $name) }
        throw
    }
    Write-Result 'installed' 'Update installed'
    try { Start-Process -FilePath (Join-Path $root 'FrameEarthVR.exe') -WorkingDirectory $root -WindowStyle Hidden }
    catch { Write-Result 'installed' 'Update installed; reopen the game from your library' }
}
catch { Write-Result 'error' $_.Exception.Message }
finally {
    if ($stageCreated -and (Test-Path -LiteralPath $stage)) { Remove-Item -LiteralPath $stage -Recurse -Force }
    Remove-Item -LiteralPath (Join-Path $inboxRoot 'request.json') -ErrorAction SilentlyContinue
}
