# Performance tuning and tile-seam troubleshooting

Nothing here has been measured on a headset by the author of these changes;
they are settings, switches and diagnostics to make that testing quick. Where
a default changed, the reason is given so it can be reverted.

## Repeatable test routine (fixed Big Ben start)

While testing, the game starts at the same viewpoint every time: on the ground about 35 m south
of Big Ben, facing it, in Grounded mode at human scale, with the sun fixed at 10:00 UTC on
21 June. It arrives through the normal fade ("Loading Big Ben ...%") and reveals once tiles are
loaded. It is controlled by `testStartEnabled` in `EarthVRSettings.asset` (also the coordinates,
height, heading, mode, scale and sun time); turn it off with `-Override '{"testStartEnabled": false}'`
or by unchecking the asset field.

1. **Update and install:** `git pull`, then
   `.\scripts\build-and-install-steam-frame.ps1 -DeviceHost 192.168.50.138`
   (add `-Release` when judging performance).
2. **Baseline run:**
   `.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -Label baseline -ClearOverride -GrantPermissions`
3. **In the headset** (the script waits 120 s): put it on, wait for the fade-in at Big Ben, then
   stand still for 20 s looking at the tower, then the Thames. Note the blur/cutoff, the tile lines
   and anything odd.
4. **Send me** `Logs\frame-report-<time>-baseline.txt` plus what you saw.
5. **Each test** is the same command with a new `-Label` and an `-Override` (each override
   replaces the previous one; do not stack them across runs):
   - `-Label shadows-off -Override '{"standaloneSunShadows": false}'`
   - `-Label no-colliders -Override '{"createPhysicsMeshes": false}'`
6. **Repeat the baseline last** (`-Label baseline-again -ClearOverride`). If it is slower than the first
   baseline, the headset is warming up and later runs are penalised regardless of settings.
7. **Finish** with `-Label done -ClearOverride`.

Each run starts at the same place but the user then moves. Try to do the same thing each time
(look at the tower, then the Thames) and avoid pushing the stick while looking up, which grows you
(grounded scaling) and changes what is rendered; the report records your scale and altitude.

## Testing in the headset: TEST TOGGLES

Open the hand menu (left View/menu button) and press the round **TEST TOGGLES** button above the
Flight/Grounded button. The page has six switches that take effect immediately, plus a live readout
(FPS, frame time, CPU, triangle/draw counts, your scale and altitude):

- **SUN SHADOWS: AUTO / ON / OFF.** AUTO draws shadows only at user scale 4x or below (the setting
  `sunShadowMaxUserScale`); ON forces them at any scale, OFF never draws them.
- **FLAT TILE LIGHTING: ON / OFF.** Flat ambient light only (no sun shading or shadows).
- **FOVEATED RENDERING: ON / OFF.** Turning it back on uses the last strength.
- **FOVEATION STRENGTH:** cycles 0.15, 0.25, 0.50 (applied immediately when foveation is on).
- **EYE TRACKING: ON / OFF.** Whether the sharp region follows your gaze (on) or stays fixed (off).
- **MSAA:** cycles OFF, 2x, 4x. This changes render targets while running; if the game ever crashes or
  flickers when pressing it, note which value you switched to.

Because the headset warms up over a session, compare settings back to back in the same place rather than
across separate runs: stay put, press one toggle, wait about ten seconds for the numbers to settle, read them,
press it again. Each change is also written to the session log. Toggles are not remembered between launches.

## Keeping `git status` clean

Android builds write a native build cache into `.utmp/` and Unity rewrites some tracked assets.
`.utmp/` is now ignored and no longer tracked. Remaining modified files after a build are expected to be
the OpenXR/XR settings assets under `Assets/XR/` (they follow the **Android Features** switches in the Steam
Frame menu) and occasionally `Packages/packages-lock.json`. Do not commit them unless you meant to change the
project's defaults; `git restore <path>` discards them. Development builds no longer leave a feature tag in
`ProjectSettings/ProjectSettings.asset`.

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

One command relaunches the game, optionally pushes a settings override and grants the game's
runtime permissions, waits, and writes **one self-describing report** to `Logs\`. The cloud
session cannot reach a headset on your network, so that file is what gets attached to the chat.

```powershell
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -Label baseline
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -Label flat-light -Override '{"standaloneFlatTileLighting": true}'
.\scripts\capture-frame-logs.ps1 -DeviceHost 192.168.50.138 -Label baseline -ClearOverride -GrantPermissions
```

`-Label` names the test and is written into the report; use a different one per run. The report
(`frame-report-<time>-<label>.txt`) contains:

- the exact script parameters and the override file that was actually on the headset,
- the installed version (a development build's version ends in the feature tag, e.g.
  `+F1R1B1L1E1S1`), the device, whether the game was still running, and its runtime permissions,
- the **session log the game writes itself** (`EarthVR/session-log.txt` in the app's data folder):
  the settings it booted with (the override text plus every effective setting), the OpenXR runtime
  and enabled extensions, the display and foveation state, what the hand-menu performance panel was
  showing, any errors, and a `perf` line every 10 seconds with average and worst frame time, the
  CPU/GPU split, render counters (development builds), Cesium collider-warning counts and where in
  the world you were (longitude, latitude, altitude, user scale, mode, tile load percentage),
- the relevant device log lines. The complete device log and a crash buffer are saved beside it.

A permission dialog appears at each launch and the game does not start until it is accepted.
`-GrantPermissions` grants pending runtime permissions over adb so it does not block; otherwise
accept it in the headset.

Pushing the override uses `adb push` into the app's data folder; if it fails, the error is shown;
edit `EarthVRSettings.asset` and rebuild instead.

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

## Quick tests without rebuilding

Each is one run of the capture script (about a minute). Use the same view and compare
screenshots; `-ClearOverride` returns to the built-in values.

| Question | Command |
| --- | --- |
| Is the foveation cutoff softer with a gentler level? | `-Override '{"standaloneFoveationLevelOverride": 0.1}'` |
| Is uniform softness better than a sharp/blurry edge? | `-Override '{"standaloneFoveationLevelOverride": 0, "standaloneRenderScale": 0.9}'` |
| Are the tile lines depth fighting? (near plane) | `-Override '{"humanNearClipMeters": 1.0}'` |
| Are they lighting? | `-Override '{"standaloneFlatTileLighting": true}'` |
| Are they MSAA related? | `-Override '{"standaloneMsaa": 1}'` |

The foveation level may be quantized into a few steps by the runtime, so 0.1 and 0.25 can
look identical; if so, the only gentler setting is off, and the render-scale row is the
alternative that spends similar GPU time without a visible edge.
The near-plane value is a test only: near distance is that value times the square root of
your user scale, so a large value clips close surfaces and hands.

If the game closes during a capture, the script reports it and saves a `-crash.txt`
file; attach it.

## Findings from headset logs (London, development build)

From four captures of the Steam Frame running the development APK in dense London:

- **Runtime:** SteamVR/OpenXR 2.17.10 with Valve's `XR_APILAYER_VALVE_fdm_injection` API
  layer providing `XR_UNITY_foveation`. `XR_FB_foveation`, `XR_FB_foveation_configuration`,
  `XR_FB_foveation_vulkan` and `XR_META_foveation_eye_tracked` are enabled, so eye-tracked
  foveation is supported. `XR_EXT_eye_gaze_interaction`, `XR_FB_space_warp`,
  `XR_EXT_frame_synthesis`, `XR_META_recommended_layer_resolution` and
  `XR_FB_display_refresh_rate` are available but unused.
- **CPU-bound, not pixel-bound.** Frame time stayed at 24-31 ms with foveation off and render
  scale 0.9 (27.8 ms) as with foveation 0.25 at scale 1.0 (24.5-27.8 ms). Many frames are exactly
  27.8 ms, two frames of a 72 Hz display (13.9 ms), and the main thread alone takes
  12.6-18.7 ms, so it routinely misses the 13.9 ms budget and the game drops to every second
  refresh. GPU time is not reported on this platform (`GPU n/a`).
  Because the GPU is not the limit, lowering resolution or foveating buys nothing here;
  higher MSAA or render scale should be nearly free.
- **Development builds are slower.** They carry script debugging, profiler hooks and stack
  traces. Judge performance with **Build Release APK**
  (`build-and-install-steam-frame.ps1 -Release`).
- **Physics mesh warnings.** Cesium baking mesh colliders logs "triangles where the distance
  between any 2 vertices is greater than 500 units" about 230 times a minute. It means the
  tiles contain very large triangles (flat water). Each warning used to print seven lines with
  stack traces, which overflowed the log buffer and evicted the startup lines; stack traces are
  now off for Log and Warning on the headset.
- **A permission dialog appears at each launch.** The game does not start until it is accepted,
  so a capture only sees what happens after that. The capture script now defaults to 120 s and
  asks for a larger log buffer.

Quick CPU tests (read `CPU` in the panel or the `perf` log line; same view each time):

| Question | Override |
| --- | --- |
| How much is collider baking costing? | `{"createPhysicsMeshes": false}` (grounding and collisions then rely on height sampling) |
| How much is tile detail costing? | `{"standaloneMaximumScreenSpaceError": 12}` |
| Can quality rise for free? | `{"standaloneFoveationLevelOverride": 0, "standaloneMsaa": 4}` |

For a definitive answer, attach Unity's Profiler to the development build over adb and record a
few hundred frames in London; the main thread's top entries by time show where it goes.

## Not done

- Baking the day/night colour grade into a custom tile shader (avoids the
  post-process pass entirely). It changes shaders that cannot be validated
  without a Unity build and this project has already had a stripped-shader
  startup failure.
- Adaptive tile detail. Changing a tileset property recreates the tileset, so
  it would have to scale a hidden streaming camera instead.
- A separate Android quality level / URP asset in the project files.
