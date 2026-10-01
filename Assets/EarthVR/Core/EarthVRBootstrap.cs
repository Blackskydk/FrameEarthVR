using System;
using EarthVR.Configuration;
using EarthVR.Sound;
using EarthVR.Sky;
using EarthVR.Input;
using EarthVR.Navigation;
using EarthVR.Scaling;
using EarthVR.Terrain;
using EarthVR.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.OpenXR;

namespace EarthVR.Core
{
    [DefaultExecutionOrder(-1000)]
    public sealed class EarthVRBootstrap : MonoBehaviour
    {
        private static EarthVRBootstrap _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (FindFirstObjectByType<EarthVRBootstrap>() != null)
                return;
            new GameObject("EarthVR Runtime").AddComponent<EarthVRBootstrap>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            var settingsAsset = Resources.Load<EarthVRSettings>("EarthVRSettings");
            var settings = settingsAsset != null
                ? Instantiate(settingsAsset)
                : EarthVRSettings.CreateRuntimeDefaults();
            settings.hideFlags = HideFlags.DontSave;
            SettingsOverrides.TryApplyFromDisk(settings);
            SelectStartingPlace(settings);

            if (Application.isMobilePlatform)
            {
                // Cesium's mesh-collider warnings arrive hundreds of times a minute;
                // stack traces made each one seven log lines and cost CPU to capture.
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            }

            Application.targetFrameRate = 90;
            QualitySettings.vSyncCount = 0;
            QualitySettings.maxQueuedFrames = 1;
            RuntimeQuality.Apply(settings);
            if (Application.isMobilePlatform)
                StartCoroutine(RuntimeQuality.ApplyFoveationLevel(settings.standaloneFoveationLevelOverride));
            StartCoroutine(XrDiagnostics.LogStartup());
            StartCoroutine(XrDiagnostics.LogPerformance(10f));
#if UNITY_6000_2_OR_NEWER
            if (OpenXRSettings.Instance != null)
                OpenXRSettings.Instance.useOpenXRPredictedTime = true;
#endif

            var input = gameObject.AddComponent<OpenXRInputReader>();
            input.Initialize(Resources.Load<InputActionAsset>("EarthVRInputActions"));
            gameObject.AddComponent<XRInteractionManager>();
            var rig = EarthVRRig.Create(input, settings);
            rig.NavigationSpace.SetParent(transform, false);

            var earthObject = new GameObject("Cesium Geospatial World");
            earthObject.transform.SetParent(transform, false);
            var earth = earthObject.AddComponent<CesiumEarthProvider>();
            earth.Initialize(settings, rig.Camera);

            var navigation = gameObject.AddComponent<NavigationController>();
            navigation.Initialize(input, settings, rig, earth.Georeference, settings.startPlaceName);

            var flightAudio = gameObject.AddComponent<FlightAudioController>();
            flightAudio.Initialize(input, settings, rig, navigation);

            var sunAndSky = gameObject.AddComponent<SunSkyController>();
            sunAndSky.Initialize(input, settings, rig, navigation);

            var scaling = gameObject.AddComponent<WorldManipulationController>();
            scaling.Initialize(input, settings, rig, earth.Georeference, navigation, sunAndSky);

            var highAltitudePresentation = gameObject.AddComponent<HighAltitudePresentationController>();
            highAltitudePresentation.Initialize(settings, navigation, scaling, sunAndSky);

            var grounding = gameObject.AddComponent<GroundingController>();
            grounding.Initialize(settings, rig, navigation, scaling, earth.Georeference, earth.Tileset);

            var carMode = gameObject.AddComponent<CarModeController>();
            carMode.Initialize(rig, navigation, scaling);

            var originRebaser = gameObject.AddComponent<GeographicOriginRebaser>();
            originRebaser.Initialize(settings, rig, earth.Georeference, scaling, sunAndSky);
            grounding.SetOriginRebaser(originRebaser);

            var vignette = gameObject.AddComponent<ComfortVignetteController>();
            vignette.Initialize(settings, rig, navigation);

            var geocoder = gameObject.AddComponent<NominatimGeocodingProvider>();
            var places = new LocalPlaceLibrary();
            var arrival = gameObject.AddComponent<LoadingAwareArrivalController>();
            arrival.Initialize(settings, rig, navigation, scaling, grounding, sunAndSky, earth.Tileset, places);
            var globePicker = gameObject.AddComponent<MiniatureGlobePicker>();
            globePicker.Initialize(input, settings, rig, navigation, arrival);
            var globeLabels = gameObject.AddComponent<GlobeOverviewLabels>();
            globeLabels.Initialize(settings, rig, earth.Georeference, scaling);
            var globeOverview = gameObject.AddComponent<GlobeOverviewController>();
            globeOverview.Initialize(
                input,
                settings,
                rig,
                earth.Georeference,
                navigation,
                scaling,
                grounding,
                originRebaser,
                earth.Tileset,
                arrival,
                sunAndSky,
                globePicker,
                globeLabels);
            var menu = gameObject.AddComponent<EarthVRWristMenu>();
#if EARTHVR_STEAMVR
            ISystemKeyboardProvider keyboardProvider = new SteamVrSystemKeyboardProvider();
#else
            ISystemKeyboardProvider keyboardProvider = null;
#endif
            menu.Initialize(
                input,
                settings,
                rig,
                navigation,
                originRebaser,
                scaling,
                earth,
                geocoder,
                places,
                arrival,
                globePicker,
                globeOverview,
                carMode,
                vignette,
                keyboardProvider);
            var credentialSetup = gameObject.AddComponent<CredentialSetupPanel>();
            credentialSetup.Initialize(rig, input, earth, navigation, scaling);
            menu.SetCredentialSetup(credentialSetup);
            menu.SetUpdateChecker(gameObject.AddComponent<ReleaseUpdateChecker>());
        }

        private static void SelectStartingPlace(EarthVRSettings settings)
        {
            if (!settings.randomizeStartingLocation || OfflinePlaceCatalog.StartingPlaces.Count == 0)
                return;

            const string previousIndexKey = "EarthVR.PreviousStartingPlace";
            var previousIndex = PlayerPrefs.GetInt(previousIndexKey, -1);
            var random = new System.Random(unchecked(Environment.TickCount * 397 ^ DateTime.UtcNow.Millisecond));
            var index = OfflinePlaceCatalog.NormalizeStartingPlaceIndex(
                random.Next(OfflinePlaceCatalog.StartingPlaces.Count),
                previousIndex);
            if (index < 0)
                return;

            var place = OfflinePlaceCatalog.StartingPlaces[index];
            settings.startPlaceName = place.Name;
            settings.startLongitude = place.Longitude;
            settings.startLatitude = place.Latitude;
            settings.startHeightMeters = place.HeightMeters;
            PlayerPrefs.SetInt(previousIndexKey, index);
            PlayerPrefs.Save();
        }
    }
}
