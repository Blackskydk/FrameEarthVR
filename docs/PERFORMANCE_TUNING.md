# Performance tuning

How the PC and Steam Frame (Android) quality profiles differ, what the in-game
**SETTINGS** page changes, and what is known about the thin lines between tiles.

## What is applied where

| Setting | PC | Steam Frame (Android) | Notes |
| --- | --- | --- | --- |
| Tile screen-space error | 4 | 6 | Lower is finer. Set at startup only; changing it later recreates the tileset. |
| Tile cache | 4096 MB | 1536 MB | |
| MSAA | 4x | 2x | Written to the URP asset by `RuntimeQuality`, which is where URP reads MSAA from. |
| Render scale | 1.25 | 1.0 | Same route. |
| Bloom | on | **off** | The pipeline is LDR and bloom's threshold is 1.1, so it adds several full-screen passes for almost no visible change. |
| Sun shadows | on | on | Drawn only up to `sunShadowMaxUserScale` (4). The shadow distance is fixed in scene units, so at giant scale the shadow map spans kilometres, is too coarse to see, and still redraws every tile. Photoreal tiles already carry baked shadows. |
| Edge preload margin | n/a | 1.1 (was 1.2) | Headset-only extra camera that loads tiles just outside the view. |
| Foveated rendering | n/a | level 0.25, eye-tracked | 0.5 looked too aggressive. Higher is stronger. |
| Render regions, symmetric projection | n/a | off | With them on, the foveation cut-off was clearly visible. |
| Buffer discards, late latching | n/a | on | |
| Log stack traces | n/a | off for Log and Warning | Cesium's mesh-collider warnings arrive hundreds of times a minute; each used to print seven lines. |

PC and headset share one URP asset and one quality level, so these are applied
at startup from `EarthVRSettings` rather than baked into the asset. In the
Editor the asset's authored MSAA and render scale are restored when play stops.

The foveation values are set when the APK is built (`EarthVRSetupWizard`, run by
the build script) and the level can be changed without a rebuild through
`standaloneFoveationLevelOverride` in `EarthVRSettings.asset` (0-1; negative
leaves the build-time level untouched).

## The SETTINGS page

On the hand menu, press the **SETTINGS** ring button (above the other round
buttons). Each button applies immediately and is remembered for the next start
(`UserQualityPreferences`, stored in PlayerPrefs):

| Button | Choices | Notes |
| --- | --- | --- |
| Sun shadows | Auto / On / Off | Auto follows the user-scale limit above. |
| Flat tile lighting | On / Off | Lights tiles with a flat ambient only, so the baked photogrammetry lighting is not re-lit by the sun. Default off. |
| Foveated rendering | On / Off | Headset only; shows n/a on PC. |
| Foveation strength | 0.15 / 0.25 / 0.5 | Applied now if foveation is on, otherwise when it is turned back on. |
| Eye tracking | On / Off | Whether the sharp region follows the gaze or stays centred. Headset only. |
| MSAA | Off / 2x / 4x | |

A saved choice replaces the built-in value for that setting; there is no reset
button yet. To forget all choices, clear the app's data (headset) or the
`EarthVR.Quality.*` PlayerPrefs entries (PC).

## Thin gaps and see-through "X" marks between tiles (Android)

Not root-caused. Observed on water on the headset, much less on PC: faint,
straight or irregular dashed lines about 15-18 levels (of 255) off the
surrounding colour, crossing where tile corners meet. Behaviour pointed at tile
borders (hairline rasterization gaps, texture-edge differences between
neighbouring tiles) and faceted sun shading on flat photogrammetry rather than
at foveation or depth precision:

- A larger near plane did not change them (it only hid the hands), so depth
  fighting at the near plane is not the cause.
- Foveation off, MSAA 4x and the various foveation/eye-tracking variants all
  looked acceptable in side-by-side headset tests, so no single setting was
  identified as the fix.

If they come back, the SETTINGS page covers the quick checks: **Flat tile
lighting** (rules out faceted shading), **MSAA** (softens border coverage) and
**Foveated rendering** off (rules out fragment-density artifacts). If gaps
persist in every configuration they are most likely inherent to Google's
photogrammetry tiles at neighbouring detail levels. Cesium already keeps parent
tiles until their children are ready (`forbidHoles`).

## Findings from headset measurements (dense London)

- **Runtime:** SteamVR/OpenXR 2.17.10 with Valve's `XR_APILAYER_VALVE_fdm_injection` API
  layer providing `XR_UNITY_foveation`. `XR_META_foveation_eye_tracked` is enabled, so
  eye-tracked foveation is supported. `XR_FB_space_warp`, `XR_META_recommended_layer_resolution`
  and `XR_FB_display_refresh_rate` are available but unused.
- **CPU-bound more than pixel-bound.** Frame times quantise at 27.8 ms (two refreshes of the
  72 Hz display) and the main thread alone took 12-19 ms, so dense areas routinely miss the
  13.9 ms budget. Lowering resolution or foveating bought little; higher MSAA or render scale
  is close to free.
- **The sun shadow pass roughly doubled triangles and draw calls**, which is why shadows are
  skipped at giant user scale.
- **Development builds are slower** (script debugging, profiler hooks). Judge performance with
  **Build Release APK** (`build-and-install-steam-frame.ps1 -Release`).
- **A permission dialog appears at each launch**; the game does not start until it is accepted.
- Frame times drifted up over a long session, which looks like the headset warming up.

For a definitive CPU answer, attach Unity's Profiler to a development build over adb and
record a few hundred frames in a dense city; the main thread's top entries show where it goes.

## Keeping `git status` clean

Android builds write a native build cache into `.utmp/`, which is ignored and no
longer tracked (as are `*.apk`, `*.aab` and `*.unitypackage`). If files under
`Assets/XR/` or `Packages/packages-lock.json` show as modified after a build,
do not commit them unless you meant to change the project's defaults;
`git restore <path>` discards them.

## Not done

- Collider baking: Cesium's `createPhysicsMeshes` costs main-thread time in cities. It was
  never measured with it off, and grounding and collisions depend on it.
- Baking the day/night colour grade into a custom tile shader (avoids the post-process
  pass entirely). It changes shaders that cannot be validated without a Unity build, and this
  project has already had a stripped-shader startup failure.
- Adaptive tile detail. Changing a tileset property recreates the tileset, so it would
  have to scale a hidden streaming camera instead.
- A separate Android quality level / URP asset in the project files.
- Per-frame CPU/GPU timing, an on-device settings override file and a log-capture script were
  used while tuning and then removed; they are in the git history (commits before the
  "Clean up test tooling" commit) if they are needed again.
