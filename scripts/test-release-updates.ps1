$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets/EarthVR/Core/ReleaseVersion.cs'),
    (Join-Path $projectRoot 'Assets/EarthVR/Core/ReleaseUpdatePolicy.cs')
)
$checks = 0
function Assert-Update($condition, $message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
function Parse-Version($text) {
    $parsed = $null
    if (-not [EarthVR.Core.ReleaseVersion]::TryParse($text, [ref]$parsed)) { throw "Invalid version: $text" }
    return $parsed
}
foreach ($pair in @(
    @('1.0.0-preview.9', 'v1.0.0-preview.10'),
    @('1.0.0-preview.10', '1.0.0'),
    @('1.9.9', '1.10.0'),
    @('1.0.0-alpha', '1.0.0-alpha.1'),
    @('1.0.0-1', '1.0.0-alpha')
)) {
    Assert-Update ((Parse-Version $pair[0]).CompareTo((Parse-Version $pair[1])) -lt 0) 'Version ordering failed'
}
Assert-Update ((Parse-Version '1.0.0+build1').CompareTo((Parse-Version 'v1.0.0+build2')) -eq 0) 'Metadata changes precedence'
foreach ($invalid in @('1.0', '1.01.0', '1.0.0-preview.01', '1.0.0-', 'garbage')) {
    $parsed = $null
    Assert-Update (-not [EarthVR.Core.ReleaseVersion]::TryParse($invalid, [ref]$parsed)) 'Invalid version accepted'
}
function New-Release($version, $preview = $false) {
    $release = [EarthVR.Core.PublishedRelease]::new()
    $release.tag_name = $version
    $release.prerelease = $preview
    $release.html_url = "https://github.com/Blackskydk/FrameEarthVR/releases/tag/$version"
    $asset = [EarthVR.Core.ReleaseAsset]::new()
    $asset.name = 'FrameEarthVR.apk'
    $asset.state = 'uploaded'
    $asset.size = 100
    $asset.browser_download_url = "https://github.com/Blackskydk/FrameEarthVR/releases/download/$version/FrameEarthVR.apk"
    $release.assets = @($asset)
    return $release
}
$stable = New-Release 'v1.0.0'
$preview = New-Release 'v1.1.0-preview.10' $true
Assert-Update ([EarthVR.Core.ReleaseUpdatePolicy]::FindUpdate('1.0.0-preview.2', @($stable, $preview)) -eq $preview) 'Preview channel lost newer preview'
Assert-Update ($null -eq [EarthVR.Core.ReleaseUpdatePolicy]::FindUpdate('1.0.0', @($preview, $stable))) 'Stable channel offered preview or same version'
Assert-Update ([EarthVR.Core.ReleaseUpdatePolicy]::FindUpdate('1.0.0-preview.2', @($stable)) -eq $stable) 'Preview failed to offer stable release'
foreach ($mode in @('draft', 'missing', 'empty', 'uploading', 'hostile', 'wrongRepo')) {
    $release = New-Release 'v2.0.0'
    switch ($mode) {
        'draft' { $release.draft = $true }
        'missing' { $release.assets = @() }
        'empty' { $release.assets[0].size = 0 }
        'uploading' { $release.assets[0].state = 'new' }
        'hostile' { $release.html_url = 'https://github.com.attacker.test/Blackskydk/FrameEarthVR/releases/tag/v2.0.0' }
        'wrongRepo' { $release.assets[0].browser_download_url = 'https://github.com/other/repo/releases/download/v2.0.0/FrameEarthVR.apk' }
    }
    Assert-Update ($null -eq [EarthVR.Core.ReleaseUpdatePolicy]::FindUpdate('1.0.0', @($release))) "Rejected release offered: $mode"
}
Assert-Update ($null -eq [EarthVR.Core.ReleaseUpdatePolicy]::FindUpdate('1.0.0', $null)) 'Empty feed offered update'
$windows = New-Release 'v2.0.0'
$windows.assets[0].name = 'FrameEarthVR-Windows.zip'
$windows.assets[0].browser_download_url = 'https://github.com/Blackskydk/FrameEarthVR/releases/download/v2.0.0/FrameEarthVR-Windows.zip'
Assert-Update ($null -eq [EarthVR.Core.ReleaseUpdatePolicy]::FindUpdate('1.0.0', @($windows))) 'APK build offered Windows archive'
Assert-Update ([EarthVR.Core.ReleaseUpdatePolicy]::FindUpdate('1.0.0', @($windows), 'FrameEarthVR-Windows.zip') -eq $windows) 'Windows build missed matching update'
Assert-Update ($null -eq [EarthVR.Core.ReleaseUpdatePolicy]::FindUpdate('1.0.0-preview.2', @($stable), 'FrameEarthVR-Windows.zip')) 'Windows build offered APK'
Assert-Update (-not [EarthVR.Core.ReleaseUpdatePolicy]::HasDigest($windows.assets[0])) 'Missing digest accepted'
$windows.assets[0].digest = 'sha256:' + ('a' * 64)
Assert-Update ([EarthVR.Core.ReleaseUpdatePolicy]::HasDigest($windows.assets[0])) 'Valid SHA-256 digest rejected'
$windows.assets[0].digest = 'sha256:invalid'
Assert-Update (-not [EarthVR.Core.ReleaseUpdatePolicy]::HasDigest($windows.assets[0])) 'Invalid SHA-256 digest accepted'
Write-Host "$checks release update checks passed."
