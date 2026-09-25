# Build and install on Steam Frame

Steam Frame's native standalone path is an Android ARM64 APK running on the headset through Valve's Lepton compatibility layer. Rendering, tracking, controller input, Cesium streaming, and gameplay run on the headset; a PC is only needed to build and install development versions.

This project includes Valve OpenXR Utilities in `Packages/manifest.json`, an Android/OpenXR configuration command, development and release APK build commands, and an `adb` install script.

## 1. Install the Android toolchain

In Unity Hub, add these modules to Unity `6000.3.14f1`:

- Android Build Support
- Android SDK & NDK Tools
- OpenJDK

The build command reports a clear error while these modules are missing.

## 2. Let Unity configure the project

1. Open the project and wait for Package Manager to finish resolving packages. It will fetch Valve OpenXR Utilities from Valve's official Unity repository.
2. Run **EarthVR > Setup Project and Main Scene** if the scene has not already been generated.
3. Run **EarthVR > Steam Frame > Configure Android**.
4. In **Edit > Project Settings > XR Plug-in Management > OpenXR > Android**, confirm that the Steam Frame Controller Profile is enabled.
5. Open **OpenXR Project Validation** and resolve any remaining blocking errors. Valve's Lepton validation rules appear after its package imports.

The command configures:

- Android API 29 minimum and automatic/current target API
- IL2CPP and ARM64 only
- Vulkan only
- required internet permission for Cesium streaming
- Unity OpenXR with single-pass multiview rendering
- predicted display time and render-priority latency mode
- SRP foveated rendering, symmetric projection, render-region optimization, and buffer-discard optimization
- Steam Frame, Oculus Touch fallback, and Khronos simple controller profiles
- package id `com.frameearthvr.app`

Meta Quest runtime feature groups are disabled for the Android OpenXR target because they can conflict with Lepton. The generic OpenXR input bindings remain the source of gameplay controls.

## 3. Build the APK

Use one of these Unity menu items:

- **EarthVR > Steam Frame > Build Development APK**: debugging and profiler connection enabled
- **EarthVR > Steam Frame > Build Release APK**: optimized test/distribution build

Both produce:

```text
Builds/SteamFrame/FrameEarthVR.apk
```

The first IL2CPP Android build can take several minutes.

## 4. Connect the headset

On Steam Frame, enable Developer Mode and launch **Lepton Development** from the Steam Library. Keep it running while connecting.

For Wi-Fi, keep the PC and headset on the same network. The included script uses Valve's `frame` hostname by default:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-steam-frame.ps1
```

If the hostname is not resolved, pass the headset IP shown by Lepton Development:

```powershell
.\scripts\install-steam-frame.ps1 -DeviceHost 192.168.1.42
```

For USB:

```powershell
.\scripts\install-steam-frame.ps1 -Usb
```

The script finds Unity's bundled `adb`, connects, replaces an existing development install, and launches the app. Use `-NoLaunch` to install only. Once `adb` is connected, Unity's regular **Build and Run** workflow also works.

## 5. Profile on the headset

Use the development APK with Unity Profiler and Android logcat. Check GPU frame time, CPU tile decoding, network throughput, thermal throttling, memory, and Cesium cache pressure. Tune `standaloneRenderScale`, `standaloneMsaa`, tile screen-space error, cache size, and concurrent tile loads in `EarthVRSettings.asset` based on hardware measurements.

No desktop or emulator result proves headset performance. Foveation and multiview render regions also need validation on the actual Steam Frame runtime.

## Credentials

Files under `Assets/StreamingAssets` are copied into the APK and can be extracted. Use a narrowly scoped Cesium ion token, never a general account token, for any build installed or shared outside your own headset.

## Troubleshooting

- **Android Build Support is not installed:** add all three Unity Hub modules listed above for the exact editor version.
- **Steam Frame Controller Profile is missing:** wait for Package Manager to finish importing Valve OpenXR Utilities, then rerun **Configure Android**.
- **`adb connect` fails:** verify Developer Mode, launch Lepton Development, and check that PC and headset are on the same network. Try the displayed IP instead of `frame`.
- **USB forwarding fails:** reconnect USB, accept any headset authorization prompt, and keep Lepton Development in the foreground.
- **App opens to black:** run OpenXR/Lepton validation, confirm Vulkan and ARM64, and inspect `adb logcat -s Unity`.
- **Terrain never appears:** verify the local Cesium token and network access. Internet permission is already forced by the project settings.

Official references: [Valve Unity integration](https://partner.steamgames.com/doc/steamhardware/steamframe/engines/unity), [Valve ADB and Lepton setup](https://partner.steamgames.com/doc/steamhardware/steamframe/adb_lepton), [Valve debugging](https://partner.steamgames.com/doc/steamhardware/steamframe/debugging), [Valve OpenXR Utilities](https://github.com/ValveSoftware/Unity/blob/main/com.valvesoftware.openxr.utils/Documentation~/index.md), and [Cesium supported platforms](https://cesium.com/learn/unity/ref-doc/supported-platforms.html).
