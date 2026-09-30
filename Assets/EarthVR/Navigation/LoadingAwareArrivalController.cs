using System;
using System.Collections;
using CesiumForUnity;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Scaling;
using EarthVR.Sky;
using EarthVR.Terrain;
using UnityEngine;
using UnityEngine.UI;

namespace EarthVR.Navigation
{
    /// <summary>Owns comfortable long-range travel and only reveals the new
    /// view after Cesium has useful content ready (or a bounded timeout).</summary>
    public sealed class LoadingAwareArrivalController : MonoBehaviour
    {
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private NavigationController _navigation;
        private WorldManipulationController _scaling;
        private GroundingController _grounding;
        private SunSkyController _sunAndSky;
        private Cesium3DTileset _tileset;
        private IBookmarkProvider _places;
        private Image _fadeImage;
        private Text _statusText;
        private GameObject _fadeCanvasObject;
        private Coroutine _arrival;

        public bool IsArriving { get; private set; }
        public float LoadProgress { get; private set; }
        public string StatusMessage { get; private set; } = string.Empty;

        public void Initialize(
            EarthVRSettings settings,
            EarthVRRig rig,
            NavigationController navigation,
            WorldManipulationController scaling,
            GroundingController grounding,
            SunSkyController sunAndSky,
            Cesium3DTileset tileset,
            IBookmarkProvider places)
        {
            _settings = settings;
            _rig = rig;
            _navigation = navigation;
            _scaling = scaling;
            _grounding = grounding;
            _sunAndSky = sunAndSky;
            _tileset = tileset;
            _places = places;
            CreateFadeSurface();
        }

        public SavedPlace CaptureCurrentPlace(string name = null)
        {
            var llh = _navigation.LongitudeLatitudeHeight;
            return PlaceLibraryRules.Prepare(new SavedPlace
            {
                name = string.IsNullOrWhiteSpace(name) ? _navigation.CurrentPlaceName : name,
                longitude = llh.x,
                latitude = llh.y,
                heightMeters = llh.z,
                headingDegrees = _navigation.HeadingDegrees,
                userScale = _scaling.UserScale,
                movementMode = _navigation.State.Mode,
                utcTimeTicks = _sunAndSky.UtcTime.Ticks
            });
        }

        public SavedPlace CreateSearchDestination(GeographicPlace place) =>
            PlaceLibraryRules.Prepare(new SavedPlace
            {
                name = place.Name,
                longitude = place.Longitude,
                latitude = place.Latitude,
                heightMeters = place.HeightMeters > 0d
                    ? place.HeightMeters
                    : _settings.searchArrivalHeightMeters,
                headingDegrees = 0f,
                userScale = 1f,
                movementMode = MovementMode.Flight,
                utcTimeTicks = _sunAndSky.UtcTime.Ticks
            });

        public void TravelTo(SavedPlace place, bool recordRecent = true)
        {
            if (!PlaceLibraryRules.IsValid(place))
                return;
            if (_arrival != null)
                StopCoroutine(_arrival);
            _arrival = StartCoroutine(TravelRoutine(PlaceLibraryRules.Prepare(place), recordRecent));
        }

        /// <summary>Finishes a travel path that already reached its destination
        /// continuously, so persistence and solar time are updated without the
        /// fade-and-teleport arrival pipeline.</summary>
        public void CompleteSeamlessArrival(SavedPlace place, bool recordRecent)
        {
            if (!PlaceLibraryRules.IsValid(place))
                return;
            place = PlaceLibraryRules.Prepare(place);
            _sunAndSky.SetDaylightForLocation(place.longitude, place.latitude);
            place.utcTimeTicks = _sunAndSky.UtcTime.Ticks;
            if (recordRecent)
                _places.RecordRecent(place);
            LoadProgress = _tileset == null || _tileset.suspendUpdate
                ? 100f
                : _tileset.ComputeLoadProgress();
            StatusMessage = string.Empty;
        }

        private IEnumerator TravelRoutine(SavedPlace place, bool recordRecent)
        {
            IsArriving = true;
            _navigation.NavigationEnabled = false;
            _scaling.InteractionsEnabled = false;
            _fadeImage.gameObject.SetActive(true);
            _statusText.text = $"Travelling to\n{place.name}";
            StatusMessage = $"Travelling to {place.name}";

            yield return Fade(1f);

            _grounding.RestoreModeForArrival(place.movementMode);
            var restoredScale = place.userScale;
            _scaling.SetUserScaleKeepingObserverFixed(restoredScale);
            _navigation.GoToLocation(
                place.longitude,
                place.latitude,
                place.heightMeters,
                place.headingDegrees,
                place.name);
            _sunAndSky.SetDaylightForLocation(place.longitude, place.latitude);
            place.utcTimeTicks = _sunAndSky.UtcTime.Ticks;
            if (recordRecent)
                _places.RecordRecent(place);

            // Let Cesium observe the new camera/georeference before accepting a
            // potentially stale 100% load value from the previous destination.
            yield return null;
            yield return null;

            var started = Time.unscaledTime;
            var readySince = -1f;
            while (true)
            {
                LoadProgress = _tileset == null || _tileset.suspendUpdate
                    ? 100f
                    : _tileset.ComputeLoadProgress();
                var elapsed = Time.unscaledTime - started;
                var ready = LoadProgress >= _settings.arrivalLoadPercentage &&
                            elapsed >= _settings.arrivalMinimumBlackSeconds;
                if (ready)
                {
                    if (readySince < 0f)
                        readySince = Time.unscaledTime;
                    if (Time.unscaledTime - readySince >= 0.2f)
                        break;
                }
                else
                {
                    readySince = -1f;
                }

                _statusText.text = $"Loading {place.name}\n{LoadProgress:N0}%";
                StatusMessage = $"Loading arrival: {LoadProgress:N0}%";
                if (elapsed >= _settings.arrivalLoadTimeoutSeconds)
                {
                    StatusMessage = $"Arrival loading timed out at {LoadProgress:N0}%";
                    break;
                }
                yield return null;
            }

            yield return Fade(0f);
            _fadeImage.gameObject.SetActive(false);
            _navigation.NavigationEnabled = true;
            _scaling.InteractionsEnabled = true;
            IsArriving = false;
            _arrival = null;
            if (!StatusMessage.Contains("timed out"))
                StatusMessage = string.Empty;
        }

        private IEnumerator Fade(float targetAlpha)
        {
            var color = _fadeImage.color;
            var startAlpha = color.a;
            var elapsed = 0f;
            var duration = Mathf.Max(0.01f, _settings.arrivalFadeSeconds);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                color.a = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(elapsed / duration));
                _fadeImage.color = color;
                _statusText.color = new Color(0.85f, 0.95f, 1f, color.a);
                yield return null;
            }
            color.a = targetAlpha;
            _fadeImage.color = color;
            _statusText.color = new Color(0.85f, 0.95f, 1f, targetAlpha);
        }

        private void CreateFadeSurface()
        {
            var canvasObject = new GameObject("Loading-aware Arrival Fade", typeof(Canvas));
            _fadeCanvasObject = canvasObject;
            canvasObject.transform.SetParent(_rig.Camera.transform, false);
            // Keep status text at a comfortable focal distance in VR. The
            // previous 20 cm placement forced uncomfortable eye convergence.
            canvasObject.transform.localPosition = new Vector3(0f, 0f, 1.5f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.001f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _rig.Camera;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500;

            var fadeObject = new GameObject("Blackout", typeof(RectTransform), typeof(Image));
            fadeObject.transform.SetParent(canvasObject.transform, false);
            var fadeRect = fadeObject.GetComponent<RectTransform>();
            fadeRect.sizeDelta = new Vector2(5000f, 5000f);
            _fadeImage = fadeObject.GetComponent<Image>();
            _fadeImage.color = new Color(0.005f, 0.008f, 0.012f, 0f);
            _fadeImage.raycastTarget = false;

            var statusObject = new GameObject("Arrival Status", typeof(RectTransform), typeof(Text));
            statusObject.transform.SetParent(canvasObject.transform, false);
            var statusRect = statusObject.GetComponent<RectTransform>();
            statusRect.sizeDelta = new Vector2(1000f, 260f);
            _statusText = statusObject.GetComponent<Text>();
            _statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _statusText.fontSize = 42;
            _statusText.alignment = TextAnchor.MiddleCenter;
            _statusText.color = new Color(0.85f, 0.95f, 1f, 0f);
            _statusText.raycastTarget = false;
            _fadeImage.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (!IsArriving)
                return;
            _navigation.NavigationEnabled = true;
            _scaling.InteractionsEnabled = true;
            if (_fadeImage != null)
                _fadeImage.gameObject.SetActive(false);
            IsArriving = false;
            _arrival = null;
        }

        private void OnDestroy()
        {
            if (_fadeCanvasObject != null)
                Destroy(_fadeCanvasObject);
        }
    }
}
