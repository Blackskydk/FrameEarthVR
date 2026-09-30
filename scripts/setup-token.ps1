[CmdletBinding()]
param(
    [ValidateSet('Windows', 'FrameApk', 'FrameWindows')][string]$Target = 'Windows',
    [string]$TokenFile,
    [string]$DeviceHost = 'frame',
    [string]$DevkitKey,
    [string]$DataFolder,
    [switch]$SkipAccessCheck
)
$ErrorActionPreference = 'Stop'

function Save-EarthVRToken([string]$Folder, [string]$Token) {
    if ([string]::IsNullOrWhiteSpace($Token) -or $Token.Length -gt 8192 -or $Token -match '\s|REPLACE_WITH') { throw 'Invalid token format.' }
    [IO.Directory]::CreateDirectory($Folder) | Out-Null
    $path = Join-Path $Folder 'cesium-ion.local.json'
    $temporary = Join-Path $Folder ([Guid]::NewGuid().ToString('N') + '.tmp')
    $backup = $temporary + '.bak'
    try {
        [IO.File]::WriteAllText($temporary, (@{accessToken=$Token} | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
        if ([IO.File]::Exists($path)) { [IO.File]::Replace($temporary, $path, $backup) }
        else { [IO.File]::Move($temporary, $path) }
    }
    finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
        if ([IO.File]::Exists($backup)) { [IO.File]::Delete($backup) }
    }
}

function Show-EarthVRSetup {
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    $form = New-Object Windows.Forms.Form
    $form.Text = 'Frame Earth VR - save your Cesium token'
    $form.ClientSize = New-Object Drawing.Size(510, 245)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'; $form.MaximizeBox = $false
    $label = New-Object Windows.Forms.Label
    $label.Text = 'Close the game first. Paste your own Cesium ion token below.'
    $label.SetBounds(18, 18, 475, 35); $form.Controls.Add($label)
    $inputBox = New-Object Windows.Forms.TextBox
    $inputBox.UseSystemPasswordChar = $true; $inputBox.MaxLength = 8192
    $inputBox.SetBounds(18, 58, 475, 26); $form.Controls.Add($inputBox)
    $destination = New-Object Windows.Forms.ComboBox
    $destination.DropDownStyle = 'DropDownList'
    $destination.Items.AddRange(@('Windows on this PC', 'APK on Steam Frame', 'Windows/Proton on Steam Frame'))
    $destination.SelectedIndex = @('Windows','FrameApk','FrameWindows').IndexOf($Target)
    $destination.SetBounds(18, 103, 295, 28); $form.Controls.Add($destination)
    $hostBox = New-Object Windows.Forms.TextBox
    $hostBox.Text = $DeviceHost; $hostBox.SetBounds(325, 103, 168, 28); $form.Controls.Add($hostBox)
    $hint = New-Object Windows.Forms.Label
    $hint.Text = 'Frame: pair with Devkit Client and launch the installed game once. The box on the right is its hostname or IP. Updates keep the saved token.'
    $hint.SetBounds(18, 140, 475, 45); $form.Controls.Add($hint)
    $save = New-Object Windows.Forms.Button
    $save.Text = 'Save token'; $save.DialogResult = 'OK'; $save.SetBounds(280, 200, 105, 28); $form.Controls.Add($save)
    $cancel = New-Object Windows.Forms.Button
    $cancel.Text = 'Cancel'; $cancel.DialogResult = 'Cancel'; $cancel.SetBounds(395, 200, 98, 28); $form.Controls.Add($cancel)
    $form.AcceptButton = $save; $form.CancelButton = $cancel
    try {
        if ($form.ShowDialog() -ne 'OK') { return $null }
        return @{token=$inputBox.Text.Trim(); target=@('Windows','FrameApk','FrameWindows')[$destination.SelectedIndex]; host=$hostBox.Text.Trim()}
    }
    finally { $inputBox.Clear(); $form.Dispose() }
}

# Kept in memory; the token is sent over SSH stdin, never in command arguments.
$frameProgram = @'
import json, os, re, sys, uuid
from pathlib import Path
r = json.load(sys.stdin)
home = Path.home()
libraries = { (home/'.local/share/Steam/steamapps').resolve(), (home/'.steam/steam/steamapps').resolve() }
for library in list(libraries):
    vdf = library/'libraryfolders.vdf'
    if vdf.is_file():
        for value in re.findall(r'"path"\s+"([^"]+)"',vdf.read_text()):
            libraries.add(Path(value.replace(chr(92)*2,chr(92)))/'steamapps')
suffix = 'external/Android/data/com.frameearthvr.app/files/EarthVR' if r['platform']=='apk' else 'pfx/drive_c/users/*/AppData/LocalLow/DefaultCompany/FrameEarthVR/EarthVR'
candidates = sorted({str(p) for library in libraries if (library/'compatdata').is_dir()
    for c in (library/'compatdata').iterdir() if c.name.isdigit() for p in c.glob(suffix) if p.is_dir()})
if r['mode']=='discover':
    print(json.dumps(candidates)); sys.exit(0)
folder = r['folder']
if folder not in candidates: raise SystemExit('Game data folder not found. Launch the game once first.')
token = r['token'].strip()
if not token or len(token)>8192 or re.search(r'\s|REPLACE_WITH',token,re.I): raise SystemExit('Invalid token format')
root = Path(folder)
path = root/'cesium-ion.local.json'
if root.is_symlink() or path.is_symlink(): raise SystemExit('Credential path must not be a symlink')
temporary = root/(uuid.uuid4().hex+'.tmp')
try:
    fd = os.open(temporary, os.O_WRONLY|os.O_CREAT|os.O_EXCL, 0o600)
    with os.fdopen(fd,'w') as stream:
        json.dump({'accessToken':token},stream); stream.flush(); os.fsync(stream.fileno())
    os.replace(temporary,path)
finally:
    temporary.unlink(missing_ok=True)
print('Saved token. Reopen the game from Steam.')
'@

function Invoke-FrameTokenCommand([hashtable]$Request) {
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($frameProgram))
    $remote = "podman unshare python3 -c 'import base64;exec(base64.b64decode(`"$encoded`"))'"
    $result = ($Request | ConvertTo-Json -Compress) | & $script:ssh -i $DevkitKey -o BatchMode=yes -o ConnectTimeout=10 -o StrictHostKeyChecking=accept-new "steamos@$DeviceHost" $remote
    if ($LASTEXITCODE -ne 0) { throw 'Frame setup failed. Check pairing, network, and that the game has launched once.' }
    return $result
}

# Dot-sourcing exposes only functions for tests; it never opens a dialog.
if ($MyInvocation.InvocationName -eq '.') { return }
try {
    if ($TokenFile) { $token = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $TokenFile).Path).Trim() }
    else {
        $selection = Show-EarthVRSetup
        if ($null -eq $selection) { return }
        $token = $selection.token; $Target = $selection.target; $DeviceHost = $selection.host
    }
    if ([string]::IsNullOrWhiteSpace($token) -or $token.Length -gt 8192 -or $token -match '\s|REPLACE_WITH') { throw 'Enter your token without spaces or placeholder text.' }
    if (-not $SkipAccessCheck) {
        try {
            Invoke-RestMethod -Uri 'https://api.cesium.com/v1/assets/2275207/endpoint' -Headers @{Authorization=('Bearer '+$token)} -TimeoutSec 20 | Out-Null
        }
        catch { throw 'Cesium access check failed. Check your network, assets:read permission, and access to asset 2275207.' }
    }
    if ($Target -eq 'Windows') {
        if (-not $DataFolder) { $DataFolder = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData/LocalLow/DefaultCompany/FrameEarthVR/EarthVR' }
        Save-EarthVRToken $DataFolder $token
        Write-Host 'Saved token for Windows. Launch FrameEarthVR.exe. Updates keep this token.'
    }
    else {
        if ($DeviceHost -notmatch '^[a-zA-Z0-9][a-zA-Z0-9.:-]*$') { throw 'Enter the Frame hostname or IP.' }
        if (-not $DevkitKey) { $DevkitKey = Join-Path $env:LOCALAPPDATA 'steamos-devkit/steamos-devkit/devkit_rsa' }
        if (-not (Test-Path -LiteralPath $DevkitKey)) { throw 'Pair your Frame in Valve Devkit Client first.' }
        $script:ssh = (Get-Command ssh.exe -ErrorAction SilentlyContinue).Source
        if (-not $script:ssh) { $script:ssh = Join-Path $env:ProgramFiles 'Git/usr/bin/ssh.exe' }
        if (-not (Test-Path -LiteralPath $script:ssh)) { throw 'SSH not found. Enable Windows OpenSSH Client or install Git for Windows.' }
        $platform = if ($Target -eq 'FrameApk') { 'apk' } else { 'windows' }
        $candidates = @(Invoke-FrameTokenCommand @{mode='discover'; platform=$platform} | ConvertFrom-Json)
        if (-not $candidates.Count) { throw 'Launch the installed game once on Frame, close it, then retry.' }
        if ($DataFolder) {
            if ($DataFolder -notin $candidates) { throw 'DataFolder must match a discovered game data folder.' }
        }
        elseif ($candidates.Count -eq 1) { $DataFolder = $candidates[0] }
        else {
            Add-Type -AssemblyName System.Windows.Forms, System.Drawing
            $choice = New-Object Windows.Forms.Form; $choice.Text = 'Choose the game data folder'; $choice.Width=850; $choice.Height=180
            $list = New-Object Windows.Forms.ComboBox; $list.DropDownStyle='DropDownList'; $list.Items.AddRange([object[]]$candidates); $list.SelectedIndex=0; $list.SetBounds(15,20,800,30); $choice.Controls.Add($list)
            $ok = New-Object Windows.Forms.Button; $ok.Text='Use folder'; $ok.DialogResult='OK'; $ok.SetBounds(680,70,120,30); $choice.Controls.Add($ok)
            try { if ($choice.ShowDialog() -ne 'OK') { return }; $DataFolder = [string]$list.SelectedItem } finally { $choice.Dispose() }
        }
        Invoke-FrameTokenCommand @{mode='save'; platform=$platform; folder=$DataFolder; token=$token} | Write-Host
    }
    if (-not $TokenFile) { [Windows.Forms.MessageBox]::Show('Token saved. Launch the game. Future updates keep your token.','Frame Earth VR setup') | Out-Null }
}
catch {
    if (-not $TokenFile) { [Windows.Forms.MessageBox]::Show($_.Exception.Message,'Frame Earth VR setup') | Out-Null }
    else { throw }
}
finally { $token = $null; $selection = $null }
