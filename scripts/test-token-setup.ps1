$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'setup-token.ps1')
$folder = Join-Path ([IO.Path]::GetTempPath()) ('earthvr-token-test-' + [Guid]::NewGuid().ToString('N'))
try {
    Save-EarthVRToken $folder 'test-user-token-one'
    $path = Join-Path $folder 'cesium-ion.local.json'
    if ((Get-Content $path -Raw | ConvertFrom-Json).accessToken -ne 'test-user-token-one') { throw 'Initial token save failed.' }
    Save-EarthVRToken $folder 'test-user-token-two'
    if ((Get-Content $path -Raw | ConvertFrom-Json).accessToken -ne 'test-user-token-two') { throw 'Token replacement failed.' }
    foreach ($invalid in @('', 'two words', 'REPLACE_WITH_TOKEN', ('a' * 8193))) {
        $rejected = $false
        try { Save-EarthVRToken $folder $invalid } catch { $rejected = $true }
        if (-not $rejected) { throw 'Invalid token accepted.' }
        if ((Get-Content $path -Raw | ConvertFrom-Json).accessToken -ne 'test-user-token-two') { throw 'Invalid save damaged the previous token.' }
    }
    if (@(Get-ChildItem $folder -Filter '*.tmp').Count) { throw 'Temporary credential file leaked.' }
    $errors = $null; $tokens = $null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'setup-token.ps1'), [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw 'Setup script parser errors.' }
    Write-Host '8 token setup checks passed.'
}
finally {
    # Only the freshly created, unique test folder is removed.
    if ([IO.Directory]::Exists($folder)) { [IO.Directory]::Delete($folder, $true) }
}
