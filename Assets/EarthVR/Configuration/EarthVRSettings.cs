using UnityEngine;

namespace EarthVR.Configuration
{
    [CreateAssetMenu(menuName = "EarthVR/Settings", fileName = "EarthVRSettings")]
    public sealed class EarthVRSettings : ScriptableObject
    {
        [Header("Starting location (Copenhagen)")]
        public double startLongitude = 12.5683;
        public double startLatitude = 55.6761;
        [Min(0f)] public float startHeightMeters = 300f;

        [Header("User scale")]
        [Min(0.01f)] public float minimumUserScale = 1f;
        [Min(1f)] public float maximumUserScale = 100000f;
        [Min(0.5f)] public float realEyeHeightMeters = 1.75f;
        [Min(0.01f)] public float groundedScaleDoublingsPerSecond = 1.6f;

        [Header("Flight")]
        [Min(0.1f)] public float humanScaleSpeedMetersPerSecond = 1.8f;
        [Min(1f)] public float maximumGeographicSpeedMetersPerSecond = 1500000f;
        [Min(0f)] public float accelerationSeconds = 0.35f;
        [Min(0f)] public float decelerationSeconds = 0.5f;
        [Min(1f)] public float altitudeSpeedMultiplier = 18f;
        [Min(1f)] public float scaleSpeedExponent = 0.65f;
        [Min(1f)] public float boostSpeedMultiplier = 8f;

        [Header("Pointing and cone drag")]
        [Min(1f)] public float pointerDefaultLengthMeters = 25f;
        [Min(10f)] public float pointerMaximumLengthMeters = 2000000f;
        [Min(10f)] public float maximumGrabDistanceMeters = 2000000f;
        [Min(0.01f)] public float coneDragSmoothingSeconds = 0.11f;
        [Min(0.001f)] public float coneDragFastResponseSeconds = 0.02f;
        [Min(0.05f)] public float coneDragFastResponseAngleDegrees = 2f;
        [Min(1f)] public float maximumGrabTranslationGain = 250f;

        [Header("Drag momentum")]
        public bool dragMomentumEnabled = true;
        [Min(0.01f)] public float dragMomentumDecaySeconds = 0.5f;
        [Min(0f)] public float dragMomentumMinimumSpeedMetersPerSecond = 0.05f;

        [Header("Flight safety and comfort")]
        [Min(0f)] public float groundApproachSafetyMeters = 150f;
        [Range(0.05f, 1f)] public float minimumApproachSpeedFraction = 0.15f;
        [Min(0f)] public float boostEaseSeconds = 0.25f;
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
        [Min(0.01f)] public float groundCorrectionSeconds = 0.35f;
        [Min(0.1f)] public float heightSampleIntervalSeconds = 1.5f;
        [Min(100f)] public float searchArrivalHeightMeters = 2500f;

        [Header("Tiles")]
        [Min(1f)] public float pcMaximumScreenSpaceError = 6f;
        [Min(1f)] public float standaloneMaximumScreenSpaceError = 10f;
        [Min(64)] public int pcCacheMegabytes = 3072;
        [Min(64)] public int standaloneCacheMegabytes = 1024;
        [Min(1)] public int maximumSimultaneousTileLoads = 12;
        public bool createPhysicsMeshes = true;

        [Header("Camera")]
        [Min(0.001f)] public float humanNearClipMeters = 0.03f;
        [Min(1000f)] public float geographicFarClipMeters = 20000000f;

        [Header("Platform quality profiles")]
        [Range(0.5f, 2f)] public float pcRenderScale = 1f;
        [Range(0.5f, 1.5f)] public float standaloneRenderScale = 0.9f;
        [Range(0, 8)] public int pcMsaa = 2;
        [Range(0, 4)] public int standaloneMsaa = 2;

        public static EarthVRSettings CreateRuntimeDefaults()
        {
            var value = CreateInstance<EarthVRSettings>();
            value.hideFlags = HideFlags.DontSave;
            return value;
        }
    }
}
