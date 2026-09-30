# Install Frame Earth VR on Steam Frame

Players use the prebuilt ARM64 APK from GitHub Releases. They do not need Unity
or a source checkout. The repository alone is not an installable app: a public
release needs to include the APK and its SHA-256 checksum. Download
`FrameEarthVR.apk` from [GitHub Releases](https://github.com/Blackskydk/FrameEarthVR/releases)
and follow the [Steam Frame installation walkthrough in the README](../README.md#install-on-steam-frame). No credentials belong
in a release, installer URL, or GitHub issue.

## Supply your own token on first launch

For the **native Windows build**, extract the complete `FrameEarthVR-Windows.zip`
into a writable folder, run token setup with **Windows on this PC**, then launch
`FrameEarthVR.exe`. The folder can be moved without moving its saved token.
For **Windows/Proton on Frame**, install the extracted Windows build through
Devkit Client using Proton, launch it once, close it, and choose **Windows/Proton
on Steam Frame** in token setup. APK installation uses Lepton and the README's
Devkit guide. Tokens are configured after the initial installation; they are
never baked into the downloaded app.

Create a [Cesium ion](https://ion.cesium.com/) account, add Google Photorealistic
3D Tiles from the Asset Depot, and create an application access token with
`assets:read` permission for asset `2275207`. Follow
[Cesium's token guide](https://cesium.com/learn/ion/cesium-ion-access-tokens/).
Your account's terms, plan, quotas, and any applicable charges are your own.

The easiest route is **PC token setup** (preview.5): download `setup-token.ps1`
from the release, right-click it in Windows Explorer, and select **Run with
PowerShell**. Paste your token into the masked dialog and choose **Windows on
this PC**, **APK on Steam Frame**, or **Windows/Proton on Steam Frame**. It checks
access with Cesium and saves the token directly into the game's persistent data.
For Frame, pair in Devkit Client and launch the game once to create its data
folder, then close it before saving. The PC and Frame must be on the same network.
Windows OpenSSH Client or Git for Windows supplies SSH. Then reopen from Steam.
For native Windows, token setup can run before the game's very first launch.

If Windows blocks the downloaded script, open its Properties and choose Unblock.
Alternatively, run this single command in the folder where you downloaded it:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup-token.ps1
```

For an installation script, supply a **file path** parameter rather than putting
the token itself into command history:

```powershell
.\setup-token.ps1 -Target FrameApk -TokenFile .\my-token.txt
```

The file contains only the user's token. Use `Windows` or `FrameWindows` for the
other destinations; `-DeviceHost` accepts a Frame hostname/IP. Delete the input
file when finished. The setup sends the token over encrypted SSH stdin, never
through Steam launch options, the APK, a release URL, or SSH command arguments.

The in-game setup panel remains available with a case-sensitive controller
keyboard and **Save & Start**. In the APK, **PC SETUP HELP** replaces Paste because
the native clipboard call can crash under Lepton. Windows Paste remains available.
Both the setup script and in-game Save check Cesium asset access before saving.

Only a Cesium ion token is required. The app accesses Google 3D Tiles through
Cesium ion; the separate Google API key loader is currently unused.

Tokens are stored as local JSON under the app's persistent data directory,
outside the APK. This is device storage, not encrypted secure storage. They
are masked on the setup panel and are not sent to the publisher;
the app sends them to Cesium to authorize terrain access. Reopen **Your Cesium
Account** in the hand menu to replace or forget a token. Uninstalling/clearing
app data may remove it. The updater changes only the game files, keeping this
data folder and the existing Steam shortcut/compatdata. APK and Windows/Proton
are separate installations, so configure each once if you use both. Keep the
same package ID, Windows company/product name, and Steam shortcut across updates.

## Check for updates

Starting with `1.0.0-preview.2`, the game checks GitHub Releases once, eight
seconds after startup. Open the hand menu to see the result, or select
**Check for Updates** to check manually. Preview.2 opens the release page.
Preview.3 adds **Download & Apply Update** with a Windows updater and a
one-time Frame helper setup. See the [on-device update guide](ON_DEVICE_UPDATES.md)
for its single setup command and the remaining headset validation. Manual APK
uploads through Devkit Client remain available for initial installation and recovery.

Preview builds include newer previews and stable releases; stable builds only
offer stable releases. Drafts and releases without an uploaded asset for the
running platform are ignored.
Checks need internet and a **public** repository; a private repository reports
that the update feed is unavailable. No GitHub login or terrain token is sent
with these checks. Failed checks leave gameplay available.

## Install with the SteamOS Devkit Client

This installation uses buttons and fields; no terminal command is required.
Install **SteamOS Devkit Client** through Steam on your PC and open it.
Put your PC and Frame on the same network. Enable Steam Settings > System >
Developer Mode on Frame, then Developer > Pair new host. In the client's
**Devkits** tab, click **Register** beside your Frame and confirm on the headset.
Download the APK into a folder containing only that file, then use **Title Upload**:

| Field | Value |
|---|---|
| Name | Frame Earth VR |
| Local Folder | Folder containing only the release APK |
| Start Command | FrameEarthVR.apk |
| Runtime | Lepton (called Android in some documentation) |

After upload, launch the title under Library > Non-Steam. See
[Valve's guide](https://partner.steamgames.com/doc/steamhardware/steamframe/loadgames).
The PC is needed for sideloading; gameplay runs on Frame and streams terrain
over its internet connection.

For an update, replace the APK in the same PC folder and upload the same title
again. Keep the app installed to preserve its data. Pairing is a one-time setup.

## Publish an installable GitHub release (maintainer)

1. Save the Unity project. Use **EarthVR > Steam Frame > Build Release APK**.
   This temporarily removes the two local credential JSON files and their
   metadata from StreamingAssets, and restores them in a `finally` block.
   Editor/development builds can retain their local setup. Release runtime
   ignores environment variables and bundled local tokens.
2. The pre-build guard also refuses release builds with local credential files
   or serialized token/key fields in Unity assets/scenes/prefabs. If Unity is
   killed during staging, recover files from `Library/EarthVRPrivateBuildBackup`
   into `Assets/StreamingAssets/EarthVR` before another build. Never publish
   that private backup directory.
3. Set the semantic version and increasing Android version code in
   `Assets/EarthVR/Core/ReleaseVersion.cs` (`ReleaseBuildStamp`) before building;
   the Steam Frame builder applies both to the APK. Use a stable Android signing
   keystore for published releases. Keep the keystore and passwords out of
   Git. Changing signing keys requires uninstalling the old app.
4. Run `scripts/prepare-public-release.ps1 -Version v0.1.0` (substitute the actual
   version). It rejects development APKs, local credential entries, missing
   ARM64 binaries, and builds predating first-run setup. It writes the APK,
   checksums and release notes under `Builds/Public/<version>`.
   Run `scripts/test-public-release-policy.ps1` to check the serialized credential
   guard independently of Unity.
5. Test on a clean Frame app installation with a **new user's token**, including
   a failed token, successful setup, relaunch, token replacement/removal, and
   update without data loss. Confirm stereo UI and headset performance.
6. Create a GitHub Release for that exact tag and attach `FrameEarthVR.apk`,
   `frame-updater.py`, and `SHA256SUMS.txt`. For Windows/Proton, build with
   **EarthVR > Windows > Build Release** and pass `-WindowsFolder Builds/Windows`
   to the packaging script; also attach `FrameEarthVR-Windows.zip`.
   Copy `RELEASE_NOTES.md` into the release body.
   These files are prepared locally; the helper does not publish them.

The GitHub README links to Releases so readers can find available builds.
