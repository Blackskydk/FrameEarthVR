# Geospatial and navigation architecture

## Coordinate ownership

`CesiumGeoreference` owns the WGS84/ECEF-to-local mapping. The Google `Cesium3DTileset` is its identity-transformed child. Code does not accumulate offsets on streamed tiles and never edits tile vertices or geographic metadata.

The XR rig is separate:

`EarthVR Runtime -> Navigation Space -> XR Origin (Floor) -> tracked head/controllers`

Scale-and-fly, yaw rotation, ground correction, and world drag modify Navigation Space. Physical poses remain local children, preserving room-scale tracking. During a drag, the selected ECEF point and controller-local ray form a hard constraint on both the normal update and OpenXR's final before-render pose. Flight also fixes the original hit depth. Grounded instead fixes tracking-floor height and solves ray depth, which preserves the picked surface point on the live pointer without allowing a downward hand sweep to lift the player.

## Giant scale

For selected scale `S`:

- Cesium globe scale is `1 / S`.
- One physical metre therefore spans about `S` geographic metres.
- A configured 1.75 m eye height reads as 175 m at 100x.
- Geographic flight speed is converted back to Unity units by dividing by `S`.
- Near/far planes shrink with scale to retain useful depth precision.

Scale is logarithmic in utility methods and changes continuously from vertical thumbstick intent in Grounded mode. Before changing scale, the user's floor point is stored as an ECEF `double3`. The same coordinate is transformed before/after the Cesium scale change, and Navigation Space receives the delta. This keeps the user's feet planted as the miniature-world scale changes.

## Grounding

Grounded mode uses loaded Cesium physics geometry first. If no nearby collider is available, it calls `SampleHeightMostDetailed` at the current longitude/latitude. The fallback is cached in ECEF—not scale-dependent Unity coordinates—so it remains valid while scaling. Corrections are damped and the previous safe height is retained on a failed sample.

Every path into Grounded first rebases the local frame and projects Navigation Space's forward vector onto the new tangent plane. Reconstructing the root rotation with that heading and world-up removes accumulated pitch/roll without Euler-angle ambiguity, while rotating around the tracked head preserves eye position. This same operation backs the manual recenter action and the final step of a tilted-overview return.

## Origin rebasing

`GeographicOriginRebaser` measures camera distance in scaled Unity-local metres, so its precision threshold remains meaningful at every user scale. Once the threshold is crossed, it captures camera position, forward, and up in ECEF; moves the Cesium origin to the exact camera ECEF point; then solves the Navigation Space pose that represents the same camera pose in the new local frame. Momentum is rotated into that frame and physics transforms are synchronized in the same `LateUpdate`.

## Places and arrivals

`LocalPlaceLibrary` persists bookmarks and a bounded, deduplicated recent-place list under `Application.persistentDataPath`. Each record contains longitude, latitude, ellipsoid height, heading, user scale, movement mode, and UTC solar time. `OfflinePlaceCatalog` provides accent-insensitive prefix/token matching across bundled cities, geographic landmarks, national parks and other prominent POIs; bookmarks and recents are merged ahead of catalog matches. This autocomplete path is synchronous and makes no service request. Explicit submitted searches still use Nominatim. Search results, bookmarks, recents, and miniature-globe selections use `LoadingAwareArrivalController`: they fade out, restore the viewpoint, wait for Cesium's view-load percentage to reach the configured threshold, and then fade back in. Full planetary overview uses a separate continuous path: logarithmic altitude/scale interpolation reveals the globe, a great-circle orbit centers the selected coordinate, the georeference is invisibly moved to that surface point, and a load-adaptive descent reaches the final viewpoint without a blackout or terminal teleport. During this overview, `SunSkyController` temporarily replaces the astronomical Sun direction with the Earth-center-to-viewer direction so the visible hemisphere remains lit. On every arrival, both travel paths choose destination-local solar noon for the active simulation date and persist that normalized time in recents; polar-night destinations fall back to the appropriate summer solstice. `GlobeOverviewLabels` projects the globe-visible subset of the same offline catalog from WGS84 into the current rebased frame each update. It horizon-culls the far side, viewport-culls off-screen entries, and resolves label overlaps in a stable priority order without calling a live places or geocoding provider.

The miniature globe and the `EarthVRWristMenu` hand menu are children of the tracked left controller. The left View/pause button shows or hides them together, and any travel (the arrival pipeline or planetary overview) closes both. The globe uses a generated UV sphere whose longitude/latitude convention is shared by hit conversion, so its local NASA Blue Marble texture acts as an offline visual index. The menu panel billboards toward the viewer while keeping its controller-relative position beside the globe; its live labels and colors expose movement-mode and comfort-vignette state. The right controller remains the only pointing ray.

## High-altitude presentation

Tileset settings, including screen-space error, stay fixed after startup. Every Cesium for Unity `Cesium3DTileset` property setter calls `RecreateTileset()`, which discards all loaded tiles and requests the root again, so no property may be driven per frame. Cesium's distance-based selection already coarsens tiles as altitude increases. Camera range follows the WGS84 geometric horizon, `sqrt(h(2R+h))`, with a safety multiplier and a low-altitude minimum instead of using an unnecessarily interplanetary fixed far plane; this preserves Flight-mode depth precision while keeping the curved limb safely in range. `HighAltitudePresentationController` blends the final band into the procedural sky with matching day/night horizon fog. Flight speed also has an altitude-proportional lower bound, so visual scale and travel scale grow together.

## Extensibility

`IEarthVRInput` isolates navigation from device layouts. `IGeocodingProvider` keeps search replaceable, while `IBookmarkProvider` separates the UI/arrival flow from local JSON persistence. Platform-specific additions should remain behind interfaces and narrowly scoped `UNITY_ANDROID`/`UNITY_STANDALONE_WIN` directives.

Car mode is a third persisted `MovementMode`. `NavigationController` owns its speed, steering, collision sweep, and geographic motion so rebasing, diagnostics, saved viewpoints, and comfort systems remain shared with Flight and Grounded. `GroundingController` treats Car as surface-bound and supplies streamed terrain/photogrammetry height following. `CarModeController` owns only mode entry/exit, suppression of conflicting world manipulation, and the tracking-space cockpit visual.
