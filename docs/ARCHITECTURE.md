# Geospatial and navigation architecture

## Coordinate ownership

`CesiumGeoreference` owns the WGS84/ECEF-to-local mapping. The Google `Cesium3DTileset` is its identity-transformed child. Code does not accumulate offsets on streamed tiles and never edits tile vertices or geographic metadata.

The XR rig is separate:

`EarthVR Runtime -> Navigation Space -> XR Origin (Floor) -> tracked head/controllers`

Scale-and-fly, yaw rotation, ground correction, and cone drag modify Navigation Space. Physical poses remain local children, preserving room-scale tracking.

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

## Extensibility

`IEarthVRInput` isolates navigation from device layouts. `IGeocodingProvider` and `IBookmarkProvider` are intentionally provider-only interfaces for a later milestone; there is no fake production network implementation. Platform-specific additions should remain behind interfaces and narrowly scoped `UNITY_ANDROID`/`UNITY_STANDALONE_WIN` directives.
