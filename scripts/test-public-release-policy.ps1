$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not ('EarthVR.Editor.PublicReleasePolicy' -as [type])) {
    Add-Type -TypeDefinition (Get-Content (Join-Path $projectRoot 'Assets/EarthVR/Editor/PublicReleasePolicy.cs') -Raw)
}
$cases = @(
    @{ text = "  ionAccessToken: `r`n  otherProperty: 1"; expected = $false },
    @{ text = '  defaultIonAccessToken: ""'; expected = $false },
    @{ text = "  accessToken: ''"; expected = $false },
    @{ text = '  apiKey: null'; expected = $false },
    @{ text = '  ionAccessToken: example-test-secret'; expected = $true },
    @{ text = '  _defaultIonAccessToken: "example-test-secret"'; expected = $true },
    @{ text = "  apiKey: 'example-test-secret'"; expected = $true }
)
foreach ($case in $cases) {
    if ([EarthVR.Editor.PublicReleasePolicy]::ContainsSerializedCredential($case.text) -ne $case.expected) {
        throw 'Serialized credential guard regression test failed.'
    }
}
Write-Host "$($cases.Count) serialized credential guard tests passed."
