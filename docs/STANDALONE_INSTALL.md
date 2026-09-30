# Install Frame Earth VR on Steam Frame

Players use the prebuilt ARM64 APK from GitHub Releases. They do not need Unity
or a source checkout. The repository alone is not an installable app: a public
release needs to include the APK and the FrameDrop manifest described below.

## Install with FrameDrop

1. Install [FrameDrop](https://framedropvr.com/) on your PC.
2. Put the PC and Frame on the same network. On Frame, enable Steam Settings >
   System > Developer Mode, then Developer > Pair new host.
3. Click Pair in FrameDrop and confirm on the headset.
4. Download `FrameEarthVR.apk` from this repository's Releases and drop it into
   FrameDrop, or use **Install with FrameDrop** in that release's notes.
5. Confirm the title and install. Launch Frame Earth VR from Steam on the headset.

FrameDrop detects the APK runtime and registers a Steam shortcut. It is an
optional third-party installer; see its [usage guide](https://framedropvr.com/how-to/).
The manifest links only to the APK, with a SHA-256 checksum. No credentials
belong in a manifest, release, installer URL, or GitHub issue.

## Supply your own token on first launch

Create a [Cesium ion](https://ion.cesium.com/) account, add Google Photorealistic
3D Tiles from the Asset Depot, and create an application access token with
`assets:read` permission for asset `2275207`. Follow
[Cesium's token guide](https://cesium.com/learn/ion/cesium-ion-access-tokens/).
Your account's terms, plan, quotas, and any applicable charges are your own.

The app opens a setup panel before terrain loads. Paste your token from the
**headset's** clipboard, use the controller keyboard (case-sensitive), or try
the system keyboard if available in your Lepton runtime. Select **Save & Start**.
The app verifies access to the tiles asset before saving; internet is required.
The controller keyboard supports token letters, numbers, periods, hyphens,
and underscores. Copying on the PC does not automatically copy to the headset.

Only a Cesium ion token is required. The app accesses Google 3D Tiles through
Cesium ion; the separate Google API key loader is currently unused.

Tokens are stored as local JSON under the app's persistent data directory,
outside the APK. This is device storage, not encrypted secure storage. They
are masked on the setup panel and are not sent to the publisher or FrameDrop;
the app sends them to Cesium to authorize terrain access. Reopen **Your Cesium
Account** in the hand menu to replace or forget a token. Uninstalling/clearing
app data may remove it; a normal same-signature APK update should preserve it.

## Valve installer alternative

Use the SteamOS Devkit Client, pair the Frame, then Title Upload:

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
3. Use a stable Android signing keystore for published releases and increment
   Android's version code for updates. Keep the keystore and passwords out of
   Git. Changing signing keys requires uninstalling the old app.
4. Run `scripts/prepare-public-release.ps1 -Version v0.1.0` (substitute the actual
   version). It rejects development APKs, local credential entries, missing
   ARM64 binaries, and builds predating first-run setup. It writes the APK,
   checksums, manifest, and release notes under `Builds/Public/<version>`.
   Run `scripts/test-public-release-policy.ps1` to check the serialized credential
   guard independently of Unity.
5. Test on a clean Frame app installation with a **new user's token**, including
   a failed token, successful setup, relaunch, token replacement/removal, and
   update without data loss. Confirm stereo UI and headset performance.
6. Create a GitHub Release for that exact tag and attach `FrameEarthVR.apk`,
   `FrameEarthVR.framedrop.json`, and `SHA256SUMS.txt`. Copy `RELEASE_NOTES.md`
   into the release body. These files are prepared locally; the helper does not
   publish them. Keep a direct APK download alongside the FrameDrop link.

The generated link uses FrameDrop's documented
[`install?manifest=` integration](https://framedropvr.com/docs/), with a
version-specific GitHub Release URL and checksum. The GitHub README links to
Releases so readers always see available builds rather than an unverified
hard-coded release link.
