using System.Collections;
using CesiumForUnity;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Input;
using EarthVR.Scaling;
using EarthVR.Sky;
using EarthVR.Terrain;
using EarthVR.UI;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthVR.Navigation
{
    /// <summary>
    /// Tilts the real Cesium ground into a north-up map in front of the viewer.
    /// The user controls how far to zoom out before selecting a destination.
    /// </summary>
    public sealed class GlobeOverviewController : MonoBehaviour
    {
        private const double EquatorialRadiusMeters = 6378137d;
        private const double PolarRadiusMeters = 6356752.314245d;

        private enum OverviewState
        {
            Inactive,
            Entering,
            Choosing,
            Zooming
        }

        private IEarthVRInput _input;
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private CesiumGeoreference _georeference;
        private NavigationController _navigation;
        private WorldManipulationController _scaling;
        private GroundingController _grounding;
        private GeographicOriginRebaser _originRebaser;
        private Cesium3DTileset _tileset;
        private LoadingAwareArrivalController _arrival;
        private SunSkyController _sunAndSky;
        private MiniatureGlobePicker _miniatureGlobe;
        private GlobeOverviewLabels _globeLabels;
        private OverviewState _state;
        private SavedPlace _returnPlace;
        private double3 _returnSurfaceEcef;
        private bool _rightTriggerWasHeld;
        private Coroutine _transition;
        private Transform _aimMarker;
        private TextMesh _instructions;
        private bool _hasAim;
        private double3 _aimEcef;
        private Vector3 _pathHeadLocalPosition;
        private Quaternion _pathHeadLocalRotation;
        private Vector3 _pathEyePosition;
        private Quaternion _pathEyeRotation;
        private bool _hasPathAnchor;
        private float _overviewStartScale;
        private float _overviewMaximumScale;
        private float _overviewStartAltitude;
        private float _overviewMaximumAltitude;
        private float _overviewZoom01;
        private Vector3 _overviewStartPositionOffset;
        private double _overviewCenterLongitude;
        private double _overviewCenterLatitude;
        private bool _mapPointerDown;
        private bool _mapDragging;
        private Vector3 _mapDragStartDirection;
        private double3 _mapGrabAnchorEcef;
        private float _overviewRollDegrees;
        private Transform _overviewRotationHand;
        private Quaternion _previousOverviewHandLocalRotation;
        private bool _manualZoomWasInward;
        private bool _hasManualZoomAnchor;
        private double3 _manualZoomAnchorEcef;

        public bool IsActive => _state != OverviewState.Inactive;

        public void Initialize(
            IEarthVRInput input,
            EarthVRSettings settings,
            EarthVRRig rig,
            CesiumGeoreference georeference,
            NavigationController navigation,
            WorldManipulationController scaling,
            GroundingController grounding,
            GeographicOriginRebaser originRebaser,
            Cesium3DTileset tileset,
            LoadingAwareArrivalController arrival,
            SunSkyController sunAndSky,
            MiniatureGlobePicker miniatureGlobe,
            GlobeOverviewLabels globeLabels)
        {
            _input = input;
            _settings = settings;
            _rig = rig;
            _georeference = georeference;
            _navigation = navigation;
            _scaling = scaling;
            _grounding = grounding;
            _originRebaser = originRebaser;
            _tileset = tileset;
            _arrival = arrival;
            _sunAndSky = sunAndSky;
            _miniatureGlobe = miniatureGlobe;
            _globeLabels = globeLabels;
            CreateFeedback();
        }

        private void Update()
        {
            if (_input == null || _arrival == null)
                return;

            if (_arrival.IsArriving)
            {
                if (_state != OverviewState.Inactive)
                    YieldToExternalArrival();
                _rightTriggerWasHeld = _input.RightTriggerHeld;
                return;
            }

            if (_state != OverviewState.Inactive)
                UpdateOverviewLighting();

            if (_input.ToggleGlobeOverviewPressed)
            {
                if (_state == OverviewState.Inactive)
                {
                    // A Flight/Grounded perspective transition owns these same
                    // transforms. Ignore the button until that transition settles.
                    if (_navigation.NavigationEnabled && _scaling.InteractionsEnabled)
                        BeginOverview();
                }
                else if (_state == OverviewState.Choosing)
                    BeginZoomAndTravel(_returnSurfaceEcef, _returnPlace, false);
            }

            var rightHeld = _input.RightTriggerHeld;
            var rightPressed = rightHeld && !_rightTriggerWasHeld;
            var rightReleased = !rightHeld && _rightTriggerWasHeld;
            _rightTriggerWasHeld = rightHeld;

            if (_state != OverviewState.Choosing)
                return;

            UpdateAim();
            UpdateMapDrag(rightHeld, rightPressed, rightReleased);
            if (_state == OverviewState.Choosing)
            {
                UpdateManualZoom();
                UpdateOverviewRotation();
            }
        }

        private void YieldToExternalArrival()
        {
            if (_transition != null)
                StopCoroutine(_transition);
            _transition = null;
            _hasAim = false;
            _aimMarker.gameObject.SetActive(false);
            _instructions.gameObject.SetActive(false);
            _globeLabels.SetVisible(false);
            _miniatureGlobe.SetVisible(true);
            _sunAndSky.ClearOverviewSunDirection();
            _state = OverviewState.Inactive;
        }

        private void BeginOverview()
        {
            _returnPlace = _arrival.CaptureCurrentPlace();
            _returnSurfaceEcef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(_returnPlace.longitude, _returnPlace.latitude, 0d));
            _grounding.RestoreModeForArrival(MovementMode.Flight);
            _navigation.NavigationEnabled = false;
            _scaling.InteractionsEnabled = false;
            _miniatureGlobe.SetVisible(false);
            _globeLabels.SetVisible(true);
            _rightTriggerWasHeld = _input.RightTriggerHeld;
            _instructions.gameObject.SetActive(true);
            _instructions.text = "TILTING MAP\nNORTH UP";
            CapturePathAnchor();
            _state = OverviewState.Entering;
            _transition = StartCoroutine(EnterOverviewRoutine());
        }

        private IEnumerator EnterOverviewRoutine()
        {
            // Put the local frame on the departure surface first, preserving the
            // exact rendered pose. Entry changes orientation only: no automatic
            // altitude or scale change is allowed here.
            _originRebaser.RebaseAtEcef(_returnSurfaceEcef);
            CapturePathAnchor();
            _overviewStartScale = Mathf.Max(0.0001f, _scaling.UserScale);
            _overviewMaximumScale = Mathf.Clamp(
                _settings.globeOverviewUserScale,
                Mathf.Max(_settings.minimumUserScale, _overviewStartScale),
                _settings.maximumUserScale);
            var startRotation = _pathEyeRotation;
            var startPosition = _pathEyePosition;
            var duration = Mathf.Clamp(_settings.globeOverviewTransitionSeconds * 0.4f, 0.5f, 1.5f);
            _overviewStartAltitude = Mathf.Max(1f, (float)_returnPlace.heightMeters);
            _overviewMaximumAltitude = CalculateOverviewAltitudeMeters(_returnSurfaceEcef);
            var startPathPosition = EcefToWorld(LongitudeLatitudeHeightToEcef(
                _returnPlace.longitude,
                _returnPlace.latitude,
                _overviewStartAltitude));
            _overviewStartPositionOffset = startPosition - startPathPosition;
            _overviewZoom01 = 0f;
            _overviewCenterLongitude = _returnPlace.longitude;
            _overviewCenterLatitude = _returnPlace.latitude;
            _overviewRollDegrees = 0f;
            _mapPointerDown = false;
            _mapDragging = false;
            _overviewRotationHand = null;
            _manualZoomWasInward = false;
            _hasManualZoomAnchor = false;
            var targetRotation = CalculateNadirRotation(
                startPosition,
                _returnPlace.longitude,
                _returnPlace.latitude);

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Smooth01(elapsed / duration);
                SetPathEyePose(
                    startPosition,
                    Quaternion.Slerp(startRotation, targetRotation, t));
                yield return null;
            }

            SetPathEyePose(startPosition, targetRotation);
            UpdateOverviewInstructions();
            _state = OverviewState.Choosing;
            _transition = null;
        }

        private void UpdateManualZoom()
        {
            var vertical = _input.Fly.y;
            if (Mathf.Abs(vertical) > 0.01f && _overviewMaximumScale > _overviewStartScale)
            {
                var zoomingIn = vertical > 0f;
                if (!zoomingIn)
                {
                    _hasManualZoomAnchor = false;
                }
                else if (_mapPointerDown)
                {
                    // A trigger grab takes ownership of the zoom anchor. Keep
                    // it after release so one continuous stick gesture cannot
                    // snap back to the point that was aimed at beforehand.
                    _manualZoomAnchorEcef = _mapGrabAnchorEcef;
                    _hasManualZoomAnchor = true;
                }
                else if (!_hasManualZoomAnchor && _hasAim)
                {
                    // Capture once per inward stick gesture. Resampling the
                    // controller ray every frame made tiny hand motion rotate
                    // Earth even though the user was only asking to zoom.
                    _manualZoomAnchorEcef = _aimEcef;
                    _hasManualZoomAnchor = true;
                }

                var hasZoomAnchor = zoomingIn && _hasManualZoomAnchor;
                var zoomAnchorEcef = _mapPointerDown
                    ? _mapGrabAnchorEcef
                    : _manualZoomAnchorEcef;

                // Once the user starts descending toward a remote hemisphere,
                // move Cesium's local frame to the current view center before
                // Unity coordinates grow into the millions. Rebasing directly
                // at an off-axis pointer changed the overview basis and caused
                // a visible jump/roll at the start of an inward zoom.
                if (hasZoomAnchor && !_manualZoomWasInward)
                    RebaseOverviewAtCenter();

                // Pulling the stick back zooms out; pushing it forward zooms in.
                // Express the rate in scale doublings so the whole range feels
                // uniform instead of racing at one end and crawling at the other.
                var totalDoublings = Mathf.Max(
                    0.001f,
                    Mathf.Log(_overviewMaximumScale / _overviewStartScale, 2f));
                _overviewZoom01 = Mathf.Clamp01(
                    _overviewZoom01 - vertical *
                    _settings.groundedScaleDoublingsPerSecond *
                    Time.unscaledDeltaTime / totalDoublings);
                ApplyOverviewZoom(_overviewZoom01);

                // Scaling changes both the ellipsoid radius and the camera's
                // orbital radius. Correct the orbit afterward so the selected
                // geographic point remains on the live controller ray. This is
                // an ECEF/vector solve, not longitude interpolation, so it has
                // no discontinuity at +/-180 degrees and also works while the
                // trigger is holding a trackball point.
                if (hasZoomAnchor)
                {
                    _mapGrabAnchorEcef = zoomAnchorEcef;
                    if (TryGetControllerTrackballPoint(out var currentSurfaceEcef))
                        ApplyTrackballDrag(currentSurfaceEcef);
                }

                _manualZoomWasInward = zoomingIn;
            }
            else
            {
                _manualZoomWasInward = false;
                _hasManualZoomAnchor = false;
            }

            UpdateOverviewInstructions();
        }

        private void RebaseOverviewAtCenter()
        {
            var centerSurfaceEcef = LongitudeLatitudeHeightToEcef(
                _overviewCenterLongitude,
                _overviewCenterLatitude,
                0d);
            _originRebaser.RebaseAtEcef(centerSurfaceEcef);
            CapturePathAnchor();

            // Recalculate every cached orientation value in the new local
            // frame. The rendered eye pose is unchanged by the rebase, so the
            // next zoom frame now continues smoothly from that exact pose.
            var cameraLlh = CesiumWgs84Ellipsoid
                .EarthCenteredEarthFixedToLongitudeLatitudeHeight(WorldToEcef(_pathEyePosition));
            _overviewCenterLongitude = WrapLongitude(cameraLlh.x);
            _overviewCenterLatitude = math.clamp(cameraLlh.y, -89.5d, 89.5d);
            _overviewStartPositionOffset = Vector3.zero;
            _overviewRollDegrees = CalculateOverviewRollDegrees(
                _pathEyePosition,
                _pathEyeRotation,
                _overviewCenterLongitude,
                _overviewCenterLatitude);
        }

        private void UpdateOverviewRotation()
        {
            // Trigger dragging already owns a precise surface anchor. Grips are
            // the free-rotation gesture used everywhere else in EarthVR, and in
            // overview they orbit the viewer around Earth's center so all three
            // axes remain useful instead of applying a flat-world yaw.
            var hand = !_mapPointerDown
                ? (_input.RightGripHeld
                    ? _rig.RightController
                    : _input.LeftGripHeld ? _rig.LeftController : null)
                : null;
            if (hand == null)
            {
                _overviewRotationHand = null;
                return;
            }

            if (_overviewRotationHand != hand)
            {
                _overviewRotationHand = hand;
                _previousOverviewHandLocalRotation = hand.localRotation;
                return;
            }

            var currentLocalRotation = hand.localRotation;
            var localDelta = currentLocalRotation * Quaternion.Inverse(_previousOverviewHandLocalRotation);
            localDelta.ToAngleAxis(out var angle, out _);
            if (angle > 180f)
                angle = 360f - angle;
            if (angle > 0.001f && angle <= 45f)
            {
                var trackingRotation = _rig.TrackingOrigin.rotation;
                var handDeltaWorld = trackingRotation * localDelta * Quaternion.Inverse(trackingRotation);
                ApplyCameraOrbit(Quaternion.Inverse(handDeltaWorld));
            }
            _previousOverviewHandLocalRotation = currentLocalRotation;
        }

        private void UpdateMapDrag(bool rightHeld, bool rightPressed, bool rightReleased)
        {
            if (rightPressed && _hasAim)
            {
                _mapPointerDown = true;
                _mapDragging = false;
                _mapDragStartDirection = _rig.Camera.transform
                    .InverseTransformDirection(_rig.RightController.forward)
                    .normalized;
                _mapGrabAnchorEcef = _aimEcef;
            }

            if (_mapPointerDown && rightHeld)
            {
                var currentDirection = _rig.Camera.transform
                    .InverseTransformDirection(_rig.RightController.forward)
                    .normalized;
                var angularTravel = Vector3.Angle(_mapDragStartDirection, currentDirection);
                if (angularTravel >= _settings.miniatureGlobeDragThresholdDegrees)
                    _mapDragging = true;

                if (_mapDragging)
                {
                    // Treat the visible Earth as a physical trackball. The
                    // initially touched geographic point is the grab anchor;
                    // orbiting the viewer by the inverse surface rotation puts
                    // that exact point under the live controller ray without
                    // ever translating the globe's center off to one side.
                    if (TryGetControllerTrackballPoint(out var currentSurfaceEcef))
                        ApplyTrackballDrag(currentSurfaceEcef);
                    _aimMarker.gameObject.SetActive(false);
                }
            }

            if (!rightReleased || !_mapPointerDown)
                return;

            var shouldTravel = !_mapDragging && _hasAim;
            _mapPointerDown = false;
            _mapDragging = false;
            if (!shouldTravel)
                return;

            var longitudeLatitudeHeight =
                CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(_aimEcef);
            var place = _arrival.CreateSearchDestination(new GeographicPlace(
                $"Globe {longitudeLatitudeHeight.y:F2}°, {longitudeLatitudeHeight.x:F2}°",
                longitudeLatitudeHeight.x,
                longitudeLatitudeHeight.y));
            BeginZoomAndTravel(_aimEcef, place, true);
        }

        private void ApplyTrackballDrag(double3 currentSurfaceEcef)
        {
            var center = EcefToWorld(double3.zero);
            var grabbedDirection = EcefToWorld(_mapGrabAnchorEcef) - center;
            var currentDirection = EcefToWorld(currentSurfaceEcef) - center;
            if (grabbedDirection.sqrMagnitude < 0.000001f ||
                currentDirection.sqrMagnitude < 0.000001f)
                return;

            // FromTo(current, grabbed) is the camera-orbit inverse of rotating
            // the grabbed surface point toward the controller's current hit.
            var cameraOrbit = CalculateTrackballCameraOrbit(
                grabbedDirection,
                currentDirection);
            ApplyCameraOrbit(cameraOrbit);
        }

        private void ApplyCameraOrbit(Quaternion cameraOrbit)
        {
            var center = EcefToWorld(double3.zero);
            var position = center + cameraOrbit * (_pathEyePosition - center);
            var rotation = cameraOrbit * _pathEyeRotation;
            SetPathEyePose(position, rotation);

            var cameraLlh = CesiumWgs84Ellipsoid
                .EarthCenteredEarthFixedToLongitudeLatitudeHeight(WorldToEcef(_pathEyePosition));
            _overviewCenterLongitude = WrapLongitude(cameraLlh.x);
            _overviewCenterLatitude = math.clamp(cameraLlh.y, -89.5d, 89.5d);
            _overviewStartPositionOffset = Vector3.zero;
            _overviewRollDegrees = CalculateOverviewRollDegrees(
                _pathEyePosition,
                _pathEyeRotation,
                _overviewCenterLongitude,
                _overviewCenterLatitude);
        }

        public static Quaternion CalculateTrackballCameraOrbit(
            Vector3 grabbedSurfaceDirection,
            Vector3 currentPointerSurfaceDirection) =>
            Quaternion.FromToRotation(
                currentPointerSurfaceDirection.normalized,
                grabbedSurfaceDirection.normalized);

        /// <summary>Returns a real ellipsoid hit while the ray crosses Earth and
        /// clamps to the corresponding virtual-trackball limb after it leaves
        /// the silhouette. A long arm sweep therefore reaches a natural stop
        /// instead of reversing direction, throwing Earth aside, or losing the
        /// grab abruptly.</summary>
        private bool TryGetControllerTrackballPoint(out double3 surfaceEcef)
        {
            var origin = WorldToEcef(_rig.RightController.position);
            var direction = WorldToEcef(
                _rig.RightController.position + _rig.RightController.forward) - origin;
            if (TryIntersectEllipsoid(origin, direction, out surfaceEcef))
                return true;

            var scaledOrigin = new double3(
                origin.x / EquatorialRadiusMeters,
                origin.y / EquatorialRadiusMeters,
                origin.z / PolarRadiusMeters);
            var scaledDirection = new double3(
                direction.x / EquatorialRadiusMeters,
                direction.y / EquatorialRadiusMeters,
                direction.z / PolarRadiusMeters);
            var cameraRadius = math.length(scaledOrigin);
            if (cameraRadius <= 1d || math.lengthsq(scaledDirection) < 1e-20d)
                return false;

            var cameraRadial = scaledOrigin / cameraRadius;
            var towardCenter = -cameraRadial;
            var lateral = scaledDirection -
                          towardCenter * math.dot(scaledDirection, towardCenter);
            if (math.lengthsq(lateral) < 1e-20d)
                return false;

            // On the normalized unit sphere, a tangent point T obeys
            // dot(T, camera) == 1. Its radial component is therefore 1/r,
            // with the remaining component pointing toward the ray's screen-
            // space side. It stays fixed at that limb as the ray moves farther
            // outside the silhouette, which is the stable arcball behavior.
            var radialComponent = 1d / cameraRadius;
            var lateralComponent = math.sqrt(math.max(
                0d,
                1d - radialComponent * radialComponent));
            var unit = cameraRadial * radialComponent +
                       math.normalize(lateral) * lateralComponent;
            surfaceEcef = new double3(
                unit.x * EquatorialRadiusMeters,
                unit.y * EquatorialRadiusMeters,
                unit.z * PolarRadiusMeters);
            return true;
        }

        private static double WrapLongitude(double longitude)
        {
            longitude %= 360d;
            if (longitude > 180d)
                longitude -= 360d;
            else if (longitude < -180d)
                longitude += 360d;
            return longitude;
        }

        private void ApplyOverviewZoom(float zoom01)
        {
            var eased = Smooth01(zoom01);
            var scale = LogLerp(_overviewStartScale, _overviewMaximumScale, eased);
            var altitude = LogLerp(_overviewStartAltitude, _overviewMaximumAltitude, eased);
            _scaling.SetUserScaleKeepingObserverFixed(scale);

            var pathPosition = EcefToWorld(LongitudeLatitudeHeightToEcef(
                _overviewCenterLongitude,
                _overviewCenterLatitude,
                altitude));
            var position = pathPosition + _overviewStartPositionOffset * (1f - eased);
            var northUpRotation = CalculateNadirRotation(
                position,
                _overviewCenterLongitude,
                _overviewCenterLatitude);
            var rotation = Quaternion.AngleAxis(
                _overviewRollDegrees,
                northUpRotation * Vector3.forward) * northUpRotation;
            SetPathEyePose(
                position,
                rotation);
        }

        private float CalculateOverviewRollDegrees(
            Vector3 position,
            Quaternion rotation,
            double longitude,
            double latitude)
        {
            var northUp = CalculateNadirRotation(position, longitude, latitude);
            var forward = northUp * Vector3.forward;
            var northUpOnScreen = Vector3.ProjectOnPlane(northUp * Vector3.up, forward);
            var actualUpOnScreen = Vector3.ProjectOnPlane(rotation * Vector3.up, forward);
            if (northUpOnScreen.sqrMagnitude < 0.000001f ||
                actualUpOnScreen.sqrMagnitude < 0.000001f)
                return 0f;
            return Vector3.SignedAngle(
                northUpOnScreen.normalized,
                actualUpOnScreen.normalized,
                forward);
        }

        private void UpdateOverviewInstructions()
        {
            var altitude = LogLerp(
                _overviewStartAltitude,
                _overviewMaximumAltitude,
                Smooth01(_overviewZoom01));
            _instructions.text =
                $"TILTED EARTH  •  {altitude / 1000f:N1} KM\n" +
                "RIGHT STICK FORWARD: IN  •  BACK: OUT\n" +
                "TRIGGER: CLICK TRAVEL / DRAG EARTH  •  GRIP: ROTATE  •  D-PAD UP: RETURN";
        }

        private void BeginZoomAndTravel(double3 selectedSurfaceEcef, SavedPlace destination, bool recordRecent)
        {
            if (_transition != null)
                StopCoroutine(_transition);
            _state = OverviewState.Zooming;
            _hasAim = false;
            _aimMarker.gameObject.SetActive(false);
            _instructions.text = $"CENTERING\n{destination.name}";
            _transition = StartCoroutine(ZoomAndTravelRoutine(selectedSurfaceEcef, destination, recordRecent));
        }

        private IEnumerator ZoomAndTravelRoutine(
            double3 selectedSurfaceEcef,
            SavedPlace destination,
            bool recordRecent)
        {
            var longitudeLatitudeHeight =
                CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(selectedSurfaceEcef);
            var longitude = longitudeLatitudeHeight.x;
            var latitude = longitudeLatitudeHeight.y;

            // Orbit at exactly the zoom level chosen by the user. Selecting a
            // destination must never force an additional automatic zoom-out.
            var center = EcefToWorld(double3.zero);
            var startPosition = _pathEyePosition;
            var startRotation = _pathEyeRotation;
            var startDirection = (startPosition - center).normalized;
            var startDistance = Vector3.Distance(startPosition, center);
            var destinationSurfacePosition = EcefToWorld(selectedSurfaceEcef);
            var destinationDirection = (destinationSurfacePosition - center).normalized;
            var destinationOverviewPosition = center + destinationDirection * startDistance;
            var destinationRotation = CalculateNadirRotation(
                destinationOverviewPosition,
                longitude,
                latitude);
            var orbitDuration = Mathf.Max(0.1f, _settings.globeOverviewDestinationOrbitSeconds);
            var elapsed = 0f;
            while (elapsed < orbitDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Smooth01(elapsed / orbitDuration);
                center = EcefToWorld(double3.zero);
                var direction = GreatCircleDirection(startDirection, destinationDirection, t);
                var position = center + direction * startDistance;
                SetPathEyePose(position, Quaternion.Slerp(startRotation, destinationRotation, t));
                yield return null;
            }

            center = EcefToWorld(double3.zero);
            var centeredPosition = center + destinationDirection * startDistance;
            var centeredRotation = CalculateNadirRotation(centeredPosition, longitude, latitude);
            SetPathEyePose(centeredPosition, centeredRotation);

            // Re-express the identical rendered pose in a frame centered on the
            // destination. The subsequent city-scale coordinates stay small and
            // precise all the way down.
            _originRebaser.RebaseAtEcef(selectedSurfaceEcef);
            CapturePathAnchor();

            var startScale = Mathf.Max(0.0001f, _scaling.UserScale);
            var targetScale = Mathf.Clamp(
                destination.userScale,
                _settings.minimumUserScale,
                _settings.maximumUserScale);
            var startLlh = CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(
                WorldToEcef(_pathEyePosition));
            var startAltitude = Mathf.Max(1f, (float)startLlh.z);
            var targetAltitude = Mathf.Max(1f, (float)destination.heightMeters);
            var descentStartPosition = _pathEyePosition;
            var descentPathStart = EcefToWorld(LongitudeLatitudeHeightToEcef(
                longitude,
                latitude,
                startAltitude));
            var descentOffset = descentStartPosition - descentPathStart;
            var zoomDuration = Mathf.Max(0.5f, _settings.globeOverviewZoomInSeconds);
            var progress = 0f;
            while (progress < 1f)
            {
                var speed = CalculateStreamingSpeedMultiplier(progress);
                progress = Mathf.Min(1f, progress + Time.unscaledDeltaTime * speed / zoomDuration);
                var t = Smooth01(progress);
                var scale = LogLerp(startScale, targetScale, t);
                var altitude = LogLerp(startAltitude, targetAltitude, t);
                _scaling.SetUserScaleKeepingObserverFixed(scale);

                var pathPosition = EcefToWorld(LongitudeLatitudeHeightToEcef(
                    longitude,
                    latitude,
                    altitude));
                var position = pathPosition + descentOffset * (1f - t);
                var nadirRotation = CalculateNadirRotation(position, longitude, latitude);
                var headingRotation = CalculateHeadingRotation(
                    longitude,
                    latitude,
                    destination.headingDegrees);
                var tilt = Smooth01(Mathf.InverseLerp(0.58f, 1f, t));
                SetPathEyePose(
                    position,
                    Quaternion.Slerp(nadirRotation, headingRotation, tilt));

                var load = CurrentLoadProgress();
                _instructions.text = $"ZOOMING TO {destination.name}\n{altitude / 1000f:N1} KM  •  TILES {load:N0}%";
                yield return null;
            }

            _scaling.SetUserScaleKeepingObserverFixed(targetScale);
            var finalPosition = EcefToWorld(LongitudeLatitudeHeightToEcef(
                destination.longitude,
                destination.latitude,
                targetAltitude));
            SetPathEyePose(
                finalPosition,
                CalculateHeadingRotation(
                    destination.longitude,
                    destination.latitude,
                    destination.headingDegrees));

            _grounding.RestoreModeForArrival(destination.movementMode);
            _navigation.CompleteSeamlessArrival(destination.name);
            _arrival.CompleteSeamlessArrival(destination, recordRecent);
            _globeLabels.SetVisible(false);
            _miniatureGlobe.SetVisible(true);
            _instructions.gameObject.SetActive(false);
            _navigation.NavigationEnabled = true;
            _scaling.InteractionsEnabled = true;
            _sunAndSky.ClearOverviewSunDirection();
            _state = OverviewState.Inactive;
            _transition = null;
        }

        private void UpdateAim()
        {
            _hasAim = false;
            _aimMarker.gameObject.SetActive(false);

            var ray = new Ray(_rig.RightController.position, _rig.RightController.forward);
            if (Physics.Raycast(
                    ray,
                    out var blockingHit,
                    10f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Collide) &&
                (blockingHit.collider.GetComponent<WorldSpaceButton>() != null ||
                 blockingHit.collider.GetComponent<CelestialDragHandle>() != null ||
                 blockingHit.collider.GetComponent<MiniatureGlobeSurface>() != null))
                return;

            var originEcef = WorldToEcef(ray.origin);
            var forwardEcef = WorldToEcef(ray.origin + ray.direction) - originEcef;
            if (!TryIntersectEllipsoid(originEcef, forwardEcef, out _aimEcef))
                return;

            _hasAim = true;
            _aimMarker.position = EcefToWorld(_aimEcef);
            _aimMarker.gameObject.SetActive(true);
        }

        private float CalculateOverviewAltitudeMeters(double3 surfaceEcef)
        {
            var surfaceRadius = math.length(surfaceEcef);
            var halfAngle = Mathf.Clamp(
                _settings.globeOverviewDiameterDegrees * 0.5f,
                10f,
                40f) * Mathf.Deg2Rad;
            return (float)(surfaceRadius / Mathf.Sin(halfAngle) - surfaceRadius);
        }

        private static double3 LongitudeLatitudeHeightToEcef(
            double longitude,
            double latitude,
            double height) =>
            CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(longitude, latitude, height));

        private Quaternion CalculateNadirRotation(Vector3 cameraPosition, double longitude, double latitude)
        {
            var surface = EcefToWorld(LongitudeLatitudeHeightToEcef(longitude, latitude, 0d));
            var forward = (surface - cameraPosition).normalized;
            return Quaternion.LookRotation(forward, SurfaceNorthWorld(longitude, latitude));
        }

        private Quaternion CalculateHeadingRotation(double longitude, double latitude, float headingDegrees)
        {
            var surface = EcefToWorld(LongitudeLatitudeHeightToEcef(longitude, latitude, 0d));
            var aboveSurface = EcefToWorld(LongitudeLatitudeHeightToEcef(
                longitude,
                latitude,
                1000d));
            var up = (aboveSurface - surface).normalized;
            var north = Vector3.ProjectOnPlane(SurfaceNorthWorld(longitude, latitude), up).normalized;
            if (north.sqrMagnitude < 0.000001f)
                north = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
            var east = Vector3.Cross(north, up).normalized;
            var radians = headingDegrees * Mathf.Deg2Rad;
            var forward = (north * Mathf.Cos(radians) + east * Mathf.Sin(radians)).normalized;
            return Quaternion.LookRotation(forward, up);
        }

        public static Vector3 GreatCircleDirection(Vector3 from, Vector3 to, float t)
        {
            from.Normalize();
            to.Normalize();
            t = Mathf.Clamp01(t);
            var dot = Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f);
            if (dot > 0.9995f)
                return Vector3.Lerp(from, to, t).normalized;
            if (dot < -0.9995f)
            {
                var reference = Mathf.Abs(from.y) < 0.9f ? Vector3.up : Vector3.right;
                var axis = Vector3.Cross(from, reference).normalized;
                return Quaternion.AngleAxis(180f * t, axis) * from;
            }

            var angle = Mathf.Acos(dot);
            var sinAngle = Mathf.Sin(angle);
            return (from * (Mathf.Sin((1f - t) * angle) / sinAngle) +
                    to * (Mathf.Sin(t * angle) / sinAngle)).normalized;
        }

        private float CalculateStreamingSpeedMultiplier(float progress)
        {
            if (progress < 0.15f || _tileset == null || _tileset.suspendUpdate)
                return 1f;
            var threshold = Mathf.Clamp(_settings.globeOverviewMinimumLoadPercentage, 1f, 100f);
            var loadBlend = Mathf.InverseLerp(
                Mathf.Max(0f, threshold - 35f),
                threshold,
                CurrentLoadProgress());
            return Mathf.Lerp(
                _settings.globeOverviewMinimumZoomSpeed,
                1f,
                Smooth01(loadBlend));
        }

        private float CurrentLoadProgress() =>
            _tileset == null || _tileset.suspendUpdate ? 100f : _tileset.ComputeLoadProgress();

        private Vector3 SurfaceNorthWorld(double longitude, double latitude)
        {
            var here = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(longitude, latitude, 0d));
            var step = latitude > 89.98d ? -0.01d : 0.01d;
            var north = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(longitude, math.clamp(latitude + step, -89.999d, 89.999d), 0d));
            var direction = EcefToWorld(north) - EcefToWorld(here);
            if (step < 0d)
                direction = -direction;
            return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.up;
        }

        private void CapturePathAnchor()
        {
            var root = _rig.NavigationSpace;
            _pathHeadLocalPosition = root.InverseTransformPoint(_rig.Camera.transform.position);
            _pathHeadLocalRotation = Quaternion.Inverse(root.rotation) * _rig.Camera.transform.rotation;
            _pathEyePosition = _rig.Camera.transform.position;
            _pathEyeRotation = _rig.Camera.transform.rotation;
            _hasPathAnchor = true;
        }

        /// <summary>Moves the reference eye along the geographic path while the
        /// current tracked head pose remains a live local offset. The user can
        /// keep looking and leaning naturally throughout the multi-second zoom.</summary>
        private void SetPathEyePose(Vector3 position, Quaternion rotation)
        {
            if (!_hasPathAnchor)
                CapturePathAnchor();
            var rootRotation = rotation * Quaternion.Inverse(_pathHeadLocalRotation);
            var rootPosition = position - rootRotation * _pathHeadLocalPosition;
            _rig.NavigationSpace.SetPositionAndRotation(rootPosition, rootRotation);
            _pathEyePosition = position;
            _pathEyeRotation = rotation;
            if (_state != OverviewState.Inactive)
                UpdateOverviewLighting();
            Physics.SyncTransforms();
        }

        private void UpdateOverviewLighting()
        {
            if (_sunAndSky == null || _rig == null)
                return;
            var center = EcefToWorld(double3.zero);
            var towardViewer = _rig.Camera.transform.position - center;
            if (towardViewer.sqrMagnitude > 0.000001f)
                _sunAndSky.SetOverviewSunDirection(towardViewer.normalized);
        }

        private double3 WorldToEcef(Vector3 world)
        {
            var local = _georeference.transform.InverseTransformPoint(world);
            return _georeference.TransformUnityPositionToEarthCenteredEarthFixed(
                new double3(local.x, local.y, local.z));
        }

        private Vector3 EcefToWorld(double3 ecef)
        {
            var local = _georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            return _georeference.transform.TransformPoint((Vector3)(float3)local);
        }

        private Vector3 EcefDirectionToWorld(double3 ecefDirection)
        {
            var local = _georeference.TransformEarthCenteredEarthFixedDirectionToUnity(ecefDirection);
            return _georeference.transform.TransformDirection((Vector3)(float3)local);
        }

        public static bool TryIntersectEllipsoid(double3 origin, double3 direction, out double3 hit)
        {
            hit = default;
            var length = math.length(direction);
            if (length <= 1e-9d)
                return false;
            direction /= length;

            var inverseA2 = 1d / (EquatorialRadiusMeters * EquatorialRadiusMeters);
            var inverseB2 = 1d / (PolarRadiusMeters * PolarRadiusMeters);
            var a = (direction.x * direction.x + direction.y * direction.y) * inverseA2 +
                    direction.z * direction.z * inverseB2;
            var b = 2d * ((origin.x * direction.x + origin.y * direction.y) * inverseA2 +
                          origin.z * direction.z * inverseB2);
            var c = (origin.x * origin.x + origin.y * origin.y) * inverseA2 +
                    origin.z * origin.z * inverseB2 - 1d;
            var discriminant = b * b - 4d * a * c;
            if (discriminant < 0d)
                return false;

            var root = math.sqrt(discriminant);
            var near = (-b - root) / (2d * a);
            var far = (-b + root) / (2d * a);
            var distance = near > 0d ? near : far;
            if (distance <= 0d)
                return false;
            hit = origin + direction * distance;
            return true;
        }

        private void CreateFeedback()
        {
            var markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            markerObject.name = "Planetary Overview Aim";
            markerObject.transform.localScale = Vector3.one * 0.045f;
            Destroy(markerObject.GetComponent<Collider>());
            var renderer = markerObject.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
                renderer.sharedMaterial = new Material(shader)
                {
                    name = "Planetary Overview Aim Material",
                    color = new Color(1f, 0.72f, 0.12f, 0.92f),
                    hideFlags = HideFlags.DontSave
                };
            _aimMarker = markerObject.transform;
            markerObject.SetActive(false);

            var instructionObject = new GameObject("Planetary Overview Instructions", typeof(TextMesh));
            instructionObject.transform.SetParent(_rig.Camera.transform, false);
            instructionObject.transform.localPosition = new Vector3(0f, -0.34f, 0.85f);
            instructionObject.transform.localRotation = Quaternion.identity;
            _instructions = instructionObject.GetComponent<TextMesh>();
            _instructions.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _instructions.fontSize = 48;
            _instructions.characterSize = 0.0025f;
            _instructions.anchor = TextAnchor.MiddleCenter;
            _instructions.alignment = TextAlignment.Center;
            _instructions.color = new Color(0.88f, 0.96f, 1f, 0.95f);
            instructionObject.SetActive(false);
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static float LogLerp(float from, float to, float t) =>
            Mathf.Exp(Mathf.Lerp(Mathf.Log(from), Mathf.Log(to), Mathf.Clamp01(t)));

        private void OnDisable()
        {
            if (_state == OverviewState.Inactive)
                return;
            if (_transition != null)
                StopCoroutine(_transition);
            _miniatureGlobe?.SetVisible(true);
            _globeLabels?.SetVisible(false);
            if (_instructions != null)
                _instructions.gameObject.SetActive(false);
            if (_aimMarker != null)
                _aimMarker.gameObject.SetActive(false);
            _sunAndSky?.ClearOverviewSunDirection();
            if (_navigation != null)
                _navigation.NavigationEnabled = true;
            if (_scaling != null)
                _scaling.InteractionsEnabled = true;
            _state = OverviewState.Inactive;
        }
    }
}
