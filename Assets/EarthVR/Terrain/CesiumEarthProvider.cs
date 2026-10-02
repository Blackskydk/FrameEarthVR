using System.Collections;
using System.Text.RegularExpressions;
using CesiumForUnity;
using EarthVR.Configuration;
using UnityEngine;
using UnityEngine.XR;

namespace EarthVR.Terrain
{
    public sealed class CesiumEarthProvider : MonoBehaviour
    {
        // Cesium ion's curated Google Photorealistic 3D Tiles asset.
        private const long GooglePhotorealistic3DTilesAssetId = 2275207;
        private const long CesiumWorldTerrainAssetId = 1;
        private const long BingMapsAerialAssetId = 2;

        public CesiumGeoreference Georeference { get; private set; }
        public Cesium3DTileset Tileset { get; private set; }
        public bool HasAccessToken { get; private set; }
        public bool IsUsingTerrainFallback { get; private set; }
        public string StatusMessage { get; private set; } = "Loading Cesium ion configuration";
        private string _ionAccessToken;
        private CesiumIonRasterOverlay _fallbackImagery;
        private Coroutine _fallbackActivation;
        private Coroutine _rateLimitRetry;
        private Coroutine _streamingHealthCheck;
        private float _rateLimitRetryAt;
        private float _rateLimitRecoveryAt;
        private int _consecutiveRateLimits;
        private string _rateLimitSourceName = "Google 3D Tiles";
        private Camera _streamingCamera;
        private Camera _edgePreloadCamera;
        private float _edgePreloadMargin = 1.2f;
        private bool _preferGooglePhotorealisticTiles;
        private static readonly Camera.StereoscopicEye[] PreloadEyes =
            { Camera.StereoscopicEye.Left, Camera.StereoscopicEye.Right };

        private void OnEnable()
        {
            Cesium3DTileset.OnCesium3DTilesetLoadFailure += OnTilesetLoadFailure;
            CesiumRasterOverlay.OnCesiumRasterOverlayLoadFailure += OnRasterOverlayLoadFailure;
        }

        private void OnDisable()
        {
            Cesium3DTileset.OnCesium3DTilesetLoadFailure -= OnTilesetLoadFailure;
            CesiumRasterOverlay.OnCesiumRasterOverlayLoadFailure -= OnRasterOverlayLoadFailure;
            if (_fallbackActivation != null)
            {
                StopCoroutine(_fallbackActivation);
                _fallbackActivation = null;
            }
            if (_rateLimitRetry != null)
            {
                StopCoroutine(_rateLimitRetry);
                _rateLimitRetry = null;
            }
            if (_streamingHealthCheck != null)
            {
                StopCoroutine(_streamingHealthCheck);
                _streamingHealthCheck = null;
            }
        }

        private void Update()
        {
            UpdateEdgePreloadCamera();
            if (_rateLimitRetry != null)
            {
                var seconds = Mathf.Max(0f, _rateLimitRetryAt - Time.unscaledTime);
                StatusMessage = $"{_rateLimitSourceName} rate limited; retrying in {seconds:N0}s";
            }
            else if (_consecutiveRateLimits > 0 && Time.unscaledTime >= _rateLimitRecoveryAt)
            {
                _consecutiveRateLimits = 0;
                StatusMessage = IsUsingTerrainFallback
                    ? "Cesium World Terrain fallback streaming"
                    : "Google Photorealistic 3D Tiles streaming via Cesium ion";
            }
        }

        public void Initialize(EarthVRSettings settings, Camera streamingCamera)
        {
            Georeference = gameObject.AddComponent<CesiumGeoreference>();
            Georeference.SetOriginLongitudeLatitudeHeight(
                settings.startLongitude,
                settings.startLatitude,
                0d);
            Georeference.scale = 1d;

            // Do not leave the native tile selector to discover Camera.main in
            // the middle of OpenXR startup. In the Editor it also considers the
            // Scene View camera by default; that unrelated view can consume the
            // tile budget while the headset receives no useful tiles. Register
            // the actual stereo camera explicitly and exclusively.
            var cameraManager = CesiumCameraManager.GetOrCreate(gameObject);
            cameraManager.useMainCamera = false;
            cameraManager.useSceneViewCameraInEditor = false;
            cameraManager.additionalCameras.Clear();
            if (streamingCamera != null)
            {
                cameraManager.additionalCameras.Add(streamingCamera);
                _streamingCamera = streamingCamera;
                if (Application.isMobilePlatform)
                {
                    _edgePreloadMargin = Mathf.Clamp(settings.standaloneEdgePreloadMargin, 1f, 1.5f);
                    var preloadObject = new GameObject("Terrain Edge Preload View", typeof(Camera));
                    preloadObject.transform.SetParent(streamingCamera.transform, false);
                    _edgePreloadCamera = preloadObject.GetComponent<Camera>();
                    _edgePreloadCamera.enabled = false;
                    _edgePreloadCamera.stereoTargetEye = StereoTargetEyeMask.None;
                    cameraManager.additionalCameras.Add(_edgePreloadCamera);
                    UpdateEdgePreloadCamera();
                }
            }

            _preferGooglePhotorealisticTiles = settings.preferGooglePhotorealisticTiles;
            IsUsingTerrainFallback = !_preferGooglePhotorealisticTiles;
            var tilesObject = new GameObject(IsUsingTerrainFallback
                ? "Cesium World Terrain + Bing Maps Aerial"
                : "Google Photorealistic 3D Tiles (via Cesium ion)");
            tilesObject.transform.SetParent(transform, false);
            Tileset = tilesObject.AddComponent<Cesium3DTileset>();
            ConfigureTileset(Tileset, settings);
            Tileset.tilesetSource = CesiumDataSource.FromCesiumIon;
            Tileset.ionAssetID = IsUsingTerrainFallback
                ? CesiumWorldTerrainAssetId
                : GooglePhotorealistic3DTilesAssetId;

            StartCoroutine(LoadCesiumWorld());
        }

        private void UpdateEdgePreloadCamera()
        {
            if (_edgePreloadCamera == null || _streamingCamera == null)
                return;

            var verticalTangent = Mathf.Tan(_streamingCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            var horizontalTangent = verticalTangent * _streamingCamera.aspect;
            if (_streamingCamera.stereoEnabled)
            {
                horizontalTangent = 0.001f;
                verticalTangent = 0.001f;
                foreach (var eye in PreloadEyes)
                {
                    var projection = _streamingCamera.GetStereoProjectionMatrix(eye);
                    // Include both asymmetric eye frusta, then add a loading
                    // margin beyond the visible edges for small head turns.
                    horizontalTangent = Mathf.Max(horizontalTangent,
                        (1f + Mathf.Abs(projection.m02)) / Mathf.Max(0.001f, Mathf.Abs(projection.m00)));
                    verticalTangent = Mathf.Max(verticalTangent,
                        (1f + Mathf.Abs(projection.m12)) / Mathf.Max(0.001f, Mathf.Abs(projection.m11)));
                }
            }
            var margin = _edgePreloadMargin;
            _edgePreloadCamera.fieldOfView = 2f * Mathf.Atan(verticalTangent * margin) * Mathf.Rad2Deg;
            _edgePreloadCamera.nearClipPlane = _streamingCamera.nearClipPlane;
            _edgePreloadCamera.farClipPlane = _streamingCamera.farClipPlane;
            var eyeHeight = XRSettings.eyeTextureHeight > 0 ? XRSettings.eyeTextureHeight : _streamingCamera.pixelHeight;
            // Preserve the visible view's pixel density; widening a view without
            // increasing its virtual resolution would request coarser terrain.
            var height = Mathf.Max(1f, eyeHeight) * margin;
            _edgePreloadCamera.pixelRect = new Rect(0f, 0f, height * horizontalTangent / verticalTangent, height);
            _edgePreloadCamera.aspect = horizontalTangent / verticalTangent;
        }

        private static void ConfigureTileset(Cesium3DTileset tileset, EarthVRSettings settings)
        {
            tileset.suspendUpdate = true;
            tileset.showCreditsOnScreen = true;
            tileset.maximumScreenSpaceError = Application.isMobilePlatform
                ? settings.standaloneMaximumScreenSpaceError
                : settings.pcMaximumScreenSpaceError;
            tileset.maximumCachedBytes = (long)(Application.isMobilePlatform
                ? settings.standaloneCacheMegabytes
                : settings.pcCacheMegabytes) * 1024L * 1024L;
            tileset.maximumSimultaneousTileLoads = (uint)settings.maximumSimultaneousTileLoads;
            tileset.preloadAncestors = true;
            tileset.preloadSiblings = true;
            // Never replace a parent tile until all required children are ready.
            // A slightly slower refinement is preferable to cracks and transient
            // holes spread across the Earth in the tilted/map view.
            tileset.forbidHoles = true;
            tileset.loadingDescendantLimit = 20;
            tileset.enableFrustumCulling = true;
            // Cesium's fog culling is an internal distance heuristic, independent
            // of Unity fog. Disable it so the requested LOD is retained toward
            // the horizon instead of silently dropping distant tiles.
            tileset.enableFogCulling = false;
            tileset.createPhysicsMeshes = settings.createPhysicsMeshes;
        }

        private IEnumerator LoadCesiumWorld()
        {
            string accessToken = null;
            yield return CesiumIonConfiguration.LoadAccessToken(value => accessToken = value);
            while (string.IsNullOrEmpty(accessToken))
            {
                StatusMessage = "Enter your own Cesium ion token in account setup";
                yield return new WaitForSecondsRealtime(0.5f);
                accessToken = UserCredentials.ReadToken();
            }
            HasAccessToken = !string.IsNullOrEmpty(accessToken);
            if (!HasAccessToken)
            {
                StatusMessage = $"Token missing: {CesiumIonConfiguration.LocalFileName}";
                yield break;
            }

            _ionAccessToken = accessToken;
            Tileset.ionAccessToken = _ionAccessToken;
            if (IsUsingTerrainFallback)
            {
                ConfigureTerrainImagery();
                StatusMessage = "Cesium World Terrain + aerial imagery configured";
            }
            else
            {
                StatusMessage = "Google Photorealistic 3D Tiles configured via Cesium ion";
            }
            Tileset.suspendUpdate = false;
            Tileset.RecreateTileset();
            if (_streamingHealthCheck != null)
                StopCoroutine(_streamingHealthCheck);
            _streamingHealthCheck = StartCoroutine(CheckStreamingHealth());
            Debug.Log(IsUsingTerrainFallback
                ? "EarthVR started with Cesium World Terrain and Bing Maps Aerial imagery."
                : "EarthVR configured Google Photorealistic 3D Tiles through Cesium ion.");
        }

        public void ReloadUserCredentials()
        {
            if (_rateLimitRetry != null) StopCoroutine(_rateLimitRetry);
            if (_fallbackActivation != null) StopCoroutine(_fallbackActivation);
            if (_streamingHealthCheck != null) StopCoroutine(_streamingHealthCheck);
            _rateLimitRetry = null;
            _fallbackActivation = null;
            _streamingHealthCheck = null;
            StopAllCoroutines();
            _consecutiveRateLimits = 0;
            HasAccessToken = false;
            Tileset.suspendUpdate = true;
            Tileset.ionAccessToken = string.Empty;
            _ionAccessToken = null;
            if (_fallbackImagery != null)
            {
                _fallbackImagery.enabled = false;
                _fallbackImagery.ionAccessToken = string.Empty;
            }
            IsUsingTerrainFallback = !_preferGooglePhotorealisticTiles;
            Tileset.ionAssetID = IsUsingTerrainFallback ? CesiumWorldTerrainAssetId : GooglePhotorealistic3DTilesAssetId;
            Tileset.RecreateTileset();
            StartCoroutine(LoadCesiumWorld());
        }

        private IEnumerator CheckStreamingHealth()
        {
            // OpenXR may not expose valid stereo views until its session becomes
            // visible. Give it time, then recreate once from the now-valid
            // headset camera if Cesium has not produced any renderable tile.
            yield return new WaitForSecondsRealtime(8f);
            if (Tileset == null || Tileset.suspendUpdate)
            {
                _streamingHealthCheck = null;
                yield break;
            }

            var rendererCount = CountTerrainRenderers();
            var loadProgress = Tileset.ComputeLoadProgress();
            if (rendererCount == 0)
            {
                StatusMessage = "Terrain camera ready; retrying initial tile selection";
                Debug.LogWarning(
                    $"EarthVR terrain produced no renderers after OpenXR startup " +
                    $"(load progress {loadProgress:N0}%). Retrying once with the headset camera.");
                Tileset.RecreateTileset();
                yield return new WaitForSecondsRealtime(12f);
                if (Tileset != null && !Tileset.suspendUpdate)
                {
                    rendererCount = CountTerrainRenderers();
                    loadProgress = Tileset.ComputeLoadProgress();
                }
            }

            if (rendererCount == 0)
            {
                StatusMessage = $"No terrain tiles rendered (load {loadProgress:N0}%)";
                Debug.LogError(
                    $"EarthVR terrain is configured but has no renderable tiles " +
                    $"(load progress {loadProgress:N0}%). Check the preceding Cesium network message.");
            }
            else
            {
                StatusMessage = IsUsingTerrainFallback
                    ? "Cesium World Terrain + aerial imagery streaming"
                    : "Google Photorealistic 3D Tiles streaming via Cesium ion";
                Debug.Log(
                    $"EarthVR terrain visible: {rendererCount} tile renderers, " +
                    $"load progress {loadProgress:N0}%.");
            }
            _streamingHealthCheck = null;
        }

        private int CountTerrainRenderers() =>
            Tileset == null
                ? 0
                : Tileset.GetComponentsInChildren<MeshRenderer>(true).Length;

        private void OnTilesetLoadFailure(Cesium3DTilesetLoadFailureDetails details)
        {
            if (details.tileset != Tileset)
                return;

            if (details.httpStatusCode == 429)
            {
                if (!IsUsingTerrainFallback)
                    QueueTerrainFallback();
                else if (IsGooglePhotorealisticFailure(details.message))
                    Debug.Log("Ignoring a late Google 3D Tiles 429 after the terrain fallback was activated.");
                else
                    PauseForRateLimit("Cesium terrain fallback");
                return;
            }

            _consecutiveRateLimits = 0;
            _rateLimitRecoveryAt = float.PositiveInfinity;
            var sourceName = IsUsingTerrainFallback ? "Cesium terrain fallback" : "Google 3D Tiles";
            StatusMessage = $"{sourceName} error (HTTP {details.httpStatusCode})";
            Debug.LogError(
                $"EarthVR could not load {sourceName}: " +
                SanitizeFailureMessage(details.message));
        }

        private void OnRasterOverlayLoadFailure(CesiumRasterOverlayLoadFailureDetails details)
        {
            if (details.overlay == null || Tileset == null || details.overlay.gameObject != Tileset.gameObject)
                return;

            if (details.httpStatusCode == 429 && IsUsingTerrainFallback)
            {
                PauseForRateLimit("Cesium fallback imagery");
                return;
            }

            var sourceName = IsUsingTerrainFallback ? "Cesium fallback imagery" : "Google 3D Tiles overlay";
            StatusMessage = $"{sourceName} error (HTTP {details.httpStatusCode})";
            Debug.LogError(
                $"EarthVR {sourceName} failed: " +
                SanitizeFailureMessage(details.message));
        }

        private void QueueTerrainFallback()
        {
            if (Tileset == null || IsUsingTerrainFallback || _fallbackActivation != null)
                return;

            StatusMessage = "Google quota reached; preparing Cesium terrain fallback";
            _fallbackActivation = StartCoroutine(ActivateTerrainFallbackAfterNativeUpdate());
        }

        private IEnumerator ActivateTerrainFallbackAfterNativeUpdate()
        {
            // Cesium raises load-failure events from inside its native Update.
            // Recreating the tileset from that callback invalidates the native
            // object that is still executing and crashes the Unity process.
            // First leave that update, then suspend for a complete frame before
            // changing the ion asset.
            yield return null;
            if (Tileset == null)
            {
                _fallbackActivation = null;
                yield break;
            }

            Tileset.suspendUpdate = true;
            yield return null;
            ActivateTerrainFallback();
            _fallbackActivation = null;
        }

        private void ActivateTerrainFallback()
        {
            if (Tileset == null || IsUsingTerrainFallback)
                return;

            if (_rateLimitRetry != null)
            {
                StopCoroutine(_rateLimitRetry);
                _rateLimitRetry = null;
            }

            IsUsingTerrainFallback = true;
            _consecutiveRateLimits = 0;
            _rateLimitRecoveryAt = float.PositiveInfinity;
            Tileset.gameObject.name = "Cesium World Terrain (Google 429 fallback)";

            ConfigureTerrainImagery();

            // The source and access token are unchanged. Setting only the asset
            // ID avoids multiple native recreations of the same tileset.
            Tileset.ionAssetID = CesiumWorldTerrainAssetId;
            _fallbackImagery.enabled = true;

            StatusMessage = "Google quota reached; using Cesium World Terrain + aerial imagery";
            Tileset.suspendUpdate = false;
            Debug.LogWarning(
                "Google Photorealistic 3D Tiles returned HTTP 429. " +
                "EarthVR switched this session to Cesium World Terrain with Bing Maps Aerial imagery.");
        }

        private void ConfigureTerrainImagery()
        {
            _fallbackImagery = Tileset.GetComponent<CesiumIonRasterOverlay>();
            if (_fallbackImagery == null)
                _fallbackImagery = Tileset.gameObject.AddComponent<CesiumIonRasterOverlay>();
            _fallbackImagery.enabled = false;
            _fallbackImagery.ionAssetID = BingMapsAerialAssetId;
            _fallbackImagery.ionAccessToken = _ionAccessToken;
            _fallbackImagery.enabled = true;
        }

        private void PauseForRateLimit(string sourceName)
        {
            // Several tile requests can finish after the first 429. One shared
            // pause suppresses that burst and prevents each failure from
            // starting another retry loop.
            if (_rateLimitRetry != null)
                return;
            _consecutiveRateLimits++;
            _rateLimitSourceName = sourceName;
            var delay = CalculateRateLimitBackoffSeconds(_consecutiveRateLimits);
            Tileset.suspendUpdate = true;
            _rateLimitRetryAt = Time.unscaledTime + delay;
            _rateLimitRecoveryAt = float.PositiveInfinity;
            StatusMessage = $"{sourceName} rate limited; retrying in {delay:N0}s";
            Debug.LogWarning(
                $"{sourceName} returned HTTP 429. " +
                $"Streaming is paused for {delay:N0} seconds before one automatic retry.");
            _rateLimitRetry = StartCoroutine(RetryAfterRateLimit(delay));
        }

        private IEnumerator RetryAfterRateLimit(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            _rateLimitRetry = null;
            if (Tileset == null)
                yield break;
            StatusMessage = $"Retrying {_rateLimitSourceName}";
            Tileset.suspendUpdate = false;
            Tileset.RecreateTileset();
            // A full half-minute without another 429 is treated as recovery;
            // otherwise the next pause doubles, up to fifteen minutes.
            _rateLimitRecoveryAt = Time.unscaledTime + 30f;
        }

        public static float CalculateRateLimitBackoffSeconds(int consecutiveFailures)
        {
            var exponent = Mathf.Clamp(consecutiveFailures - 1, 0, 4);
            return Mathf.Min(900f, 60f * Mathf.Pow(2f, exponent));
        }

        public static bool IsGooglePhotorealisticFailure(string message) =>
            !string.IsNullOrEmpty(message) &&
            message.IndexOf(
                "tile.googleapis.com/v1/3dtiles",
                System.StringComparison.OrdinalIgnoreCase) >= 0;

        public static string SanitizeFailureMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;
            return Regex.Replace(
                message,
                @"(?i)(key|access_token|token)=([^&\s]+)",
                "$1=<redacted>");
        }
    }
}
