using UnityEngine;

namespace EarthVR.Configuration
{
    [CreateAssetMenu(menuName = "EarthVR/Settings", fileName = "EarthVRSettings")]
    public sealed class EarthVRSettings : ScriptableObject
    {
        [Header("Test start (a fixed, repeatable viewpoint for testing)")]
        [Tooltip("Start at the fixed viewpoint below instead of a random place, arrive in the chosen mode once tiles have loaded, and fix the sun time.")]
        public bool testStartEnabled = false;
        public string testStartName = "Big Ben (test start)";
        public double testStartLongitude = -0.12458;
        public double testStartLatitude = 51.50041;
        [Tooltip("Eye height in metres above the WGS84 ellipsoid. Street level near Big Ben is about 52 m; the grounding system settles the user onto the ground.")]
        public float testStartHeightMeters = 62f;
        [Tooltip("Compass heading; 0 faces north, which looks at the tower from the south.")]
        public float testStartHeadingDegrees = 0f;
        public bool testStartGrounded = true;
        [Min(0.01f)] public float testStartUserScale = 1f;
        [Tooltip("Fixed UTC time for the sun, ISO 8601. Empty keeps the current time.")]
        public string testStartUtc = "2026-06-21T10:00:00Z";

        [Header("Starting location")]
        public bool randomizeStartingLocation = true;
        public string startPlaceName = "Copenhagen";
        public double startLongitude = 12.5683;
        public double startLatitude = 55.6761;
        [Min(0f)] public float startHeightMeters = 300f;

        [Header("User scale")]
        [Min(0.01f)] public float minimumUserScale = 0.05f;
        [Min(1f)] public float maximumUserScale = 20000000f;
        [Min(0.5f)] public float realEyeHeightMeters = 1.75f;
        [Min(0.01f)] public float groundedScaleDoublingsPerSecond = 1.6f;

        [Header("Flight")]
        [Min(0.1f)] public float humanScaleSpeedMetersPerSecond = 1.8f;
        [Min(1f)] public float maximumGeographicSpeedMetersPerSecond = 1500000f;
        [Min(0f)] public float accelerationSeconds = 0.35f;
        [Min(0f)] public float decelerationSeconds = 0.5f;
        [Min(1f)] public float altitudeSpeedMultiplier = 18f;
        [Min(0f)] public float altitudeCruiseFractionPerSecond = 0.35f;
        [Min(1f)] public float scaleSpeedExponent = 0.65f;
        [Min(1f)] public float boostSpeedMultiplier = 8f;

        [Header("Pointing and grab")]
        [Min(1f)] public float pointerDefaultLengthMeters = 25f;
        [Min(10f)] public float pointerMaximumLengthMeters = 2000000f;
        [Min(10f)] public float maximumGrabDistanceMeters = 2000000f;

        [Header("Drag momentum")]
        public bool dragMomentumEnabled = true;
        [Min(0.01f)] public float dragMomentumDecaySeconds = 0.5f;
        [Min(0f)] public float dragMomentumMinimumSpeedMetersPerSecond = 0.05f;

        [Header("Flight safety and comfort")]
        [Min(0f)] public float groundApproachSafetyMeters = 150f;
        [Range(0.05f, 1f)] public float minimumApproachSpeedFraction = 0.15f;
        [Min(0.01f)] public float groundApproachRelaxSeconds = 0.6f;
        [Min(0f)] public float groundApproachLookaheadSeconds = 1.2f;
        [Min(0f)] public float boostEaseSeconds = 0.25f;
        [Min(0.02f)] public float flightCollisionRadiusMeters = 0.18f;
        [Min(0f)] public float flightSurfaceClearanceMeters = 0.03f;

        [Header("Mode-transition perspective shift")]
        [Min(0.05f)] public float modeTransitionSeconds = 0.7f;
        [Min(0.5f)] public float modeGroundSampleTimeoutSeconds = 5f;
        [Header("Flight audio")]
        [Range(0f, 1f)] public float soaringWindMaximumVolume = 0.42f;
        [Min(0.01f)] public float soaringWindResponseSeconds = 0.3f;
        [Min(0.1f)] public float soaringWindFullSpeedMetersPerSecond = 18f;

        [Header("Comfort vignette")]
        public bool comfortVignetteEnabled = false;
        [Min(0.1f)] public float comfortVignetteStartSpeedMetersPerSecond = 1.5f;
        [Min(0.1f)] public float comfortVignetteFullSpeedMetersPerSecond = 6f;
        [Range(0f, 1f)] public float comfortVignetteMaximumAlpha = 0.85f;
        [Min(0.01f)] public float comfortVignetteResponseSeconds = 0.25f;

        [Header("Grounding")]
        [Min(0f)] public float groundClearanceMeters = 0.08f;
        [Min(0.1f)] public float groundProbeDistanceMeters = 1500f;
        [Min(0.01f)] public float groundCorrectionSeconds = 0.55f;
        [Min(0.01f)] public float groundCorrectionAscendSeconds = 0.45f;
        [Min(0f)] public float groundedFloorLookaheadSeconds = 0.25f;
        [Min(0.1f)] public float heightSampleIntervalSeconds = 1.5f;
        [Min(100f)] public float searchArrivalHeightMeters = 2500f;

        [Header("Car mode")]
        [Min(1f)] public float carMaximumSpeedMetersPerSecond = 32f;
        [Min(1f)] public float carMaximumReverseSpeedMetersPerSecond = 10f;
        [Min(0.1f)] public float carAccelerationMetersPerSecondSquared = 8f;
        [Min(0.1f)] public float carBrakingMetersPerSecondSquared = 14f;
        [Min(1f)] public float carSteeringDegreesPerSecond = 72f;
        [Min(0.1f)] public float carCollisionRadiusMeters = 0.65f;

        [Header("Geographic origin rebasing")]
        [Min(10f)] public float originRebaseDistanceUnityMeters = 2000f;
        [Min(0f)] public float originRebaseCooldownSeconds = 0.25f;

        [Header("Loading-aware arrivals")]
        [Range(1f, 100f)] public float arrivalLoadPercentage = 85f;
        [Min(0f)] public float arrivalMinimumBlackSeconds = 0.65f;
        [Min(1f)] public float arrivalLoadTimeoutSeconds = 20f;
        [Min(0.01f)] public float arrivalFadeSeconds = 0.25f;

        [Header("Miniature globe picker")]
        [Min(0.05f)] public float miniatureGlobeRadiusMeters = 0.12f;
        [Range(1f, 2f)] public float miniatureGlobeHoverScale = 1.35f;
        [Range(0.5f, 10f)] public float miniatureGlobeDragThresholdDegrees = 2f;

        [Header("Planetary overview")]
        [Min(1000f)] public float globeOverviewUserScale = 4000000f;
        [Range(20f, 80f)] public float globeOverviewDiameterDegrees = 48f;
        [Min(0.25f)] public float globeOverviewTransitionSeconds = 3f;
        [Min(0.1f)] public float globeOverviewDestinationOrbitSeconds = 1.15f;
        [Min(0.5f)] public float globeOverviewZoomInSeconds = 6f;
        [Range(1f, 100f)] public float globeOverviewMinimumLoadPercentage = 60f;
        [Range(0.05f, 1f)] public float globeOverviewMinimumZoomSpeed = 0.25f;
        [Min(1000f)] public float globeOverviewLabelsMinimumScale = 100000f;
        [Range(4, 64)] public int globeOverviewMaximumLabels = 28;
        [Range(0.01f, 0.2f)] public float globeOverviewLabelSeparation = 0.055f;

        [Header("Tiles")]
        [Tooltip("Try Google Photorealistic 3D Tiles before the Cesium terrain fallback. Disable while Google root requests are quota-limited.")]
        public bool preferGooglePhotorealisticTiles = true;
        [Min(1f)] public float pcMaximumScreenSpaceError = 4f;
        [Min(1f)] public float standaloneMaximumScreenSpaceError = 6f;
        [Min(64)] public int pcCacheMegabytes = 4096;
        [Min(64)] public int standaloneCacheMegabytes = 1536;
        [Min(1)] public int maximumSimultaneousTileLoads = 20;
        public bool createPhysicsMeshes = true;

        [Header("Camera")]
        [Min(0.001f)] public float humanNearClipMeters = 0.03f;
        [Tooltip("Minimum geographic camera range. Above low altitude, the actual range grows automatically to stay beyond Earth's geometric horizon.")]
        [Min(1000f)] public float geographicFarClipMeters = 100000f;
        [Min(1f)] public float horizonFarClipMultiplier = 1.35f;

        [Header("High-altitude horizon")]
        [Min(0f)] public float horizonFogStartAltitudeMeters = 10000f;
        [Range(0.1f, 1f)] public float horizonFogStartFraction = 0.72f;
        [Min(0.5f)] public float horizonFogEndFraction = 1.04f;

        [Header("Platform quality profiles")]
        [Tooltip("MSAA and render scale are written to the URP asset at startup (see RuntimeQuality).")]
        [Range(0.5f, 2f)] public float pcRenderScale = 1.25f;
        [Range(0.5f, 1.5f)] public float standaloneRenderScale = 1f;
        [Range(0, 8)] public int pcMsaa = 4;
        [Range(0, 4)] public int standaloneMsaa = 2;
        [Tooltip("Bloom needs HDR to do much; this pipeline is LDR, so it costs several full-screen passes for almost no visible change.")]
        public bool pcBloom = true;
        public bool standaloneBloom = false;
        [Tooltip("Daytime dynamic sun shadows near the viewer. Photoreal tiles already carry baked shadows.")]
        public bool pcSunShadows = true;
        public bool standaloneSunShadows = true;
        [Tooltip("Sun shadows are only drawn while the user scale is at or below this. The shadow distance is fixed in scene units, so at giant scale the shadow map spans kilometres, is too coarse to see, and still redraws every tile.")]
        [Min(1f)] public float sunShadowMaxUserScale = 4f;
        [Tooltip("Loading margin around the visible view for the headset-only terrain edge preload camera. Smaller loads fewer unseen tiles.")]
        [Range(1f, 1.5f)] public float standaloneEdgePreloadMargin = 1.1f;
        [Tooltip("Foveation level (0-1) applied once XR is running; higher is stronger. Negative leaves the build-time level untouched.")]
        [Range(-1f, 1f)] public float standaloneFoveationLevelOverride = 0.25f;

        [Tooltip("Light tiles with a flat, direction-free ambient instead of the sun. Photogrammetry is already lit, so this avoids re-lighting it and its faceted shading; day/night still dims it.")]
        public bool pcFlatTileLighting = false;
        public bool standaloneFlatTileLighting = false;

        public bool FlatTileLightingEnabled => Application.isMobilePlatform ? standaloneFlatTileLighting : pcFlatTileLighting;
        public bool BloomEnabled => Application.isMobilePlatform ? standaloneBloom : pcBloom;
        public bool SunShadowsEnabled => Application.isMobilePlatform ? standaloneSunShadows : pcSunShadows;

        /// <summary>Keeps values read from the on-device override file inside
        /// ranges the renderer and tile streamer can safely use.</summary>
        public void ClampToSafeRanges()
        {
            pcMaximumScreenSpaceError = Mathf.Max(1f, pcMaximumScreenSpaceError);
            standaloneMaximumScreenSpaceError = Mathf.Max(1f, standaloneMaximumScreenSpaceError);
            pcCacheMegabytes = Mathf.Max(64, pcCacheMegabytes);
            standaloneCacheMegabytes = Mathf.Max(64, standaloneCacheMegabytes);
            maximumSimultaneousTileLoads = Mathf.Max(1, maximumSimultaneousTileLoads);
            humanNearClipMeters = Mathf.Max(0.001f, humanNearClipMeters);
            pcRenderScale = Mathf.Clamp(pcRenderScale, 0.5f, 2f);
            standaloneRenderScale = Mathf.Clamp(standaloneRenderScale, 0.5f, 1.5f);
            pcMsaa = Mathf.Clamp(pcMsaa, 0, 8);
            standaloneMsaa = Mathf.Clamp(standaloneMsaa, 0, 4);
            standaloneEdgePreloadMargin = Mathf.Clamp(standaloneEdgePreloadMargin, 1f, 1.5f);
            sunShadowMaxUserScale = Mathf.Max(1f, sunShadowMaxUserScale);
            standaloneFoveationLevelOverride = Mathf.Clamp(standaloneFoveationLevelOverride, -1f, 1f);
        }

        public static EarthVRSettings CreateRuntimeDefaults()
        {
            var value = CreateInstance<EarthVRSettings>();
            value.hideFlags = HideFlags.DontSave;
            return value;
        }
    }
}
