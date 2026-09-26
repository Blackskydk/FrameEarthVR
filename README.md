# Frame Earth VR MVP

Frame Earth VR is a personal-use Unity/OpenXR prototype for exploring Google Photorealistic 3D Tiles streamed through Cesium ion at human and giant scales. The first target is Windows PC VR with an HTC Vive headset, Valve Index controllers, and SteamVR as the active OpenXR runtime. The code avoids the legacy SteamVR Unity plugin.

The repository is source-only. Unity cache folders, builds, and local credentials are intentionally excluded.

## Pinned toolchain

- Unity `6000.3.14f1` (Unity 6.3 LTS)
- Universal Render Pipeline `17.3.0`
- Input System `1.16.0`
- OpenXR Plugin `1.16.1`
- XR Interaction Toolkit `3.3.0`
- Cesium for Unity `1.25.1`

The versions are pinned in `Packages/manifest.json`. Cesium is obtained from its official scoped registry. Unity 6.3 is the current LTS line; Cesium 1.25.x supports Windows x64 and Android ARM64. See the official [Unity 6 release page](https://unity.com/releases/unity-6), [Cesium quickstart](https://cesium.com/learn/unity/unity-quickstart/), and [Valve Unity guidance for Steam Frame](https://partner.steamgames.com/doc/steamhardware/steamframe/engines/unity).

## First-time setup

1. Install Unity `6000.3.14f1` in Unity Hub with **Windows Build Support (IL2CPP)**. For Steam Frame builds, also add **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK**.
2. In SteamVR, open **Settings > OpenXR** and make SteamVR the current OpenXR runtime.
3. Open this repository as a Unity project. Allow Package Manager to restore dependencies from Unity and `https://unity.pkg.cesium.com`.
4. Run **EarthVR > Setup Project and Main Scene**. This creates the URP renderer/pipeline assets and settings asset, selects D3D11, enables the Input System, assigns the OpenXR loader, enables available Index/Vive interaction profiles, creates `Assets/EarthVR/Scenes/EarthVR.unity`, and adds it to Build Settings.
5. Unity may request one editor restart after changing Active Input Handling. Accept it, reopen the project, and run the setup command once more if OpenXR validation still reports a loader issue.
6. Configure both [the Google Map Tiles API key](docs/GOOGLE_API_KEY.md) and [the Cesium ion access token](docs/CESIUM_ION_TOKEN.md). Copy each example to its matching `.local.json` file; never put a real credential in an example or source file.
7. Open `Assets/EarthVR/Scenes/EarthVR.unity`, start SteamVR, connect the headset/controllers, and press Play.

The runtime bootstrap constructs the Cesium world, XR tracking hierarchy, controller indicators, navigation systems, wrist UI, and lighting. No manual GameObject assembly is required.

## Credentials and repository safety

Real credentials belong only in `Assets/StreamingAssets/EarthVR/google-maps.local.json` and `Assets/StreamingAssets/EarthVR/cesium-ion.local.json`, or in the `EARTHVR_GOOGLE_MAPS_API_KEY` and `EARTHVR_CESIUM_ION_ACCESS_TOKEN` environment variables. The local JSON files and their Unity metadata are excluded by `.gitignore`; the committed `.example.json` files contain placeholders only.

Before sharing a build, remember that credentials packaged in a Unity player can be extracted. Use separate, least-privilege application credentials, restrict the Google key to Map Tiles API, restrict the Cesium token to `assets:read` for asset `2275207`, and apply quotas and monitoring.

## Windows build

After the setup wizard succeeds:

1. Open **File > Build Profiles** and select **Windows**.
2. Architecture: **Intel 64-bit**. Graphics API: **Direct3D 11**.
3. Confirm **Project Settings > XR Plug-in Management > PC** has OpenXR enabled.
4. In **OpenXR > Interaction Profiles**, enable Valve Index Controller Profile and HTC Vive Controller Profile when present.
5. Resolve OpenXR Project Validation errors, then choose **Build** into a folder under `Builds/`.

Local configuration under StreamingAssets is copied into a player build and can be extracted by anyone with the build. Use a narrowly scoped application token for builds you distribute.

## Steam Frame build

The project can build a native standalone Android/ARM64 APK for Steam Frame through Valve's Lepton runtime. After installing Unity's Android modules, let Package Manager import Valve OpenXR Utilities, then use:

1. **EarthVR > Steam Frame > Configure Android**
2. **EarthVR > Steam Frame > Build Development APK**
3. `powershell -ExecutionPolicy Bypass -File .\scripts\install-steam-frame.ps1`

The APK is written to `Builds/SteamFrame/FrameEarthVR.apk`. See the [complete Steam Frame build and installation guide](docs/STEAM_FRAME_MIGRATION.md) for Wi-Fi, USB, validation, profiling, and troubleshooting.

For a one-click development build and Wi-Fi install, close the Unity Editor, launch
`Build-and-Install-Steam-Frame.cmd`, and keep Lepton Development running on the headset.
The launcher pauses when finished so build or connection errors remain visible.

## Project layout

- `Core`: bootstrap, tracking rig, extension interfaces
- `Input`: logical action abstraction and editable `.inputactions` asset
- `Navigation`: flight, turning, state, speed model
- `Scaling`: logarithmic scale math and world manipulation
- `Terrain`: Cesium provider and smoothed grounding
- `Environment`: NOAA-based solar positioning, draggable time-of-day Sun, and procedural day/night sky
- `UI`: wrist UI, controller-selectable buttons, search, and diagnostics
- `Configuration`: ScriptableObject tuning and editor-generated render assets
- `Editor`: one-command project/scene setup
- `Tests`: edit-mode tests for input-independent logic

## Architecture in brief

Cesium stays authoritative in ECEF/WGS84 coordinates. The Cesium hierarchy remains at identity. A user scale `S` is represented by `CesiumGeoreference.scale = 1 / S`, the precision-aware method documented by Cesium. When the scale changes, the selected ECEF pivot is converted before and after the change and the XR navigation space is translated by the difference. The pivot therefore remains visually fixed.

Physical room-scale tracking remains one Unity metre per physical metre. Because the globe occupies `1 / S` Unity units, that motion represents approximately `S` geographic metres. Cone drag stores the selected mesh point in ECEF, keeps it on the controller ray at its original grab depth, and moves the observer horizontally so distant targets can be pulled underneath without modifying downloaded tile transforms or crossing the terrain.

See [architecture notes](docs/ARCHITECTURE.md), [controls](docs/CONTROLS.md), [test checklist](docs/PC_VR_TEST_CHECKLIST.md), [Steam Frame migration](docs/STEAM_FRAME_MIGRATION.md), and [known limitations](docs/KNOWN_LIMITATIONS.md).

## Running tests

Open **Window > General > Test Runner**, select **EditMode**, and run `EarthVR.Tests`. Tests cover logarithmic scale conversion, physical/world conversion, height calculations, clamping, flight-speed behavior, and movement-mode transitions.

## Troubleshooting

- **Black view / no terrain:** check `cesium-ion.local.json`, confirm the token has `assets:read` access to Google Photorealistic 3D Tiles asset `2275207`, and inspect the wrist status or Console for the exact request error.
- **Location search fails:** search is user-initiated and uses the public OpenStreetMap Nominatim service without autocomplete. Check connectivity and respect its one-request-per-second usage policy.
- **Package restore fails:** verify internet access and that the Cesium scoped registry in `Packages/manifest.json` is reachable. Do not replace Cesium with its Git URL; release packages contain platform-native binaries.
- **Headset does not start:** make SteamVR the active OpenXR runtime, enable OpenXR under the PC build target, and run OpenXR Project Validation.
- **Controllers track but actions do not:** verify the Valve Index profile is enabled, then inspect `EarthVRInputActions.inputactions`; all gameplay reads logical actions through `IEarthVRInput`.
- **Pink/missing tiles:** run the setup wizard and confirm the EarthVR URP asset is assigned in Graphics and Quality settings.
- **Ground following unavailable:** streamed geometry may not be ready. The controller first uses nearby physics meshes, then asynchronously samples the tileset. It holds the last safe elevation rather than snapping downward.
- **Low frame rate:** raise maximum screen-space error, lower render scale/MSAA, reduce cache/load concurrency, and disable physics meshes if grounded mode/world targeting is not needed.

The Android Build Support module and real Steam Frame hardware are still required to produce and validate the final APK. No claim of a fixed headset frame rate is made until the build has been profiled on-device.
