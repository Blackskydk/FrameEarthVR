using CesiumForUnity;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Input;
using EarthVR.Scaling;
using EarthVR.Sky;
using EarthVR.UI;
using Unity.Mathematics;
using UnityEngine;

namespace EarthVR.Navigation
{
    public sealed class NavigationController : MonoBehaviour
    {
        private IEarthVRInput _input;
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private CesiumGeoreference _georeference;
        private readonly NavigationState _state = new();
        private float _currentGeographicSpeed;
        private float _speedVelocity;
        private float _boostBlend;

        public NavigationState State => _state;
        public float CurrentGeographicSpeed => Mathf.Abs(_currentGeographicSpeed);
        public float AltitudeMeters { get; private set; }
        public double3 LongitudeLatitudeHeight { get; private set; }
        public bool IsActivelyMoving { get; private set; }
        public float UserScale { get; set; } = 1f;

        /// <summary>Actual linear speed of the camera in real (Unity) metres per
        /// second, i.e. the physical motion the viewer's body and eyes experience
        /// regardless of geographic scale. This is what a comfort vignette should
        /// react to, not raw geographic speed.</summary>
        public float PhysicalSpeedMetersPerSecond { get; private set; }

        public void Initialize(
            IEarthVRInput input,
            EarthVRSettings settings,
            EarthVRRig rig,
            CesiumGeoreference georeference)
        {
            _input = input;
            _settings = settings;
            _rig = rig;
            _georeference = georeference;
        }

        private void Update()
        {
            if (_input == null)
                return;

            UpdateGeographicPosition();
            if (_input.ToggleMovementModePressed)
            {
                _state.Toggle();
                // Flight and Grounded speeds come from unrelated formulas (altitude-
                // adaptive vs. human-scale walking). Carrying a damped speed value
                // across the switch could otherwise read as a lurch in the new mode.
                _currentGeographicSpeed = 0f;
                _speedVelocity = 0f;
            }
            if (_input.ResetViewPressed)
                ResetUpright();

            ApplyMovement();
            UpdateClippingPlanes();
        }

        private void UpdateGeographicPosition()
        {
            var cameraPosition = _rig.Camera.transform.position;
            var local = _georeference.transform.InverseTransformPoint(cameraPosition);
            var ecef = _georeference.TransformUnityPositionToEarthCenteredEarthFixed(
                new double3(local.x, local.y, local.z));
            LongitudeLatitudeHeight = CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
            AltitudeMeters = Mathf.Max(0f, (float)LongitudeLatitudeHeight.z);
        }

        private void ApplyMovement()
        {
            var fly = _input.Fly;
            // The Frame shoulder is an exclusive flight boost. Some runtimes may
            // expose overlapping generic controller usages, so boost wins over any
            // simultaneous grab-looking state.
            var manipulatingWorld = !_input.BoostHeld &&
                                    (_input.LeftGripHeld || _input.RightGripHeld ||
                                     _input.LeftTriggerHeld || _input.RightTriggerHeld);
            if (manipulatingWorld)
            {
                _currentGeographicSpeed = 0f;
                _speedVelocity = 0f;
                IsActivelyMoving = false;
                PhysicalSpeedMetersPerSecond = 0f;
                return;
            }

            var requested = Mathf.Clamp01(fly.magnitude);
            if (_state.Mode == MovementMode.Grounded)
            {
                var horizontalAim = Vector3.ProjectOnPlane(
                    _rig.RightController.forward,
                    Vector3.up).magnitude;
                var travelWhileScaling = Mathf.InverseLerp(0.08f, 0.45f, horizontalAim);
                if (travelWhileScaling <= 0.001f)
                {
                    _currentGeographicSpeed = 0f;
                    _speedVelocity = 0f;
                    IsActivelyMoving = false;
                    PhysicalSpeedMetersPerSecond = 0f;
                    return;
                }
                requested *= travelWhileScaling;
            }
            var baseSpeed = _state.Mode == MovementMode.Grounded
                ? _settings.humanScaleSpeedMetersPerSecond * UserScale
                : FlightSpeedModel.CalculateGeographicSpeed(
                    AltitudeMeters,
                    UserScale,
                    _settings.humanScaleSpeedMetersPerSecond,
                    _settings.altitudeSpeedMultiplier,
                    _settings.scaleSpeedExponent,
                    _settings.maximumGeographicSpeedMetersPerSecond);

            // Ease the boost multiplier in/out over its own time constant instead
            // of snapping it on with the shoulder button, so a boost press or
            // release reads as spooling up rather than a jump-cut in speed.
            var boostTarget = _input.BoostHeld && _state.Mode == MovementMode.Flight ? 1f : 0f;
            _boostBlend = Mathf.MoveTowards(
                _boostBlend,
                boostTarget,
                Time.deltaTime / Mathf.Max(0.001f, _settings.boostEaseSeconds));
            baseSpeed *= Mathf.Lerp(1f, _settings.boostSpeedMultiplier, _boostBlend);

            var targetSpeed = Mathf.Min(baseSpeed, _settings.maximumGeographicSpeedMetersPerSecond) * requested;

            // Damp the target speed as terrain approaches below so a fast, low
            // flight path eases off instead of auguring straight into a hillside.
            if (_state.Mode == MovementMode.Flight)
                targetSpeed *= ComputeGroundApproachSpeedFraction();

            var damping = Mathf.Abs(targetSpeed) > Mathf.Abs(_currentGeographicSpeed)
                ? _settings.accelerationSeconds
                : _settings.decelerationSeconds;
            _currentGeographicSpeed = Mathf.SmoothDamp(
                _currentGeographicSpeed,
                targetSpeed,
                ref _speedVelocity,
                Mathf.Max(0.001f, damping));

            Vector3 direction;
            if (_state.Mode == MovementMode.Grounded)
            {
                var horizontalRight = Vector3.ProjectOnPlane(_rig.RightController.right, Vector3.up);
                if (horizontalRight.sqrMagnitude < 0.001f)
                    horizontalRight = Vector3.ProjectOnPlane(_rig.Camera.transform.right, Vector3.up);
                horizontalRight.Normalize();
                var horizontalForward = Vector3.Cross(horizontalRight, Vector3.up).normalized;

                // When the pointer reaches terrain, steer toward that actual point.
                // A yaw-only heading can pass over a nearby aimed point, especially
                // while the world is also changing scale.
                if (TryGetGroundedAimDirection(out var aimDirection))
                    horizontalForward = aimDirection;
                direction = (horizontalForward * fly.y + horizontalRight * fly.x).normalized;
            }
            else
            {
                direction = _rig.RightController.forward * fly.y + _rig.RightController.right * fly.x;
                if (direction.sqrMagnitude > 0.0001f)
                    direction.Normalize();
            }

            var unitySpeed = ScaleMath.GeographicToUnityMeters(_currentGeographicSpeed, UserScale);
            _rig.NavigationSpace.position += direction * (unitySpeed * Time.deltaTime);
            IsActivelyMoving = Mathf.Abs(unitySpeed) > 0.05f;
            PhysicalSpeedMetersPerSecond = Mathf.Abs(unitySpeed);
        }

        /// <summary>Returns 1 when the ground below is far enough away to ignore,
        /// easing down toward <see cref="EarthVRSettings.minimumApproachSpeedFraction"/>
        /// as the camera nears whatever terrain lies directly beneath it.</summary>
        private float ComputeGroundApproachSpeedFraction()
        {
            var safetyDistanceUnity = ScaleMath.GeographicToUnityMeters(
                _settings.groundApproachSafetyMeters,
                UserScale);
            if (safetyDistanceUnity <= 0f)
                return 1f;

            if (!Physics.Raycast(
                    _rig.Camera.transform.position,
                    Vector3.down,
                    out var hit,
                    safetyDistanceUnity,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
                return 1f;

            var clearance01 = Mathf.Clamp01(hit.distance / safetyDistanceUnity);
            return Mathf.Lerp(_settings.minimumApproachSpeedFraction, 1f, clearance01);
        }

        private bool TryGetGroundedAimDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            var maximumDistance = Mathf.Max(
                _settings.pointerDefaultLengthMeters,
                ScaleMath.GeographicToUnityMeters(_settings.pointerMaximumLengthMeters, UserScale));
            if (!Physics.Raycast(
                    _rig.RightController.position,
                    _rig.RightController.forward,
                    out var hit,
                    maximumDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) ||
                hit.collider.GetComponent<WorldSpaceButton>() != null ||
                hit.collider.GetComponent<CelestialDragHandle>() != null)
                return false;

            direction = Vector3.ProjectOnPlane(
                hit.point - _rig.Camera.transform.position,
                Vector3.up);
            if (direction.sqrMagnitude < 0.0004f)
                return false;
            direction.Normalize();
            return true;
        }

        public void ResetUpright()
        {
            var headPosition = _rig.Camera.transform.position;
            _rig.NavigationSpace.rotation = Quaternion.Euler(0f, _rig.NavigationSpace.eulerAngles.y, 0f);
            _rig.NavigationSpace.position += headPosition - _rig.Camera.transform.position;
        }

        public void GoToLocation(double longitude, double latitude)
        {
            _currentGeographicSpeed = 0f;
            _speedVelocity = 0f;

            _georeference.SetOriginLongitudeLatitudeHeight(longitude, latitude, 0d);
            _rig.NavigationSpace.localPosition = Vector3.zero;
            _rig.NavigationSpace.localRotation = Quaternion.identity;
            _rig.TrackingOrigin.localRotation = Quaternion.identity;

            var trackedHead = _rig.Camera.transform.localPosition;
            _rig.TrackingOrigin.localPosition = new Vector3(
                -trackedHead.x,
                _settings.searchArrivalHeightMeters - trackedHead.y,
                -trackedHead.z);

            LongitudeLatitudeHeight = new double3(
                longitude,
                latitude,
                _settings.searchArrivalHeightMeters);
            AltitudeMeters = _settings.searchArrivalHeightMeters;
        }

        private void UpdateClippingPlanes()
        {
            _rig.Camera.nearClipPlane = Mathf.Max(0.001f, _settings.humanNearClipMeters / Mathf.Sqrt(UserScale));
            _rig.Camera.farClipPlane = Mathf.Max(
                1000f,
                ScaleMath.GeographicToUnityMeters(_settings.geographicFarClipMeters, UserScale));
        }
    }
}
