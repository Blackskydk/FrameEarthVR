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
        private float _smoothedGroundCloseness;
        private float _carSpeedMetersPerSecond;
        private Vector3 _carForward = Vector3.forward;
        private readonly RaycastHit[] _flightCollisionHits = new RaycastHit[16];
        private readonly RaycastHit[] _carCollisionHits = new RaycastHit[16];

        public NavigationState State => _state;
        public float CurrentGeographicSpeed => Mathf.Abs(_currentGeographicSpeed);
        public float AltitudeMeters { get; private set; }
        public double3 LongitudeLatitudeHeight { get; private set; }
        public bool IsActivelyMoving { get; private set; }
        public float UserScale { get; set; } = 1f;
        public bool NavigationEnabled { get; set; } = true;
        public string CurrentPlaceName { get; private set; } = "Current location";
        public Vector3 HorizontalTravelDirection => _state.Mode == MovementMode.Car
            ? _carForward
            : HorizontalControllerForward();
        public float CarSpeedMetersPerSecond => _carSpeedMetersPerSecond;

        public float HeadingDegrees
        {
            get
            {
                var forward = Vector3.ProjectOnPlane(_rig.Camera.transform.forward, Vector3.up);
                return forward.sqrMagnitude < 0.0001f
                    ? 0f
                    : Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);
            }
        }

        /// <summary>Actual linear speed of the camera in real (Unity) metres per
        /// second, i.e. the physical motion the viewer's body and eyes experience
        /// regardless of geographic scale. This is what a comfort vignette should
        /// react to, not raw geographic speed.</summary>
        public float PhysicalSpeedMetersPerSecond { get; private set; }

        public void Initialize(
            IEarthVRInput input,
            EarthVRSettings settings,
            EarthVRRig rig,
            CesiumGeoreference georeference,
            string startingPlaceName = null)
        {
            _input = input;
            _settings = settings;
            _rig = rig;
            _georeference = georeference;
            LongitudeLatitudeHeight = new double3(
                settings.startLongitude,
                settings.startLatitude,
                settings.startHeightMeters);
            AltitudeMeters = settings.startHeightMeters;
            if (!string.IsNullOrWhiteSpace(startingPlaceName))
                CurrentPlaceName = startingPlaceName.Trim();
            _state.ModeChanged += OnMovementModeChanged;
        }

        private void OnDestroy() => _state.ModeChanged -= OnMovementModeChanged;

        private void OnMovementModeChanged(MovementMode mode)
        {
            _carSpeedMetersPerSecond = 0f;
            if (mode != MovementMode.Car)
                return;
            _carForward = HorizontalControllerForward();
            if (_carForward.sqrMagnitude < 0.0001f)
                _carForward = Vector3.forward;
        }

        private void Update()
        {
            if (_input == null)
                return;

            UpdateGeographicPosition();
            if (!NavigationEnabled)
            {
                _currentGeographicSpeed = 0f;
                _speedVelocity = 0f;
                IsActivelyMoving = false;
                PhysicalSpeedMetersPerSecond = 0f;
                UpdateClippingPlanes();
                return;
            }
            if (_input.ToggleMovementModePressed)
            {
                _state.Toggle();
                // Flight and Grounded speeds come from unrelated formulas (altitude-
                // adaptive vs. human-scale walking). Carrying a damped speed value
                // across the switch could otherwise read as a lurch in the new mode.
                _currentGeographicSpeed = 0f;
                _speedVelocity = 0f;
            }
            // A mode-change listener may begin a scale-only perspective
            // transition and freeze locomotion synchronously this frame.
            if (!NavigationEnabled)
            {
                IsActivelyMoving = false;
                PhysicalSpeedMetersPerSecond = 0f;
                UpdateClippingPlanes();
                return;
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
            if (_state.Mode == MovementMode.Car)
            {
                ApplyCarMovement(fly);
                return;
            }
            // The Frame shoulder is an exclusive locomotion boost. Some runtimes may
            // expose overlapping generic controller usages, so boost wins over any
            // simultaneous grab-looking state. Only the right trigger grabs the
            // world now, so the left trigger no longer freezes flight either.
            var manipulatingWorld = !_input.BoostHeld &&
                                    (_input.LeftGripHeld || _input.RightGripHeld ||
                                     _input.RightTriggerHeld);
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
                    _settings.maximumGeographicSpeedMetersPerSecond,
                    _settings.altitudeCruiseFractionPerSecond);

            // Ease the boost multiplier in/out over its own time constant instead
            // of snapping it on with the shoulder button, so a boost press or
            // release reads as spooling up rather than a jump-cut in speed.
            var boostTarget = _input.BoostHeld ? 1f : 0f;
            _boostBlend = Mathf.MoveTowards(
                _boostBlend,
                boostTarget,
                Time.deltaTime / Mathf.Max(0.001f, _settings.boostEaseSeconds));
            baseSpeed *= Mathf.Lerp(1f, _settings.boostSpeedMultiplier, _boostBlend);

            var targetSpeed = Mathf.Min(baseSpeed, _settings.maximumGeographicSpeedMetersPerSecond) * requested;

            // Damp the target speed as terrain approaches below so a fast, low
            // flight path eases off instead of auguring straight into a hillside.
            // Boost is an explicit, deliberate request for maximum speed, so this
            // safety net steps aside rather than fighting it — otherwise boost
            // could read as doing nothing whenever flown low over terrain.
            if (_state.Mode == MovementMode.Flight && !_input.BoostHeld)
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

                // Keep locomotion tied to controller yaw. A terrain-hit heading
                // jumps as photogrammetry refines or the ray crosses a roof edge.
                direction = (horizontalForward * fly.y + horizontalRight * fly.x).normalized;
            }
            else
            {
                direction = _rig.RightController.forward * fly.y + _rig.RightController.right * fly.x;
                if (direction.sqrMagnitude > 0.0001f)
                    direction.Normalize();
            }

            var unitySpeed = ScaleMath.GeographicToUnityMeters(_currentGeographicSpeed, UserScale);
            var displacement = direction * (unitySpeed * Time.deltaTime);
            if (_state.Mode == MovementMode.Flight)
                displacement = ClampFlightDisplacement(displacement);
            _rig.NavigationSpace.position += displacement;
            PhysicalSpeedMetersPerSecond = displacement.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            IsActivelyMoving = PhysicalSpeedMetersPerSecond > 0.05f;
        }

        private void ApplyCarMovement(Vector2 input)
        {
            var throttle = Mathf.Clamp(input.y, -1f, 1f);
            var maximumForward = _settings.carMaximumSpeedMetersPerSecond *
                                 (_input.BoostHeld ? 1.35f : 1f);
            var targetSpeed = throttle >= 0f
                ? throttle * maximumForward
                : throttle * _settings.carMaximumReverseSpeedMetersPerSecond;
            _carSpeedMetersPerSecond = CarDrivingMath.MoveSpeedTowards(
                _carSpeedMetersPerSecond,
                targetSpeed,
                _settings.carAccelerationMetersPerSecondSquared,
                _settings.carBrakingMetersPerSecondSquared,
                Time.deltaTime);

            var steeringDelta = CarDrivingMath.CalculateSteeringDelta(
                input.x,
                _carSpeedMetersPerSecond,
                Mathf.Max(0.1f, maximumForward),
                _settings.carSteeringDegreesPerSecond,
                Time.deltaTime);
            _carForward = Quaternion.AngleAxis(steeringDelta, Vector3.up) * _carForward;
            _carForward = Vector3.ProjectOnPlane(_carForward, Vector3.up).normalized;

            var requestedDisplacement = _carForward * (_carSpeedMetersPerSecond * Time.deltaTime);
            var displacement = ClampCarDisplacement(requestedDisplacement);
            if (requestedDisplacement.sqrMagnitude > 0.0001f &&
                displacement.sqrMagnitude < requestedDisplacement.sqrMagnitude * 0.25f)
                _carSpeedMetersPerSecond = 0f;
            _rig.NavigationSpace.position += displacement;

            _currentGeographicSpeed = Mathf.Abs(_carSpeedMetersPerSecond) * UserScale;
            _speedVelocity = 0f;
            PhysicalSpeedMetersPerSecond = displacement.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            IsActivelyMoving = PhysicalSpeedMetersPerSecond > 0.05f;
        }

        private Vector3 ClampCarDisplacement(Vector3 displacement)
        {
            var distance = displacement.magnitude;
            if (distance <= 0.0001f)
                return Vector3.zero;

            var direction = displacement / distance;
            var radius = Mathf.Max(0.1f, _settings.carCollisionRadiusMeters);
            var castOrigin = _rig.Camera.transform.position + Vector3.down * Mathf.Min(0.65f, radius);
            var hitCount = Physics.SphereCastNonAlloc(
                castOrigin,
                radius,
                direction,
                _carCollisionHits,
                distance + 0.08f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var nearestDistance = float.PositiveInfinity;
            for (var i = 0; i < hitCount; i++)
            {
                var collider = _carCollisionHits[i].collider;
                if (collider == null ||
                    collider.GetComponent<WorldSpaceButton>() != null ||
                    collider.GetComponent<CelestialDragHandle>() != null ||
                    collider.GetComponent<MiniatureGlobeSurface>() != null)
                    continue;
                // Near-horizontal terrain triangles directly underneath the car
                // are the road, not an obstacle. Walls and steep terrain stop it.
                if (Vector3.Dot(_carCollisionHits[i].normal, Vector3.up) > 0.55f)
                    continue;
                nearestDistance = Mathf.Min(nearestDistance, _carCollisionHits[i].distance);
            }

            if (float.IsPositiveInfinity(nearestDistance))
                return displacement;
            return direction * Mathf.Min(distance, Mathf.Max(0f, nearestDistance - 0.08f));
        }

        private Vector3 HorizontalControllerForward()
        {
            var forward = _rig != null
                ? Vector3.ProjectOnPlane(_rig.RightController.forward, Vector3.up)
                : Vector3.forward;
            if (forward.sqrMagnitude < 0.0001f && _rig != null)
                forward = Vector3.ProjectOnPlane(_rig.Camera.transform.forward, Vector3.up);
            return forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
        }

        private Vector3 ClampFlightDisplacement(Vector3 displacement)
        {
            var distance = displacement.magnitude;
            if (distance <= 0.0001f)
                return Vector3.zero;

            var direction = displacement / distance;
            var radius = Mathf.Max(0.02f, _settings.flightCollisionRadiusMeters);
            var hitCount = Physics.SphereCastNonAlloc(
                _rig.Camera.transform.position,
                radius,
                direction,
                _flightCollisionHits,
                distance + _settings.flightSurfaceClearanceMeters,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var nearestDistance = float.PositiveInfinity;
            for (var i = 0; i < hitCount; i++)
            {
                var collider = _flightCollisionHits[i].collider;
                if (collider == null ||
                    collider.GetComponent<WorldSpaceButton>() != null ||
                    collider.GetComponent<CelestialDragHandle>() != null ||
                    collider.GetComponent<MiniatureGlobeSurface>() != null)
                    continue;
                nearestDistance = Mathf.Min(nearestDistance, _flightCollisionHits[i].distance);
            }

            if (float.IsPositiveInfinity(nearestDistance))
                return displacement;
            var allowed = Mathf.Max(0f, nearestDistance - _settings.flightSurfaceClearanceMeters);
            return direction * Mathf.Min(distance, allowed);
        }

        /// <summary>Returns 1 when the ground below is far enough away to ignore,
        /// easing down toward <see cref="EarthVRSettings.minimumApproachSpeedFraction"/>
        /// as the camera nears whatever terrain lies directly beneath it. A dense
        /// city skyline has a rooftop then a gap then a rooftop every few
        /// metres; sampling only straight down and reacting instantly to each
        /// one made the response visibly jumpy. This tightens instantly (never
        /// delay the safety response toward more danger) but relaxes back
        /// toward full speed gradually, and adds a short forward-looking probe
        /// so an upcoming rooftop starts easing the response in before the
        /// camera is already over it.</summary>
        private float ComputeGroundApproachSpeedFraction()
        {
            var safetyDistanceUnity = ScaleMath.GeographicToUnityMeters(
                _settings.groundApproachSafetyMeters,
                UserScale);
            if (safetyDistanceUnity <= 0f)
            {
                _smoothedGroundCloseness = 0f;
                return 1f;
            }

            var closenessBelow = SampleGroundCloseness01(_rig.Camera.transform.position, safetyDistanceUnity);

            var closenessAhead = 0f;
            var headingFlat = Vector3.ProjectOnPlane(_rig.RightController.forward, Vector3.up);
            if (headingFlat.sqrMagnitude > 0.0001f)
            {
                var lookaheadUnity = Mathf.Min(
                    safetyDistanceUnity,
                    ScaleMath.GeographicToUnityMeters(Mathf.Abs(_currentGeographicSpeed), UserScale) *
                    Mathf.Max(0f, _settings.groundApproachLookaheadSeconds));
                if (lookaheadUnity > 0.01f)
                {
                    var aheadOrigin = _rig.Camera.transform.position + headingFlat.normalized * lookaheadUnity;
                    closenessAhead = SampleGroundCloseness01(aheadOrigin, safetyDistanceUnity);
                }
            }

            var rawCloseness = Mathf.Max(closenessBelow, closenessAhead);
            _smoothedGroundCloseness = rawCloseness > _smoothedGroundCloseness
                ? rawCloseness
                : Mathf.MoveTowards(
                    _smoothedGroundCloseness,
                    rawCloseness,
                    Time.deltaTime / Mathf.Max(0.01f, _settings.groundApproachRelaxSeconds));

            return Mathf.Lerp(1f, _settings.minimumApproachSpeedFraction, _smoothedGroundCloseness);
        }

        /// <summary>0 when the surface directly below <paramref name="origin"/> is
        /// at or beyond <paramref name="maxDistance"/> (or absent entirely), 1
        /// when touching it.</summary>
        private float SampleGroundCloseness01(Vector3 origin, float maxDistance)
        {
            if (maxDistance <= 0f)
                return 0f;
            if (!Physics.Raycast(
                    origin,
                    Vector3.down,
                    out var hit,
                    maxDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
                return 0f;
            return 1f - Mathf.Clamp01(hit.distance / maxDistance);
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
                hit.collider.GetComponent<CelestialDragHandle>() != null ||
                hit.collider.GetComponent<MiniatureGlobeSurface>() != null)
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
            _rig.NavigationSpace.rotation = CalculateUprightRotation(_rig.NavigationSpace.rotation);
            _rig.NavigationSpace.position += headPosition - _rig.Camera.transform.position;
            Physics.SyncTransforms();
        }

        /// <summary>Removes pitch and roll without relying on Euler angles,
        /// which become ambiguous after a planetary-orbit transition. The
        /// navigation root's projected heading is retained.</summary>
        public static Quaternion CalculateUprightRotation(Quaternion currentRotation)
        {
            var forward = Vector3.ProjectOnPlane(
                currentRotation * Vector3.forward,
                Vector3.up);
            if (forward.sqrMagnitude < 0.000001f)
            {
                var right = Vector3.ProjectOnPlane(
                    currentRotation * Vector3.right,
                    Vector3.up);
                forward = right.sqrMagnitude > 0.000001f
                    ? Vector3.Cross(right.normalized, Vector3.up)
                    : Vector3.forward;
            }
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        public void GoToLocation(double longitude, double latitude) =>
            GoToLocation(longitude, latitude, _settings.searchArrivalHeightMeters, 0f, null);

        public void GoToLocation(
            double longitude,
            double latitude,
            double heightMeters,
            float headingDegrees,
            string placeName)
        {
            _currentGeographicSpeed = 0f;
            _speedVelocity = 0f;

            _georeference.SetOriginLongitudeLatitudeHeight(longitude, latitude, 0d);
            _rig.NavigationSpace.localPosition = Vector3.zero;
            _rig.NavigationSpace.localRotation = Quaternion.identity;
            _rig.TrackingOrigin.localRotation = Quaternion.identity;

            var trackedHead = _rig.Camera.transform.localPosition;
            var arrivalHeightUnity = ScaleMath.GeographicToUnityMeters(
                Mathf.Max(0f, (float)heightMeters),
                Mathf.Max(0.01f, UserScale));
            _rig.TrackingOrigin.localPosition = new Vector3(
                -trackedHead.x,
                arrivalHeightUnity - trackedHead.y,
                -trackedHead.z);

            SetHeading(headingDegrees);

            LongitudeLatitudeHeight = new double3(
                longitude,
                latitude,
                heightMeters);
            AltitudeMeters = Mathf.Max(0f, (float)heightMeters);
            if (!string.IsNullOrWhiteSpace(placeName))
                CurrentPlaceName = placeName.Trim();
        }

        /// <summary>Adopts a camera pose reached by continuous travel without
        /// resetting Navigation Space or producing a final teleport.</summary>
        public void CompleteSeamlessArrival(string placeName)
        {
            _currentGeographicSpeed = 0f;
            _speedVelocity = 0f;
            _boostBlend = 0f;
            IsActivelyMoving = false;
            PhysicalSpeedMetersPerSecond = 0f;
            UpdateGeographicPosition();
            if (!string.IsNullOrWhiteSpace(placeName))
                CurrentPlaceName = placeName.Trim();
        }

        private void SetHeading(float headingDegrees)
        {
            var currentForward = Vector3.ProjectOnPlane(_rig.Camera.transform.forward, Vector3.up);
            if (currentForward.sqrMagnitude < 0.0001f)
                return;
            var currentHeading = Mathf.Atan2(currentForward.x, currentForward.z) * Mathf.Rad2Deg;
            _rig.NavigationSpace.RotateAround(
                _rig.Camera.transform.position,
                Vector3.up,
                Mathf.DeltaAngle(currentHeading, headingDegrees));
        }

        private void UpdateClippingPlanes()
        {
            _rig.Camera.nearClipPlane = Mathf.Max(0.001f, _settings.humanNearClipMeters / Mathf.Sqrt(UserScale));
            var geographicFarDistance = CalculateGeographicFarClipDistance(
                AltitudeMeters,
                _settings.geographicFarClipMeters,
                _settings.horizonFarClipMultiplier);
            _rig.Camera.farClipPlane = Mathf.Max(
                1000f,
                ScaleMath.GeographicToUnityMeters(geographicFarDistance, UserScale));
        }

        /// <summary>Keeps the far plane beyond the curved-Earth limb without
        /// forcing Flight mode to render to a fixed interplanetary distance.
        /// The tighter range preserves depth precision, preventing distant
        /// terrain from breaking into a visible flat cutoff before the horizon.</summary>
        public static float CalculateGeographicFarClipDistance(
            float altitudeMeters,
            float minimumDistanceMeters,
            float horizonMultiplier)
        {
            const double earthRadiusMeters = 6378137d;
            var altitude = System.Math.Max(0d, altitudeMeters);
            var horizonDistance = System.Math.Sqrt(
                altitude * (2d * earthRadiusMeters + altitude));
            return (float)System.Math.Max(
                System.Math.Max(1000d, minimumDistanceMeters),
                horizonDistance * System.Math.Max(1d, horizonMultiplier));
        }
    }
}
