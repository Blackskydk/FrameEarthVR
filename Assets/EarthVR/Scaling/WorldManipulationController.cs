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
        // changes for as long as the trigger stays held, and its original ray
        // depth is retained. Every pose update solves Navigation Space so that
        // exact geographic point is the pointer endpoint—there is no gain,
        // spring, dead travel, or replacement surface sample.
        private bool _hasGrabAnchor;
        private double3 _grabAnchorEcef;
        private float _grabRayDistance;
        private float _groundedGrabFloorY;
        private bool _rightTriggerWasHeld;
        private bool _rightTriggerOwnedByUi;

        private Transform _rotationHand;
        private Quaternion _previousHandLocalRotation;

        private Vector3 _momentumVelocity;
        private double3? _groundedScaleAnchorEcef;

        public float UserScale { get; private set; } = 1f;
        public bool IsDraggingEarth => _hasGrabAnchor;
        public bool IsScalingGrounded => _groundedScaleAnchorEcef.HasValue;
        public bool InteractionsEnabled { get; set; } = true;
        public bool GroundedScalingEnabled { get; set; } = true;
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

        private void OnEnable()
        {
            // OpenXR performs a second pose update immediately before rendering.
            // Reapply the hard grab constraint after that update so the rendered
            // pointer can never get one pose ahead of the dragged terrain.
            Application.onBeforeRender += ApplyGrabConstraintBeforeRender;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ApplyGrabConstraintBeforeRender;
        }

        private void Update()
        {
            if (_input == null)
                return;
            if (!InteractionsEnabled || _input.BoostHeld ||
                _navigation.State.Mode != MovementMode.Grounded || !GroundedScalingEnabled ||
                _input.RightTriggerHeld || _input.LeftGripHeld || _input.RightGripHeld)
                _groundedScaleAnchorEcef = null;
            if (!InteractionsEnabled)
            {
                _hasGrabAnchor = false;
                _rotationHand = null;
                _momentumVelocity = Vector3.zero;
                _rightTriggerWasHeld = _input.RightTriggerHeld;
                _rightTriggerOwnedByUi = false;
                return;
            }
            if (_input.BoostHeld)
            {
                // Boost is an exclusive locomotion modifier in both movement
                // modes. Never let overlapping runtime bindings turn the same
                // hold into a grab, rotation, or grounded scale gesture.
                _hasGrabAnchor = false;
                _rotationHand = null;
                _momentumVelocity = Vector3.zero;
                _rightTriggerWasHeld = _input.RightTriggerHeld;
                // If trigger overlaps the boost hold, require a release before
                // terrain grabbing can begin after boost ends.
                _rightTriggerOwnedByUi = _input.RightTriggerHeld;
                return;
            }

            var rightTriggerHeld = _input.RightTriggerHeld;
            if (rightTriggerHeld && !_rightTriggerWasHeld && IsHandBlocked(_right))
                _rightTriggerOwnedByUi = true;
            if (!rightTriggerHeld)
                _rightTriggerOwnedByUi = false;
            _rightTriggerWasHeld = rightTriggerHeld;
            var dragging = rightTriggerHeld && !_rightTriggerOwnedByUi && !IsHandBlocked(_right);
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

            ApplyGrabConstraint(true);
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
                hit.collider.GetComponent<CelestialDragHandle>() != null ||
                hit.collider.GetComponent<MiniatureGlobeSurface>() != null)
                return;

            _grabAnchorEcef = WorldToEcef(hit.point);
            _grabRayDistance = Mathf.Max(0.01f, hit.distance);
            _groundedGrabFloorY = _rig.TrackingOrigin.position.y;
            _hasGrabAnchor = true;
        }

        private void ApplyGrabConstraintBeforeRender()
        {
            if (!_hasGrabAnchor || !InteractionsEnabled || _input == null ||
                !_input.RightTriggerHeld || _right == null || _rig == null)
                return;

            ApplyGrabConstraint(false);
        }

        private void ApplyGrabConstraint(bool trackMomentum)
        {
            var previousPosition = _rig.NavigationSpace.position;
            var controllerLocalPosition = _rig.NavigationSpace.InverseTransformPoint(_right.position);
            var controllerLocalForward = _rig.NavigationSpace
                .InverseTransformDirection(_right.forward)
                .normalized;
            var anchorWorld = EcefToWorld(_grabAnchorEcef);
            if (_navigation.State.Mode == MovementMode.Grounded)
            {
                _rig.NavigationSpace.position = WorldGrabConstraintMath.CalculateGroundedNavigationPosition(
                    anchorWorld,
                    _rig.NavigationSpace.rotation,
                    controllerLocalPosition,
                    controllerLocalForward,
                    _rig.TrackingOrigin.localPosition,
                    _groundedGrabFloorY,
                    _grabRayDistance,
                    out _grabRayDistance);
            }
            else
            {
                _rig.NavigationSpace.position = WorldGrabConstraintMath.CalculateNavigationPosition(
                    anchorWorld,
                    _rig.NavigationSpace.rotation,
                    controllerLocalPosition,
                    controllerLocalForward,
                    _grabRayDistance);
            }
            Physics.SyncTransforms();
            if (trackMomentum)
                TrackMomentum(previousPosition);
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
            if (_navigation.State.Mode != MovementMode.Grounded || !GroundedScalingEnabled ||
                IsHandBlocked(_right))
            {
                _groundedScaleAnchorEcef = null;
                return;
            }
            var scaleIntent = ScaleMath.GroundedScaleIntent(_right.forward.y, _input.Fly.y);
            if (Mathf.Abs(scaleIntent) < 0.001f)
            {
                _groundedScaleAnchorEcef = null;
                return;
            }
            _momentumVelocity = Vector3.zero;
            var scaleRate = _settings.groundedScaleDoublingsPerSecond;
            var multiplier = Mathf.Pow(
                2f,
                scaleIntent * scaleRate * Mathf.Min(Time.deltaTime, 0.05f));
            ApplyGroundedScale(UserScale * multiplier);
        }

        private void ApplyGroundedScale(float requestedScale)
        {
            // Anchor the supporting surface, not the clearance above it. Keeping
            // a giant's clearance in geographic metres would leave a human-sized
            // user suspended high above the ground when shrinking back down.
            _groundedScaleAnchorEcef ??= WorldToEcef(
                _rig.TrackingOrigin.position - Vector3.up * GroundedClearance());
            SetUserScale(requestedScale, _groundedScaleAnchorEcef.Value);
            // Solve from the same ECEF foot point each frame instead of repeatedly
            // converting a floor that terrain correction has moved underneath us.
            _rig.NavigationSpace.position = EcefToWorld(_groundedScaleAnchorEcef.Value) +
                Vector3.up * GroundedClearance() -
                _rig.NavigationSpace.TransformVector(_rig.TrackingOrigin.localPosition);
            Physics.SyncTransforms();
        }

        private float GroundedClearance() => Mathf.Max(0.015f,
            ScaleMath.GeographicToUnityMeters(_settings.groundClearanceMeters, UserScale));

        private bool IsPointingAtInterfaceOrCelestialHandle(Transform hand) =>
            Physics.Raycast(
                hand.position,
                hand.forward,
                out var hit,
                _settings.pointerMaximumLengthMeters,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore) &&
            (hit.collider.GetComponent<WorldSpaceButton>() != null ||
             hit.collider.GetComponent<CelestialDragHandle>() != null ||
             hit.collider.GetComponent<MiniatureGlobeSurface>() != null);

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

        /// <summary>Changes scale without moving the observer at all — no pivot,
        /// no compensating shift of Navigation Space. <see cref="SetUserScale"/>
        /// keeps a chosen point's *geographic* identity fixed as scale changes,
        /// which is right for scaling while standing somewhere, but altitude is
        /// itself scale-relative, so that alone does not hold the camera's raw
        /// position still. This does, exactly and unconditionally — for the
        /// Flight/Grounded perspective-shift transition, where the whole point
        /// is that the eyes must not move at all and only the world's apparent
        /// size changes around them.</summary>
        public void SetUserScaleKeepingObserverFixed(float requestedScale)
        {
            var clamped = ScaleMath.Clamp(requestedScale, _settings.minimumUserScale, _settings.maximumUserScale);
            _georeference.scale = ScaleMath.CesiumGlobeScale(clamped);
            UserScale = clamped;
            _navigation.UserScale = clamped;
            ScaleChanged?.Invoke(UserScale);
        }

        public void ApplyOriginRebaseRotation(Quaternion worldRotationDelta)
        {
            _momentumVelocity = worldRotationDelta * _momentumVelocity;
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

    public static class WorldGrabConstraintMath
    {
        /// <summary>Solves the root translation that makes an unmodified child
        /// ray end at the selected world point. Because this is an absolute solve
        /// from the current tracked pose, it has neither accumulated drift nor
        /// an elastic hand-to-world offset.</summary>
        public static Vector3 CalculateNavigationPosition(
            Vector3 anchorWorld,
            Quaternion navigationRotation,
            Vector3 controllerLocalPosition,
            Vector3 controllerLocalForward,
            float rayDistance)
        {
            var direction = controllerLocalForward.sqrMagnitude > 0.000001f
                ? controllerLocalForward.normalized
                : Vector3.forward;
            var pointerEndpointLocal = controllerLocalPosition +
                                       direction * Mathf.Max(0f, rayDistance);
            return anchorWorld - navigationRotation * pointerEndpointLocal;
        }

        /// <summary>Keeps the selected point exactly on the live controller ray
        /// while constraining the tracking floor to one world-space height. The
        /// extra degree of freedom is ray depth: dragging underneath the body
        /// changes how far along the ray the anchor lies instead of lifting the
        /// entire player away from the ground.</summary>
        public static Vector3 CalculateGroundedNavigationPosition(
            Vector3 anchorWorld,
            Quaternion navigationRotation,
            Vector3 controllerLocalPosition,
            Vector3 controllerLocalForward,
            Vector3 trackingFloorLocalPosition,
            float trackingFloorWorldY,
            float fallbackRayDistance,
            out float solvedRayDistance)
        {
            var directionLocal = controllerLocalForward.sqrMagnitude > 0.000001f
                ? controllerLocalForward.normalized
                : Vector3.forward;
            var directionWorld = navigationRotation * directionLocal;
            var controllerOffsetWorld = navigationRotation * controllerLocalPosition;
            var floorOffsetWorld = navigationRotation * trackingFloorLocalPosition;
            var navigationY = trackingFloorWorldY - floorOffsetWorld.y;

            if (Mathf.Abs(directionWorld.y) > 0.0001f)
            {
                solvedRayDistance =
                    (anchorWorld.y - navigationY - controllerOffsetWorld.y) /
                    directionWorld.y;
                if (solvedRayDistance < 0.01f ||
                    float.IsNaN(solvedRayDistance) ||
                    float.IsInfinity(solvedRayDistance))
                    solvedRayDistance = Mathf.Max(0.01f, fallbackRayDistance);
            }
            else
            {
                solvedRayDistance = Mathf.Max(0.01f, fallbackRayDistance);
            }

            var navigationPosition = anchorWorld -
                                     controllerOffsetWorld -
                                     directionWorld * solvedRayDistance;
            navigationPosition.y = navigationY;
            return navigationPosition;
        }
    }
}
