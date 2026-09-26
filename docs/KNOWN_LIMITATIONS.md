# Known limitations

- Unity compilation and headset behavior have not yet been verified in this checkout because no Unity Editor was available in the creation environment.
- Grab is right-hand-only and requires an actual physics hit (a first headset pass found the earlier two-handed grab and sky/ellipsoid-fallback grab both undesirable, so both were removed). Drag momentum, ground-approach flight speed safety, boost easing, the world-space comfort vignette, and the scale-reset-and-snap-to-ground on entering Grounded mode have had one round of in-headset feedback and fixes but are still unverified beyond that; expect another pass of small tuning fixes in `WorldManipulationController.cs`, `NavigationController.cs`, `GroundingController.cs`, and `ComfortVignetteController.cs`.
- Cesium terrain, imagery, and buildings require a locally supplied Cesium ion token and network connectivity.
- Location search uses the public OpenStreetMap Nominatim endpoint for this personal prototype. It performs only explicit searches, caches results in memory, and enforces a one-request-per-second limit. A distributed or higher-traffic release needs a hosted or commercial geocoder.
- The floating menu uses a lightweight physics ray for its own controls, not the full XR Interaction Toolkit UI sample stack.
- The menu's search keyboard is EarthVR's own by default; real SteamVR/OpenVR system keyboard support needs that Unity plugin added to the project and `ISystemKeyboardProvider` implemented against it — see `Assets/EarthVR/UI/ISystemKeyboardProvider.cs`.
- Geographic origin rebasing during intercontinental flight is not yet implemented. Cesium scale preserves tile precision, but long travel far from the configured origin may eventually show Unity float jitter; the next milestone should rebase the georeference while preserving ECEF camera pose and orientation.
- Grounding uses a vertical local ray plus sampled ellipsoid height. At extreme distance from the starting ENU origin, local `Vector3.up` is not the correct geodetic normal until origin rebasing is added.
- Loaded tile counts are not exposed by Cesium's public API; the overlay reports view load percentage and memory instead.
- Controller models/haptics, bookmarks, saved settings, and polished menus are outside the first milestone.
- 90 FPS is a target, not a verified result. Tile density, network, driver, SteamVR render resolution, and location materially affect it.
- Steam Frame performance and controller behavior are unverified without device testing.
