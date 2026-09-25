using CesiumForUnity;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Sky;
using EarthVR.Input;
using EarthVR.Navigation;
using EarthVR.UI;
using Unity.Mathematics;
using UnityEngine;

namespace EarthVR.Scaling
{
    /// <summary>
    /// Treats interaction as movement of the observer around an authoritative ECEF globe.
    /// The Cesium hierarchy remains identity-transformed. User scale is expressed through
    /// CesiumGeoreference.scale, which preserves tile-coordinate precision better than Transform scale.
    /// </summary>
    public sealed class WorldManipulationController : MonoBehaviour
    {
        private const float EarthMeanRadiusMeters = 6371000f;

        /// <summary>Per-hand cone-drag bookkeeping. One grabbed ECEF point never
        /// changes for as long as that hand's trigger stays held.</summary>
        private sealed class HandDragState
        {
            public bool HasAnchor;
            public double3 AnchorEcef;
            public Vector3 SmoothedOriginLocal;
            public Vector3 SmoothedDirectionLocal;
            public Vector3 GrabStartOriginLocal;
            public float GrabDistanceUnity;
            public float GrabTranslationGain;

            public void Clear() => HasAnchor = false;
        }

        private IEarthVRInput _input;
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private CesiumGeoreference _georeference;
        private NavigationController _navigation;
        private SunSkyController _sunAndSky;
        private Transform _left;
        private Transform _right;

        private readonly HandDragState _leftDrag = new();
        private readonly HandDragState _rightDrag = new();
        private bool _twoHandActive;

        private Transform _rotationHand;
        private Quaternion _previousHandLocalRotation;

        private Vector3 _momentumVelocity;

        public float UserScale { get; private set; } = 1f;
        public bool IsDraggingEarth => _leftDrag.HasAnchor || _rightDrag.HasAnchor;
        public event System.Action<float> ScaleChanged;

        public void Initialize(
            IEarthVRInput input,
            EarthVRSettings settings,
            EarthVRRig rig,
            CesiumGeoreference georeference,
            NavigationController navigation,
            SunSkyController sunAndSky)
        {
            _input = input;
            _settings = settings;
            _rig = rig;
            _georeference = georeference;
            _navigation = navigation;
            _sunAndSky = sunAndSky;
            _left = rig.LeftController;
            _right = rig.RightController;
            SetUserScale(1f, CameraPivotEcef());
        }

        private void Update()
        {
            if (_input == null)
                return;
            if (_input.BoostHeld)
            {
                // Shoulder boost is reserved for Flight mode. Never allow an
                // overlapping runtime binding to begin or continue world dragging,
                // rotation, or grounded scale manipulation.
                _leftDrag.Clear();
                _rightDrag.Clear();
                _twoHandActive = false;
                _rotationHand = null;
                return;
            }

            var leftDragging = _input.LeftTriggerHeld && !IsHandBlocked(_left);
            var rightDragging = _input.RightTriggerHeld && !IsHandBlocked(_right);

            if (!leftDragging)
                _leftDrag.Clear();
            if (!rightDragging)
                _rightDrag.Clear();
            if (leftDragging || rightDragging)
                _momentumVelocity = Vector3.zero;

            if (leftDragging && rightDragging)
            {
                UpdateTwoHandDrag();
            }
            else
            {
                _twoHandActive = false;
                if (rightDragging)
                    UpdateSingleHandDrag(_right, _rightDrag);
                else if (leftDragging)
                    UpdateSingleHandDrag(_left, _leftDrag);
                else
                    ApplyMomentumDecay();
            }

            var rotationHand = !leftDragging && !rightDragging
                ? ActiveHand(_input.LeftGripHeld, _input.RightGripHeld)
                : null;
            if (rotationHand != null)
                UpdateRotation(rotationHand);
            else
                _rotationHand = null;

            if (!leftDragging && !rightDragging && rotationHand == null)
                UpdateGroundedScale();
        }

        private bool IsHandBlocked(Transform hand) =>
            (_sunAndSky != null && _sunAndSky.IsConsumingTrigger(hand)) ||
            IsPointingAtInterfaceOrCelestialHandle(hand);

        private Transform ActiveHand(bool leftHeld, bool rightHeld)
        {
            if (rightHeld)
                return _right;
            return leftHeld ? _left : null;
        }

        private void UpdateSingleHandDrag(Transform hand, HandDragState state)
        {
            if (!state.HasAnchor)
            {
                TryBeginDrag(hand, state);
                return;
            }

            var rayOriginLocal = _rig.NavigationSpace.InverseTransformPoint(hand.position);
            var rayDirectionLocal = _rig.NavigationSpace.InverseTransformDirection(hand.forward).normalized;
            var smoothingSeconds = Mathf.Max(0.01f, _settings.coneDragSmoothingSeconds);

            // Ignore sub-millimetre pose chatter before distance leverage can
            // magnify it into visible globe movement.
            var originError = rayOriginLocal - state.SmoothedOriginLocal;
            var originErrorMagnitude = originError.magnitude;
            var filteredOriginTarget = originErrorMagnitude <= 0.003f
                ? state.SmoothedOriginLocal
                : state.SmoothedOriginLocal +
                  originError * ((originErrorMagnitude - 0.003f) / originErrorMagnitude);

            var angularError = Vector3.Angle(state.SmoothedDirectionLocal, rayDirectionLocal);
            var filteredDirectionTarget = angularError <= 0.16f
                ? state.SmoothedDirectionLocal
                : Vector3.Slerp(
                    state.SmoothedDirectionLocal,
                    rayDirectionLocal,
                    (angularError - 0.16f) / angularError).normalized;

            var deliberateMotion = Mathf.Max(
                Mathf.InverseLerp(0.004f, 0.045f, originErrorMagnitude),
                Mathf.InverseLerp(
                0.16f,
                Mathf.Max(0.06f, _settings.coneDragFastResponseAngleDegrees),
                angularError));
            var responseSeconds = Mathf.Lerp(
                smoothingSeconds,
                Mathf.Max(0.001f, _settings.coneDragFastResponseSeconds),
                deliberateMotion);
            var filter = 1f - Mathf.Exp(-Time.deltaTime / responseSeconds);
            state.SmoothedOriginLocal = Vector3.Lerp(state.SmoothedOriginLocal, filteredOriginTarget, filter);
            state.SmoothedDirectionLocal = Vector3.Slerp(
                state.SmoothedDirectionLocal,
                filteredDirectionTarget,
                filter).normalized;

            // The initially selected ECEF point never changes. Pushing or pulling
            // the controller changes its distance along the current pointer ray,
            // so the same terrain point remains the thing attached to the pointer.
            var handDepthDelta = Vector3.Dot(
                state.SmoothedOriginLocal - state.GrabStartOriginLocal,
                state.SmoothedDirectionLocal);
            var distanceAlongRay = Mathf.Clamp(
                state.GrabDistanceUnity + handDepthDelta * state.GrabTranslationGain,
                0.05f,
                Mathf.Max(state.GrabDistanceUnity * 4f, 1f));
            var desiredGrabLocal = state.SmoothedOriginLocal +
                                   state.SmoothedDirectionLocal * distanceAlongRay;
            var currentGrabWorld = EcefToWorld(state.AnchorEcef);
            var targetNavigationPosition = currentGrabWorld -
                                           _rig.NavigationSpace.rotation * desiredGrabLocal;

            // Treat this as pulling the Earth beneath the user, not pulling the
            // user up the controller ray. The selected ECEF feature remains the
            // horizontal anchor while player height is left untouched.
            targetNavigationPosition.y = _rig.NavigationSpace.position.y;

            // Solve exactly rather than easing the world afterward. A second world
            // filter visibly allowed the selected terrain feature to slip away.
            var previousPosition = _rig.NavigationSpace.position;
            _rig.NavigationSpace.position = targetNavigationPosition;
            TrackMomentum(previousPosition);
        }

        private void UpdateTwoHandDrag()
        {
            if (!_leftDrag.HasAnchor)
                TryBeginDrag(_left, _leftDrag);
            if (!_rightDrag.HasAnchor)
                TryBeginDrag(_right, _rightDrag);
            if (!_leftDrag.HasAnchor || !_rightDrag.HasAnchor)
            {
                _twoHandActive = false;
                return;
            }

            _twoHandActive = true;
            var previousPosition = _rig.NavigationSpace.position;

            var handSeparation = Vector3.ProjectOnPlane(_right.position - _left.position, Vector3.up);
            var handDistance = handSeparation.magnitude;

            var leftMapped = EcefToWorld(_leftDrag.AnchorEcef);
            var rightMapped = EcefToWorld(_rightDrag.AnchorEcef);
            var mappedSeparation = Vector3.ProjectOnPlane(rightMapped - leftMapped, Vector3.up);
            var mappedDistance = mappedSeparation.magnitude;

            // Uniform scale: make the two grabbed points' current separation match
            // the controllers' actual separation, pivoting on the anchor midpoint
            // so both points stay glued as close as possible while resizing.
            if (handDistance > 0.001f && mappedDistance > 0.001f)
            {
                var requestedScale = UserScale * mappedDistance / handDistance;
                var pivotEcef = MidpointEcef(_leftDrag.AnchorEcef, _rightDrag.AnchorEcef);
                SetUserScale(requestedScale, pivotEcef);
                leftMapped = EcefToWorld(_leftDrag.AnchorEcef);
                rightMapped = EcefToWorld(_rightDrag.AnchorEcef);
                mappedSeparation = Vector3.ProjectOnPlane(rightMapped - leftMapped, Vector3.up);
            }

            // Yaw: twist the observer, exactly, so the grabbed points' bearing
            // matches the controllers' current bearing every frame. Rotating the
            // observer around its own camera (rather than the Earth, which never
            // moves) is the same trick the single-hand grip rotation already uses.
            var mappedBearing = Mathf.Atan2(mappedSeparation.x, mappedSeparation.z) * Mathf.Rad2Deg;
            var handBearing = Mathf.Atan2(handSeparation.x, handSeparation.z) * Mathf.Rad2Deg;
            var yawDelta = Mathf.DeltaAngle(handBearing, mappedBearing);
            if (Mathf.Abs(yawDelta) > 0.001f)
                _rig.NavigationSpace.RotateAround(_rig.Camera.transform.position, Vector3.up, yawDelta);

            // Translation is solved last and exactly, same philosophy as the
            // one-hand drag: the midpoint of both grabbed points lands under the
            // midpoint of both hands with no residual drift.
            var midpointMapped = (leftMapped + rightMapped) * 0.5f;
            var midpointHands = (_left.position + _right.position) * 0.5f;
            var translation = midpointHands - midpointMapped;
            translation.y = 0f;
            _rig.NavigationSpace.position += translation;

            TrackMomentum(previousPosition);
        }

        private void TryBeginDrag(Transform hand, HandDragState state)
        {
            var maximumDistance = ScaleMath.GeographicToUnityMeters(
                _settings.maximumGrabDistanceMeters,
                UserScale);

            Vector3 grabPoint;
            if (Physics.Raycast(
                    hand.position,
                    hand.forward,
                    out var hit,
                    maximumDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponent<WorldSpaceButton>() == null &&
                hit.collider.GetComponent<CelestialDragHandle>() == null)
            {
                grabPoint = hit.point;
            }
            else if (TryIntersectPlanet(hand.position, hand.forward, out var ellipsoidPoint))
            {
                // No physics surface under the ray (open ocean, a tile-loading gap,
                // or aiming past the horizon into space). Fall back to where the
                // ray crosses the planet so a grab practically never fails.
                grabPoint = ellipsoidPoint;
            }
            else
            {
                return;
            }

            state.AnchorEcef = WorldToEcef(grabPoint);
            var originLocal = _rig.NavigationSpace.InverseTransformPoint(hand.position);
            var hitLocal = _rig.NavigationSpace.InverseTransformPoint(grabPoint);
            state.GrabDistanceUnity = Mathf.Max(0.05f, Vector3.Distance(originLocal, hitLocal));
            state.SmoothedOriginLocal = originLocal;
            state.SmoothedDirectionLocal = (hitLocal - originLocal).normalized;
            state.GrabStartOriginLocal = originLocal;

            // Soft saturation instead of a hard clamp: gain tracks grab distance
            // closely at first, then eases toward the configured maximum instead
            // of abruptly running out once a hold reaches it.
            var rawGain = state.GrabDistanceUnity / 0.8f;
            var maxGain = Mathf.Max(1f, _settings.maximumGrabTranslationGain);
            state.GrabTranslationGain = maxGain * (float)System.Math.Tanh(rawGain / maxGain);
            state.HasAnchor = true;
        }

        private bool TryIntersectPlanet(Vector3 origin, Vector3 direction, out Vector3 point)
        {
            point = Vector3.zero;
            var centerWorld = EcefToWorld(double3.zero);
            var radius = ScaleMath.GeographicToUnityMeters(EarthMeanRadiusMeters, UserScale);
            if (radius <= 0f)
                return false;

            var normalizedDirection = direction.normalized;
            var toCenter = origin - centerWorld;
            var b = Vector3.Dot(toCenter, normalizedDirection);
            var c = toCenter.sqrMagnitude - radius * radius;
            var discriminant = b * b - c;
            if (discriminant < 0f)
                return false;

            var sqrtDiscriminant = Mathf.Sqrt(discriminant);
            var t = -b - sqrtDiscriminant;
            if (t < 0f)
                t = -b + sqrtDiscriminant;
            if (t < 0f)
                return false;

            point = origin + normalizedDirection * t;
            return true;
        }

        private void TrackMomentum(Vector3 previousPosition)
        {
            var delta = _rig.NavigationSpace.position - previousPosition;
            var instantaneous = delta / Mathf.Max(Time.deltaTime, 0.0001f);
            _momentumVelocity = Vector3.Lerp(_momentumVelocity, instantaneous, 0.5f);
        }

        private void ApplyMomentumDecay()
        {
            if (!_settings.dragMomentumEnabled || _momentumVelocity.sqrMagnitude < 0.0001f)
            {
                _momentumVelocity = Vector3.zero;
                return;
            }

            var horizontalVelocity = Vector3.ProjectOnPlane(_momentumVelocity, Vector3.up);
            if (horizontalVelocity.magnitude < _settings.dragMomentumMinimumSpeedMetersPerSecond)
            {
                _momentumVelocity = Vector3.zero;
                return;
            }

            _rig.NavigationSpace.position += horizontalVelocity * Time.deltaTime;
            var decay = Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, _settings.dragMomentumDecaySeconds));
            _momentumVelocity = horizontalVelocity * decay;
        }

        private void UpdateRotation(Transform hand)
        {
            if (_rotationHand != hand)
            {
                _rotationHand = hand;
                _previousHandLocalRotation = hand.localRotation;
                return;
            }

            var currentLocalRotation = hand.localRotation;
            var previousDirection = HorizontalControllerDirection(_previousHandLocalRotation);
            var currentDirection = HorizontalControllerDirection(currentLocalRotation);
            if (previousDirection.sqrMagnitude > 0.001f && currentDirection.sqrMagnitude > 0.001f)
            {
                var yaw = Vector3.SignedAngle(previousDirection, currentDirection, Vector3.up);
                if (Mathf.Abs(yaw) <= 45f)
                    _rig.NavigationSpace.RotateAround(_rig.Camera.transform.position, Vector3.up, -yaw);
            }
            _previousHandLocalRotation = currentLocalRotation;
        }

        private Vector3 HorizontalControllerDirection(Quaternion localRotation)
        {
            var direction = _rig.TrackingOrigin.TransformDirection(localRotation * Vector3.forward);
            var horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontal.sqrMagnitude > 0.001f)
                return horizontal.normalized;

            direction = _rig.TrackingOrigin.TransformDirection(localRotation * Vector3.right);
            return Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        }

        private void UpdateGroundedScale()
        {
            if (_navigation.State.Mode != MovementMode.Grounded)
                return;

            var stick = _input.Fly;
            var intendedDirection = _right.forward * stick.y + _right.right * stick.x;
            var verticalIntent = Mathf.Clamp(intendedDirection.y, -1f, 1f);
            if (Mathf.Abs(verticalIntent) < 0.1f)
                return;

            var scaleRate = _settings.groundedScaleDoublingsPerSecond;
            var multiplier = Mathf.Pow(
                2f,
                verticalIntent * scaleRate * Time.deltaTime);
            SetUserScale(UserScale * multiplier);
        }

        private bool IsPointingAtInterfaceOrCelestialHandle(Transform hand) =>
            Physics.Raycast(
                hand.position,
                hand.forward,
                out var hit,
                _settings.pointerMaximumLengthMeters,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) &&
            (hit.collider.GetComponent<WorldSpaceButton>() != null ||
             hit.collider.GetComponent<CelestialDragHandle>() != null);

        public void SetUserScale(float requestedScale) => SetUserScale(requestedScale, CameraPivotEcef());

        public void SetUserScale(float requestedScale, double3 pivotEcef)
        {
            var clamped = ScaleMath.Clamp(requestedScale, _settings.minimumUserScale, _settings.maximumUserScale);
            if (Mathf.Approximately(clamped, UserScale) && Mathf.Approximately((float)_georeference.scale, (float)ScaleMath.CesiumGlobeScale(clamped)))
                return;

            var before = EcefToWorld(pivotEcef);
            _georeference.scale = ScaleMath.CesiumGlobeScale(clamped);
            var after = EcefToWorld(pivotEcef);
            _rig.NavigationSpace.position += after - before;
            UserScale = clamped;
            _navigation.UserScale = clamped;
            ScaleChanged?.Invoke(UserScale);
        }

        private static double3 MidpointEcef(double3 a, double3 b) => (a + b) * 0.5;

        private double3 CameraPivotEcef()
        {
            return WorldToEcef(_rig.TrackingOrigin.position);
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
            return _georeference.transform.TransformPoint(new Vector3((float)local.x, (float)local.y, (float)local.z));
        }
    }
}
