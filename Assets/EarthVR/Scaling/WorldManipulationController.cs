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
        private IEarthVRInput _input;
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private CesiumGeoreference _georeference;
        private NavigationController _navigation;
        private SunSkyController _sunAndSky;
        private Transform _left;
        private Transform _right;

        // Only the right hand grabs the world. The grabbed ECEF point never
        // changes for as long as the trigger stays held. Hand motion since the
        // grab began is scaled by a fixed gain (set once, from how far away the
        // grabbed point was) rather than re-projected along a ray each frame,
        // so the feel stays simple and predictable: close things move almost
        // 1:1 with the hand, distant things move proportionally more.
        private bool _hasGrabAnchor;
        private double3 _grabAnchorEcef;
        private Vector3 _grabStartControllerLocal;
        private Vector3 _grabStartOffsetLocal;
        private float _grabGain;

        private Transform _rotationHand;
        private Quaternion _previousHandLocalRotation;

        private Vector3 _momentumVelocity;

        public float UserScale { get; private set; } = 1f;
        public bool IsDraggingEarth => _hasGrabAnchor;
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
                _hasGrabAnchor = false;
                _rotationHand = null;
                return;
            }

            var dragging = _input.RightTriggerHeld && !IsHandBlocked(_right);
            if (dragging)
            {
                _momentumVelocity = Vector3.zero;
                UpdateGrab();
            }
            else
            {
                _hasGrabAnchor = false;
                ApplyMomentumDecay();
            }

            var rotationHand = !dragging
                ? ActiveHand(_input.LeftGripHeld, _input.RightGripHeld)
                : null;
            if (rotationHand != null)
                UpdateRotation(rotationHand);
            else
                _rotationHand = null;

            if (!dragging && rotationHand == null)
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

        private void UpdateGrab()
        {
            if (!_hasGrabAnchor)
            {
                TryBeginGrab();
                return;
            }

            // Recomputed fresh from the grab-start reference every frame (not
            // incrementally), so there is no drift: how far the hand has moved
            // since the grab began, times the fixed gain set at grab time.
            var previousPosition = _rig.NavigationSpace.position;
            var controllerLocal = _rig.NavigationSpace.InverseTransformPoint(_right.position);
            var handDelta = controllerLocal - _grabStartControllerLocal;
            var desiredLocal = _grabStartControllerLocal + _grabStartOffsetLocal + handDelta * _grabGain;
            var anchorWorld = EcefToWorld(_grabAnchorEcef);
            var targetPosition = anchorWorld - _rig.NavigationSpace.rotation * desiredLocal;

            // Treat this as pulling the Earth beneath the user, not pulling the
            // user up the controller ray. The selected ECEF feature remains the
            // horizontal anchor while player height is left untouched.
            targetPosition.y = _rig.NavigationSpace.position.y;
            _rig.NavigationSpace.position = targetPosition;
            TrackMomentum(previousPosition);
        }

        private void TryBeginGrab()
        {
            var maximumDistance = ScaleMath.GeographicToUnityMeters(
                _settings.maximumGrabDistanceMeters,
                UserScale);

            // Require an actual physics surface. No sky, ocean-less gap, or
            // open-space aim should ever be grabbable.
            if (!Physics.Raycast(
                    _right.position,
                    _right.forward,
                    out var hit,
                    maximumDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) ||
                hit.collider.GetComponent<WorldSpaceButton>() != null ||
                hit.collider.GetComponent<CelestialDragHandle>() != null)
                return;

            _grabAnchorEcef = WorldToEcef(hit.point);
            var controllerLocal = _rig.NavigationSpace.InverseTransformPoint(_right.position);
            var hitLocal = _rig.NavigationSpace.InverseTransformPoint(hit.point);
            _grabStartControllerLocal = controllerLocal;
            _grabStartOffsetLocal = hitLocal - controllerLocal;

            // Gain of 1 out to arm's length, then scales up with distance so a
            // far-away point can still be dragged a long way without needing to
            // scale up first — clamped so it never runs away to something
            // unmanageable.
            var grabDistance = Vector3.Distance(controllerLocal, hitLocal);
            _grabGain = Mathf.Clamp(
                grabDistance / Mathf.Max(0.05f, _settings.grabGainReferenceDistanceMeters),
                1f,
                Mathf.Max(1f, _settings.maximumGrabGain));
            _hasGrabAnchor = true;
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
