# Update from inside Frame Earth VR

The `1.0.0-preview.4` source adds **Download & Apply Update** to the hand menu.
It downloads the correct build onto the device, verifies its SHA-256 digest
against GitHub release metadata, and hands it to an updater. The game closes
only after the updater has verified and accepted the download. Terrain tokens,
bookmarks, and settings stay in the existing app-data directory.

This is not a silent startup install: startup checks only notify. You choose
when to download and apply an update. Windows packages and APKs are selected
separately. Preview builds can update to newer previews or stable versions;
stable builds only offer stable versions.

## Windows PC

Download and extract the complete `FrameEarthVR-Windows.zip` once. Launch
`FrameEarthVR.exe` from a folder your Windows account can write to. The game
includes its PowerShell updater; you do not type any commands or install an
extra runtime. The updater waits for the game process to exit, backs up the
previous game files, applies the new files, and relaunches the game.

For an old Windows build without the updater, install preview.4 or later
manually once. All published packages must use the same executable name.

## Frame: APK through Lepton or Windows through Proton

Install the desired build through Devkit Client and launch it once, then close
it. Frame needs a small helper running on SteamOS because the game runs in a
compatibility environment. The helper updates the **original host APK** or the
Windows game files, leaving the shortcut and compatdata in place. On next
launch, Lepton refreshes its installed app from that APK. Updating only the
temporary Android installation is not used.

Download `frame-updater.py` from the same release to the **Frame's** Downloads
folder. In a terminal on Frame, run this one command:

```sh
python3 ~/Downloads/frame-updater.py --setup
```

Choose `apk`, `windows`, or `both`. The script searches standard Steam library
and Devkit game locations and the game's existing data folders. If it finds
multiple matches, choose the displayed number. If your installation is in a
custom location, drag/paste that folder when prompted. No administrator access,
GitHub credentials, or terrain tokens are needed by the helper.

Setup installs a user service that starts with your session. Python 3, systemd,
and (for APKs) Lepton's podman tool must already be available on the device.
If the service is missing or stopped, the game displays a setup message and
keeps running. After setup, future updates need no terminal commands: select
**Download & Apply Update**, let the game close, and reopen its Steam shortcut
after installation finishes. A failed installation is shown on next launch.

The helper receives requests through the game's own local data folder; it does
not open a network listener or accept paths supplied by an update request.
It independently checks the release digest, waits for the game/container to
exit, and keeps the previous build for recovery. Windows file replacement rolls
back if a move fails. The previous APK remains at `.earthvr-previous-apk`; the
Windows backup is `.earthvr-previous` inside the install folder. Only the last
backup is kept. Do not replace or delete the game's compatdata during updates.

## Release requirements and test status

- A public GitHub release must contain `FrameEarthVR.apk` and/or
  `FrameEarthVR-Windows.zip`, with GitHub's `sha256:` asset digest. Releases
  without a matching platform asset are ignored. Checks and downloads need
  internet access; there is no GitHub token embedded in the game or helper.
- This repository's releases are public. Neither the game nor the helper needs
  a GitHub token to check or download them.
- APK releases must retain their signing key and increase the Android version
  code. Set both version values in `ReleaseBuildStamp` before building.
- Both builds and the updater checks pass locally. The APK helper was also tested
  on a Steam Frame: automatic folder discovery, rootless Lepton permissions,
  public release download and digest verification, waiting for container exit,
  replacement with a matching release hash, backup, and relaunch into preview.4.
  The relaunched game initializes its update heartbeat without startup errors.
  That test submitted a request through the local inbox; a headset interaction
  test of the download button remains. A live Windows/Proton update and saved
  credential/bookmark preservation still need verification.

For maintainers: build via **EarthVR > Steam Frame > Build Release APK** and
**EarthVR > Windows > Build Release**, then package both with:

```powershell
.\scripts\prepare-public-release.ps1 -Version v1.0.0-preview.4 -WindowsFolder .\Builds\Windows
```

Upload the APK, Windows ZIP, `frame-updater.py`, and `SHA256SUMS.txt` to the
same release. The helper script is an initial setup download, not a terrain
credential installer. Do not reuse an APK signed with a different key.

Verification commands are `scripts/test-release-updates.ps1`,
`scripts/test-windows-updater.ps1`, and `python scripts/test-frame-updater.py`.
