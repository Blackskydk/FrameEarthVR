# Frame Earth VR MVP

Frame Earth VR is a personal-use Unity/OpenXR prototype for exploring Google Photorealistic 3D Tiles streamed through Cesium ion at human and giant scales. The first target is Windows PC VR with an HTC Vive headset, Valve Index controllers, and SteamVR as the active OpenXR runtime. The code avoids the legacy SteamVR Unity plugin.

## Install on Steam Frame — no terminal needed

You need a PC running Steam and a Frame on the same Wi-Fi network. No Unity,
source download, terminal commands, or scripts are needed.

1. **Download on your PC:** get `FrameEarthVR.apk` from [Releases](https://github.com/Blackskydk/FrameEarthVR/releases) and save it in a new folder called `FrameEarthVR` containing only that APK. In Steam, find **SteamOS Devkit Client** in your Library (include **Software/Tools** in the filter), install it, and open it. [Valve's tool installation guide](https://partner.steamgames.com/doc/steamhardware/loadgames#2).
2. **Pair once:** on Frame, enable **Settings > System > Developer Mode**, then select **Settings > Developer > Pair new host**. In the PC's Devkit Client, open **Devkits**, click **Register** beside your Frame, and accept on the headset.
3. **Install:** open **Title Upload**, enter the values below, and click **Upload**.
4. **Set up and play:** launch once from **Library > Non-Steam > Devkit Game: Frame Earth VR**, then close it. On your PC, right-click the release's `setup-token.ps1`, choose **Run with PowerShell**, select **APK on Steam Frame**, and paste your own Cesium token. Reopen the game. The in-game controller keyboard is also available.

| Title Upload field | Enter/select |
|---|---|
| Name | `Frame Earth VR` |
| Local Folder | The `FrameEarthVR` folder you created |
| Start Command | `FrameEarthVR.apk` |
| Runtime | **Lepton** (some client versions/docs call it **Android**) |

**Updating:** download the newer APK into the same folder, replacing the old
file, and click **Upload** again for the same title. Pairing is only needed once.
Keep the existing installation to preserve your saved token and settings.
This follows [Valve's Frame installation guide](https://partner.steamgames.com/doc/steamhardware/steamframe/loadgames).

**Token setup (preview.5):** download `setup-token.ps1` from the release, right-click
it on your Windows PC and choose **Run with PowerShell**. Paste your own token in
the dialog and choose Windows, Frame APK, or Frame Windows/Proton. For Frame,
pair with Devkit Client, launch the game once, and close it before setup. Reopen
from Steam afterwards. The saved token survives updates. See the
[token setup guide](docs/STANDALONE_INSTALL.md#supply-your-own-token-on-first-launch).

**Preview.6 controls:** the left controller View/menu button opens and closes the
globe and menu together; gaze no longer changes their visibility. Panels stay
upright without inheriting head/controller roll. In Grounded mode, aim the right
controller nearly straight up/down and push its stick forward/back to resize.
Walking and ground correction pause while one geographic support point stays
anchored, including when shrinking back from giant scale.

**Direct updates (preview.4 source):** the hand menu adds **Download & Apply
Update** for APK and Windows builds. Windows PCs include the updater. Frame
needs a one-time helper setup for Lepton/Proton, using **one terminal command**;
future updates download and apply on the headset. See [on-device update setup
and test status](docs/ON_DEVICE_UPDATES.md). Frame integration still needs
headset validation. Updates use this repository's public GitHub releases.

On first launch, supply **your own Cesium ion token** with `assets:read` access to Google Photorealistic 3D Tiles asset `2275207`. The app checks and saves it on your headset. No publisher token or separate Google API key is needed. See the [installation and first-run guide](docs/STANDALONE_INSTALL.md).

The source checkout itself is not installable; a release must first be built and published. Unity cache folders, builds, and local credentials are excluded from Git.

The app checks for updates at startup and offers **Check for Updates** in the hand menu. Preview.2 opens the release page; preview.4 downloads and hands the update to its device helper. The update feed requires public releases; no GitHub credentials are requested.

## Pinned toolchain

- Unity `6000.3.14f1` (Unity 6.3 LTS)
- Universal Render Pipeline `17.3.0`
- Input System `1.16.0`
- OpenXR Plugin `1.16.1`
- XR Interaction Toolkit `3.3.0`
- Cesium for Unity `1.25.1`

The versions are pinned in `Packages/manifest.json`. Cesium is obtained from its official scoped registry. Unity 6.3 is the current LTS line; Cesium 1.25.x supports Windows x64 and Android ARM64. See the official [Unity 6 release page](https://unity.com/releases/unity-6), [Cesium quickstart](https://cesium.com/learn/unity/unity-quickstart/), and [Valve Unity guidance for Steam Frame](https://partner.steamgames.com/doc/steamhardware/steamframe/engines/unity).

## Developer setup — building from source

1. Install Unity `6000.3.14f1` in Unity Hub with **Windows Build Support (IL2CPP)**. For Steam Frame builds, also add **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK**.
2. In SteamVR, open **Settings > OpenXR** and make SteamVR the current OpenXR runtime.
3. Open this repository as a Unity project. Allow Package Manager to restore dependencies from Unity and `https://unity.pkg.cesium.com`.
4. Run **EarthVR > Setup Project and Main Scene**. This creates the URP renderer/pipeline assets and settings asset, selects D3D11, enables the Input System, assigns the OpenXR loader, enables available Index/Vive interaction profiles, creates `Assets/EarthVR/Scenes/EarthVR.unity`, and adds it to Build Settings.
5. Unity may request one editor restart after changing Active Input Handling. Accept it, reopen the project, and run the setup command once more if OpenXR validation still reports a loader issue.
6. Configure [the Cesium ion access token](docs/CESIUM_ION_TOKEN.md), or enter your own token in the in-app setup panel. The separate Google Map Tiles key loader is currently unused. Never put a real credential in an example or source file.
7. Open `Assets/EarthVR/Scenes/EarthVR.unity`, start SteamVR, connect the headset/controllers, and press Play.

The runtime bootstrap constructs the Cesium world, XR tracking hierarchy, controller indicators, navigation systems, wrist UI, and lighting. No manual GameObject assembly is required.

## Credentials and repository safety

Real credentials belong only in `Assets/StreamingAssets/EarthVR/google-maps.local.json` and `Assets/StreamingAssets/EarthVR/cesium-ion.local.json`, or in the `EARTHVR_GOOGLE_MAPS_API_KEY` and `EARTHVR_CESIUM_ION_ACCESS_TOKEN` environment variables. The local JSON files and their Unity metadata are excluded by `.gitignore`; the committed `.example.json` files contain placeholders only.

Before sharing a build, remember that credentials packaged in a Unity player can be extracted. Use separate, least-privilege application credentials, restrict the Google key to Map Tiles API, restrict the Cesium token to `assets:read` for asset `2275207`, and apply quotas and monitoring.

## Developer: Windows build

After the setup wizard succeeds:

1. Open **File > Build Profiles** and select **Windows**.
2. Architecture: **Intel 64-bit**. Graphics API: **Direct3D 11**.
3. Confirm **Project Settings > XR Plug-in Management > PC** has OpenXR enabled.
4. In **OpenXR > Interaction Profiles**, enable Valve Index Controller Profile and HTC Vive Controller Profile when present.
5. Resolve OpenXR Project Validation errors, then choose **Build** into a folder under `Builds/`.

Development builds may copy local configuration under StreamingAssets. Public release builds require users to enter their own token. The Steam Frame release build command excludes developer credentials and restores the local files afterward.

## Developer: Steam Frame build

The project can build a native standalone Android/ARM64 APK for Steam Frame through Valve's Lepton runtime. After installing Unity's Android modules, let Package Manager import Valve OpenXR Utilities, then use:

1. **EarthVR > Steam Frame > Configure Android**
2. **EarthVR > Steam Frame > Build Development APK**
3. `powershell -ExecutionPolicy Bypass -File .\scripts\install-steam-frame.ps1`

The APK is written to `Builds/SteamFrame/FrameEarthVR.apk`. See the [complete Steam Frame build and installation guide](docs/STEAM_FRAME_MIGRATION.md) for Wi-Fi, USB, validation, profiling, and troubleshooting.

These scripts are developer tools for Lepton Development. Players should use
the no-terminal installation guide at the top of this README for a Steam library entry.

For a one-click development build and Wi-Fi install, close the Unity Editor, launch
`Build-and-Install-Steam-Frame.cmd`, and keep Lepton Development running on the headset.
The launcher pauses when finished so build or connection errors remain visible.

## Project layout

- `Core`: bootstrap, tracking rig, extension interfaces
- `Input`: logical action abstraction and editable `.inputactions` asset
- `Navigation`: flight, grounded and car locomotion, loading-aware travel, and automatic ECEF origin rebasing
- `Scaling`: logarithmic scale math and world manipulation
- `Terrain`: Cesium provider, smoothed grounding, and altitude-aware horizon presentation
- `Environment`: NOAA-based solar positioning, draggable time-of-day Sun, and procedural day/night sky
- `UI`: globe-anchored UI, search, persistent favorites/recents, miniature-globe picker, and diagnostics
- `Configuration`: ScriptableObject tuning and editor-generated render assets
- `Editor`: one-command project/scene setup
- `Tests`: edit-mode tests for input-independent logic

## Architecture in brief

Cesium stays authoritative in ECEF/WGS84 coordinates. The Cesium hierarchy remains at identity. A user scale `S` is represented by `CesiumGeoreference.scale = 1 / S`, the precision-aware method documented by Cesium. When the scale changes, the selected ECEF pivot is converted before and after the change and the XR navigation space is translated by the difference. The pivot therefore remains visually fixed.

Physical room-scale tracking remains one Unity metre per physical metre. Because the globe occupies `1 / S` Unity units, that motion represents approximately `S` geographic metres. World drag stores the selected mesh point in ECEF and solves Navigation Space from the latest tracked pose without modifying downloaded tile transforms. In Flight, the original ray depth is fixed. In Grounded, ray depth is the free variable: the selected point remains exactly on the live pointer ray while the tracking floor remains at its grab-start height.

When continuous travel carries the camera 2 km from the current Unity origin, the runtime rebases the Cesium georeference at the camera's exact ECEF point. Navigation Space is transformed into the new local frame in the same `LateUpdate`, preserving camera position and orientation in ECEF while keeping Unity floats small. Search, bookmarks, recent places, and miniature-globe picks share a fade-to-black arrival pipeline that restores the saved viewpoint and waits for a useful Cesium load percentage before revealing it. Full planetary overview travel instead follows a continuous city-to-globe-to-city camera path with an invisible destination-centered rebase and load-adaptive descent speed, never entering that blackout pipeline. Every completed journey selects local solar noon at the destination; polar-night arrivals use that hemisphere's summer solstice so landing is always in daylight.

The left-hand destination globe uses NASA/Goddard Space Flight Center Scientific Visualization Studio's 2048×1024 Blue Marble mosaic as its local map texture. It appears together with the hand menu when the left View/pause button is pressed, follows the controller position while keeping an independent orientation, enlarges when pointed at, and supports right-pointer trackball dragging without requiring the left hand to rotate. The menu panel beside it carries search, bookmarks and recent places, recenter, diagnostics, and live Flight/Grounded and comfort-vignette toggles; both close automatically when travel begins. Search type-ahead instantly matches a bundled catalog of major cities, geographic landmarks, national parks, selected prominent POIs, bookmarks, and recents without making an API call; submitted free-form searches retain Nominatim as a fallback. Left D-pad Up on the Steam Frame (the left primary button on other controllers) tilts the real Cesium ground into a north-up map without changing zoom. Right-trigger-drag uses the touched surface location as a true trackball anchor while holding Earth's center fixed in front of the viewer; dragging beyond the visible silhouette clamps naturally at its virtual limb instead of throwing Earth sideways, and either grip also rotates the tilted Earth freely. The overview Sun follows the viewer axis so the visible hemisphere remains illuminated without changing simulation time. Pulling the right stick back zooms out, and pushing it forward keeps one ECEF surface location, captured at the start of the inward gesture, pinned to the pointer all the way in. The fixed anchor and view-centered local-frame rebase prevent zoom-only input from adding an unrequested rotation or jump, including across the antimeridian and after rotating to the opposite hemisphere. A short trigger click travels to the pointed destination. The globe-visible subset of the offline catalog uses larger bold, shadowed labels for major cities and geographic landmarks, with hemisphere culling and overlap suppression. The travel zoom automatically slows when Cesium tile refinement falls behind rather than cutting to black or teleporting. Returning to Grounded explicitly restores a tangent, upright tracking floor. The planetary map contains no overlapping decorative globe surface, and Cesium parent tiles remain visible until their replacement children are ready to avoid depth artifacts and refinement cracks. In Grounded mode, world dragging jointly solves the live pointer ray and a fixed tracking-floor height; the initially selected ECEF point never changes, while moving the controller underneath the body changes ray depth instead of lifting the player and dropping them on release. The shoulder speed boost applies in both Flight and Grounded.

See [architecture notes](docs/ARCHITECTURE.md), [controls](docs/CONTROLS.md), [test checklist](docs/PC_VR_TEST_CHECKLIST.md), [Steam Frame migration](docs/STEAM_FRAME_MIGRATION.md), and [known limitations](docs/KNOWN_LIMITATIONS.md).

## Running tests

Open **Window > General > Test Runner**, select **EditMode**, and run `EarthVR.Tests`. Tests cover logarithmic scale conversion, physical/world conversion, height calculations, clamping, flight-speed behavior, and movement-mode transitions.

## Troubleshooting

- **Black view / no terrain:** check `cesium-ion.local.json`, confirm the token has `assets:read` access to Google Photorealistic 3D Tiles asset `2275207`, and inspect the wrist status or Console for the exact request error.
- **HTTP 429 from `tile.googleapis.com`:** the Google Photorealistic root request supplied through Cesium ion has been rate- or quota-limited. EarthVR immediately falls back for that session to Cesium World Terrain with Bing Maps Aerial imagery, so the ground remains available without using the separate local Google API key. If the fallback itself is rate-limited, requests use a 60/120/240-second exponential delay instead of flooding the endpoint. Check Google Photorealistic 3D Tiles root usage in the Cesium ion Usage dashboard before the next run.
- **`StopSubsystems without an initialized manager`:** this Unity XR Management shutdown warning can appear when Play Mode ends before OpenXR finishes starting. It is not the cause of a simultaneous tile-loading failure.
- **Large-triangle physics warnings:** these come from Unity baking streamed Cesium photogrammetry collision meshes. They are warnings rather than the reason terrain failed to load; disabling physics meshes removes them but also disables exact terrain grabbing and collision.
- **Location search fails:** search is user-initiated and uses the public OpenStreetMap Nominatim service without autocomplete. Check connectivity and respect its one-request-per-second usage policy.
- **Package restore fails:** verify internet access and that the Cesium scoped registry in `Packages/manifest.json` is reachable. Do not replace Cesium with its Git URL; release packages contain platform-native binaries.
- **Headset does not start:** make SteamVR the active OpenXR runtime, enable OpenXR under the PC build target, and run OpenXR Project Validation.
- **Controllers track but actions do not:** verify the Valve Index profile is enabled, then inspect `EarthVRInputActions.inputactions`; all gameplay reads logical actions through `IEarthVRInput`.
- **Pink/missing tiles:** run the setup wizard and confirm the EarthVR URP asset is assigned in Graphics and Quality settings.
- **Ground following unavailable:** streamed geometry may not be ready. The controller first uses nearby physics meshes, then asynchronously samples the tileset. It holds the last safe elevation rather than snapping downward.
- **Low frame rate:** raise maximum screen-space error, lower render scale/MSAA, reduce cache/load concurrency, and disable physics meshes if grounded mode/world targeting is not needed.

The Android Build Support module and real Steam Frame hardware are still required to produce and validate the final APK. No claim of a fixed headset frame rate is made until the build has been profiled on-device.
