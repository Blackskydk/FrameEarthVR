# Performance tuning and tile-seam troubleshooting

Nothing here has been measured on a headset by the author of these changes;
they are settings, switches and diagnostics to make that testing quick. Where
a default changed, the reason is given so it can be reverted.

## What is applied where

| Setting | PC | Steam Frame (Android) | Notes |
| --- | --- | --- | --- |
| Tile screen-space error | 4 | 6 | Lower is finer. Set at startup only; changing it later recreates the tileset. |
| Tile cache | 4096 MB | 1536 MB | |
| MSAA | 4x | 2x | Written to the URP asset by `RuntimeQuality`, which is where URP reads MSAA from. |
| Render scale | 1.25 | 1.0 | Same route. |
| Bloom | on | **off** | The pipeline is LDR and bloom's threshold is 1.1, so it adds several full-screen passes for almost no visible change. |
| Sun shadows | on | on | Switch off to test their cost. |
| Flat tile lighting | off | off | `standaloneFlatTileLighting` / `pcFlatTileLighting`: light tiles with flat ambient only, so the baked photogrammetry lighting is not re-lit by the sun. Test for line artifacts that are really faceted shading. |
| Edge preload margin | n/a | 1.1 (was 1.2) | Headset-only extra camera that loads tiles just outside the view. |
| Foveation level | n/a | 0.25 (was 0.5) | 0.5 looked too aggressive on Steam Frame. Higher is stronger; costs GPU time when lowered. |

PC and headset share one URP asset and one quality level, so these are applied
at startup from `EarthVRSettings` rather than baked into the asset. In the
Editor the asset's authored MSAA and render scale are restored when play stops.

The hand menu's performance panel now includes a **Render** line (MSAA, render
scale, eye texture size, foveation level) so you can confirm what is really in
effect.

## Changing settings on the device without rebuilding

Create `settings-override.json` in the app's data folder, next to the saved
Cesium token:

- Steam Frame: `<Steam library>/steamapps/compatdata/<id>/external/Android/data/com.frameearthvr.app/files/EarthVR/`
- Windows: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\FrameEarthVR\EarthVR\`

Any public `EarthVRSettings` field it names replaces the built-in value;
missing fields keep theirs. Values are clamped to safe ranges, a malformed file
is ignored, and the file is read once at launch.

```json
{
  "humanNearClipMeters": 0.1,
  "standaloneFoveationLevelOverride": 0,
  "standaloneMsaa": 1
}
```

Delete the file to return to the built-in values.

## Thin gaps and see-through "X" marks between tiles (Android only)

These have not been reproduced or root-caused. The PC build uses different
depth and foveation paths, so the Android-only features are the suspects. Test
one change at a time and note which one removes the artifact.

| Try | How | What it points to |
| --- | --- | --- |
| Foveation off | `{"standaloneFoveationLevelOverride": 0}` | Foveated rendering (fragment density) artifacts |
| Larger near plane | `{"humanNearClipMeters": 0.1}` | Depth precision: the near plane is 0.03 m with a far plane of at least 100 km, and the headset's depth buffer may be less precise than PC's. Overlapping tile borders then z-fight, which looks like crossing see-through lines. |
| MSAA off | `{"standaloneMsaa": 1}` | MSAA resolve / coverage interaction |
| Coarser tiles | `{"standaloneMaximumScreenSpaceError": 12}` | Cracks between tiles at different detail levels, which change when the detail level changes |
| No render regions / symmetric projection / buffer discards | Uncheck **EarthVR > Steam Frame > Force Performance Features On Build**, change them in Project Settings > XR Plug-in Management > OpenXR > Android, then build | The Android-only multiview optimizations |

Builds normally re-force foveation, render regions, symmetric projection and
buffer discards every time. Unchecking the menu item keeps whatever Project
Settings says. Re-check it to restore the previous behavior.

What a headset screenshot of London showed: faint straight lines in a grid on
the flat river surface, dashed rather than continuous, crossing where tile
corners meet (so they read as "X" marks under perspective), and sitting in the
sharp centre of the view rather than the periphery. Their brightness differs from
the surrounding water by only about 15-18 levels out of 255, with both darker and
lighter lines. That points at tile borders (hairline rasterization gaps and
texture-edge differences between neighbouring tiles) rather than foveation, and
it is the pattern a higher MSAA count or render scale would soften. Whether PC
shows the same lines at the old MSAA 2 / scale 1.0 is the key comparison.

A second pair of screenshots (Singapore, Marina Bay) after the foveation change
shows the same lines on the water, but here they are irregular polygon outlines
(V and Z shapes, each a dark line beside a pale one) rather than a tile grid.
Two explanations fit and they are told apart by behaviour: overlapping near-coplanar
surfaces fighting for depth (lines shimmer when the head moves; PC with a float
depth buffer would not show them) versus texture-atlas edge bleeding (lines stay
fixed on the surface; PC would show them too). The performance panel's **Clip**
line gives the near/far distances, user scale and depth bits needed to estimate
depth resolution at distance: roughly distance squared over near times 2^bits.

Changing settings without a rebuild by pushing the override file with adb (the
Frame runs a real Android, so this usually works; the app reads the file at launch):

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
$s = "192.168.50.138:5555"
Set-Content -Path .\settings-override.json -Value '{"humanNearClipMeters": 0.3}' -Encoding ascii
& $adb -s $s push .\settings-override.json /sdcard/Android/data/com.frameearthvr.app/files/EarthVR/settings-override.json
& $adb -s $s shell am force-stop com.frameearthvr.app
& $adb -s $s shell monkey -p com.frameearthvr.app -c android.intent.category.LAUNCHER 1
# wait about 40 seconds for the app to start, then confirm it was read:
& $adb -s $s logcat -d -s Unity | Select-String "override","foveation"
```

Use `-Encoding ascii`: a byte-order mark makes the JSON fail to parse.

If gaps persist in every configuration they are most likely inherent to Google's
photogrammetry tiles at neighboring detail levels. Cesium already keeps parent
tiles until their children are ready (`forbidHoles`), so the remaining
options would be changes to what is drawn behind the tiles.

## Foveation and eye tracking

Fixed foveation keeps the sharp region at the centre of each eye, so a level
that looks fine in the middle can look blocky in the periphery (stepped
horizon, horizontally smeared trees at the edges). The level is applied at
startup from `standaloneFoveationLevelOverride` (0-1, higher is stronger,
negative leaves the build-time level alone). Lowering it costs GPU time.

Eye-tracked foveation moves the sharp region with your gaze, so a tighter level
costs nothing you can see. It is a build option: **EarthVR > Steam Frame >
Eye-Tracked Foveation** (unchecked by default) sets Valve's `initialUseEyeTracking`.
Whether it works depends on the Steam Frame runtime; the app only asks for it.
When first enabled it left half of one eye blurry, as if the gaze centre were
misplaced for that eye, so it is off by default. If the blur remains with it off,
the cause is more likely the Android-only symmetric projection / render regions.
If the sharp region still stays fixed, capture `adb logcat -s Unity` and the
lines mentioning foveation or eye gaze.

## Reading the performance panel

Open the hand menu and press **PERFORMANCE OVERLAY**.

- **CPU main / render thread / GPU** (ms): the side closest to the frame time is
  the limit. GPU near the frame time means pixel or geometry bound (render
  scale, MSAA, foveation, tile detail help). CPU main or render thread near it
  means tile processing or draw submission is the limit, and render scale and
  foveation will barely help. GPU shows `n/a` where the device has no GPU timer.
- **Render**: MSAA and scale are the values written to the URP asset, not a
  readback of what the GPU does. The eye texture size is the real check: it
  should grow by the render scale on each axis.

## Bisecting the headset-only blur and lines

PC shows neither the blur nor the same lines, so the cause is among the
Android-only OpenXR optimizations. Each is now a build switch under
**EarthVR > Steam Frame > Android Features** (all checked by default, which is the
previous behavior): Foveated Rendering, Render Regions + Symmetric Projection,
Buffer Discards, Late Latching. Eye-tracked foveation is a separate item in the
Steam Frame menu. **SRP Foveation API (off = Legacy)** chooses which Unity foveation
path is used.

Change one, run **Build Development APK**, and read the **Build** line in the
performance panel. It ends in a tag such as `+F1R0B1L1E0S1` (F foveation,
R render regions + symmetric projection, B buffer discards, L late latching,
E eye-tracked foveation, S SRP foveation API; 1 = on), so you can always tell which build is installed.
The same tag is logged in `Logs/SteamFrameBuild.log`.

Suggested order:

1. Uncheck **Render Regions + Symmetric Projection** only. If the blur becomes
   centred, those two were misplacing the foveation map.
2. If it is still offset, uncheck **Foveated Rendering** to confirm foveation is the
   source, and compare frame time.
3. Check the lines in each build; Buffer Discards is the next suspect for them.

The panel's lines are short on purpose: the card fits about ten.

## Capturing logs from the headset

One command relaunches the game, optionally pushes a settings override first, waits,
and saves the device log (the cloud session cannot reach a headset on your network,
so the file is what gets attached to the chat):

```powershell
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -Override '{"standaloneMsaa": 1}'
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -ClearOverride
```

It prints the installed version, saves `Logs\frame-logcat-<time>-filtered.txt` (attach
this) and the complete `Logs\frame-logcat-<time>.txt`. If pushing the override fails,
the error is shown; edit `EarthVRSettings.asset` and rebuild instead.

Lines starting `EarthVR-XR` are written by the game once XR is running: the OpenXR
runtime and its **enabled extensions** (look for `XR_FB_foveation`,
`XR_META_foveation_eye_tracked`, an eye-gaze extension), the foveation level and
flags, eye texture size and depth bits. Development builds also log a `perf` line
every 10 seconds with frame time and the CPU/GPU split.

Valve's documentation notes that foveated rendering on Steam Frame may not render
correctly with MSAA enabled (stated for Unity 2022.3). This project uses MSAA 2x, so
`{"standaloneMsaa": 1}` is the first thing to try for static blocky patches.

### Left eye wrong, right eye perfect (eye-tracked foveation)

With eye-tracked foveation on, the sharp region followed gaze but in the left eye the
blurred/sharp boundary sat in the middle of the view while the right eye was correct.
A per-eye difference like that points at how the foveation map is placed for one
eye's image, not at the gaze itself. Things to vary, one per build, with
eye-tracking checked:

1. **Render Regions + Symmetric Projection** on versus off.
2. **SRP Foveation API** on versus off (Legacy).
3. Foveation level (no rebuild): `{"standaloneFoveationLevelOverride": 0.1}`. A larger
   sharp region hides a constant offset.

Record for each: which eye, whether the boundary is straight or round, whether it
moves with gaze, and the `EarthVR-XR display` log line (foveation flags).

## Not done

- Baking the day/night colour grade into a custom tile shader (avoids the
  post-process pass entirely). It changes shaders that cannot be validated
  without a Unity build and this project has already had a stripped-shader
  startup failure.
- Adaptive tile detail. Changing a tileset property recreates the tileset, so
  it would have to scale a hidden streaming camera instead.
- A separate Android quality level / URP asset in the project files.
