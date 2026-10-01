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
Eye-Tracked Foveation** (checked by default) sets Valve's `initialUseEyeTracking`.
Whether it works depends on the Steam Frame runtime; the app only asks for it.
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

## Not done

- Baking the day/night colour grade into a custom tile shader (avoids the
  post-process pass entirely). It changes shaders that cannot be validated
  without a Unity build and this project has already had a stripped-shader
  startup failure.
- Adaptive tile detail. Changing a tileset property recreates the tileset, so
  it would have to scale a hidden streaming camera instead.
- A separate Android quality level / URP asset in the project files.
