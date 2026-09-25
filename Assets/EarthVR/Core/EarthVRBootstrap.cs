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

            var settings = Resources.Load<EarthVRSettings>("EarthVRSettings");
            if (settings == null)
                settings = EarthVRSettings.CreateRuntimeDefaults();

            Application.targetFrameRate = 90;
            QualitySettings.vSyncCount = 0;
            QualitySettings.maxQueuedFrames = 1;
            QualitySettings.antiAliasing = Application.isMobilePlatform ? settings.standaloneMsaa : settings.pcMsaa;
            XRSettings.eyeTextureResolutionScale = Application.isMobilePlatform ? settings.standaloneRenderScale : settings.pcRenderScale;
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
            earth.Initialize(settings);

            var navigation = gameObject.AddComponent<NavigationController>();
            navigation.Initialize(input, settings, rig, earth.Georeference);

            var flightAudio = gameObject.AddComponent<FlightAudioController>();
            flightAudio.Initialize(input, settings, rig, navigation);

            var sunAndSky = gameObject.AddComponent<SunSkyController>();
            sunAndSky.Initialize(input, settings, rig, navigation);

            var scaling = gameObject.AddComponent<WorldManipulationController>();
            scaling.Initialize(input, settings, rig, earth.Georeference, navigation, sunAndSky);

            var grounding = gameObject.AddComponent<GroundingController>();
            grounding.Initialize(settings, rig, navigation, scaling, earth.Georeference, earth.Tileset);

            var vignette = gameObject.AddComponent<ComfortVignetteController>();
            vignette.Initialize(settings, rig, navigation);

            var geocoder = gameObject.AddComponent<NominatimGeocodingProvider>();
            var menu = gameObject.AddComponent<EarthVRWristMenu>();
            menu.Initialize(input, settings, rig, navigation, scaling, earth, geocoder, vignette);

        }
    }
}
