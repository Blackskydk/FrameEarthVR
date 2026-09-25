# Known limitations

- Unity compilation and headset behavior have not yet been verified in this checkout because no Unity Editor was available in the creation environment.
- The two-handed grab (simultaneous pan/rotate/zoom), drag momentum, ellipsoid-fallback grab, ground-approach flight speed safety, boost easing, and comfort vignette were written without a compiler or headset to check them against. The math has been worked through by hand (in particular the scale-direction and yaw-sign conventions for the two-hand solve), but sign errors or feel issues that only show up in play should be expected on first test and are easy, localized fixes in `WorldManipulationController.cs` and `NavigationController.cs`.
- Cesium terrain, imagery, and buildings require a locally supplied Cesium ion token and network connectivity.
- Location search uses the public OpenStreetMap Nominatim endpoint for this personal prototype. It performs only explicit searches, caches results in memory, and enforces a one-request-per-second limit. A distributed or higher-traffic release needs a hosted or commercial geocoder.
- The wrist panel uses a lightweight physics ray for its own controls, not the full XR Interaction Toolkit UI sample stack.
- Geographic origin rebasing during intercontinental flight is not yet implemented. Cesium scale preserves tile precision, but long travel far from the configured origin may eventually show Unity float jitter; the next milestone should rebase the georeference while preserving ECEF camera pose and orientation.
- Grounding uses a vertical local ray plus sampled ellipsoid height. At extreme distance from the starting ENU origin, local `Vector3.up` is not the correct geodetic normal until origin rebasing is added.
- Loaded tile counts are not exposed by Cesium's public API; the overlay reports view load percentage and memory instead.
- Controller models/haptics, bookmarks, saved settings, and polished menus are outside the first milestone.
- 90 FPS is a target, not a verified result. Tile density, network, driver, SteamVR render resolution, and location materially affect it.
- Steam Frame performance and controller behavior are unverified without device testing.
