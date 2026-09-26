using System.Collections;
using CesiumForUnity;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Navigation;
using EarthVR.Scaling;
using Unity.Mathematics;
using UnityEngine;

namespace EarthVR.Terrain
{
    [DefaultExecutionOrder(100)]
    public sealed class GroundingController : MonoBehaviour
    {
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private NavigationController _navigation;
        private WorldManipulationController _scaling;
        private CesiumGeoreference _georeference;
        private Cesium3DTileset _tileset;
        private double3? _sampledSurfaceEcef;
        private float _verticalVelocity;

        public bool HasGroundSolution { get; private set; }

        public void Initialize(
            EarthVRSettings settings,
            EarthVRRig rig,
            NavigationController navigation,
            WorldManipulationController scaling,
            CesiumGeoreference georeference,
            Cesium3DTileset tileset)
        {
            _settings = settings;
            _rig = rig;
            _navigation = navigation;
            _scaling = scaling;
            _georeference = georeference;
            _tileset = tileset;
            _navigation.State.ModeChanged += OnModeChanged;
            StartCoroutine(SampleHeightLoop());
        }

        private void OnDestroy()
        {
            if (_navigation != null)
                _navigation.State.ModeChanged -= OnModeChanged;
        }

        private void OnModeChanged(MovementMode mode)
        {
            if (mode != MovementMode.Grounded)
                return;

            // Become human scale but stay at whatever altitude Flight mode left
            // the viewer at — SetUserScale keeps the current position (the
            // pivot) fixed in world space, it only changes the scale factor.
            // No forced snap to the literal ground here: that would fight the
            // "I want to be at the height I'm currently at" ask below.
            _scaling.SetUserScale(1f);
        }

        private void LateUpdate()
        {
            if (_navigation == null || _navigation.State.Mode != MovementMode.Grounded)
                return;
            if (_scaling.IsDraggingEarth)
            {
                // The grab solver owns all three axes while held so the selected
                // terrain point cannot be pulled away from the controller by the
                // independent ground-height correction.
                _verticalVelocity = 0f;
                return;
            }
            if (!_navigation.IsActivelyMoving)
            {
                // Only follow terrain height while the thumbstick is actually
                // driving travel. Otherwise physically stepping around a room-
                // scale play area — or just standing still — must never cause an
                // automatic climb onto whatever happens to be underneath.
                return;
            }

            if (!TryComputeDesiredFloor(out var desiredFloor))
            {
                HasGroundSolution = false;
                return;
            }

            HasGroundSolution = true;
            var currentFloor = _rig.TrackingOrigin.position.y;
            float corrected;
            if (desiredFloor > currentFloor)
            {
                // Never ease upward through solid terrain. Roofs and replacement
                // high-detail tiles must push the tracking floor clear this frame.
                corrected = desiredFloor;
                _verticalVelocity = 0f;
            }
            else
            {
                // Descending toward a lower surface can remain comfortable.
                corrected = Mathf.SmoothDamp(
                    currentFloor,
                    desiredFloor,
                    ref _verticalVelocity,
                    _settings.groundCorrectionSeconds);
            }
            _rig.NavigationSpace.position += Vector3.up * (corrected - currentFloor);
        }

        private bool TryComputeDesiredFloor(out float desiredFloor)
        {
            desiredFloor = 0f;
            var scale = _scaling.UserScale;
            var probeDistance = ScaleMath.GeographicToUnityMeters(_settings.groundProbeDistanceMeters, scale);
            // Probe below the tracking floor, not the camera. The camera moves
            // with real head/room tracking, which would otherwise re-sample
            // terrain height (and climb onto a nearby roof or wall) from mere
            // physical stepping. TrackingOrigin only moves from deliberate
            // navigation (thumbstick travel, grab, or this controller's own
            // correction), which is the only thing that should re-probe.
            var trackingOriginPosition = _rig.TrackingOrigin.position;
            var start = trackingOriginPosition + Vector3.up * Mathf.Max(2f, probeDistance * 0.25f);
            float? surfaceY = null;
            if (Physics.Raycast(start, Vector3.down, out var hit, Mathf.Max(10f, probeDistance * 1.5f)))
                surfaceY = hit.point.y;
            else if (_sampledSurfaceEcef.HasValue)
                surfaceY = EcefToWorld(_sampledSurfaceEcef.Value).y;

            if (!surfaceY.HasValue)
                return false;

            // Keep a small physical buffer even at giant scales. The geographic
            // clearance can otherwise become sub-millimetre in Unity space and
            // lose the precision battle against streamed photogrammetry meshes.
            var clearanceUnity = Mathf.Max(
                0.015f,
                ScaleMath.GeographicToUnityMeters(_settings.groundClearanceMeters, scale));
            desiredFloor = surfaceY.Value + clearanceUnity;
            return true;
        }

        private IEnumerator SampleHeightLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(_settings.heightSampleIntervalSeconds);
                if (_navigation.State.Mode != MovementMode.Grounded || _tileset == null || _tileset.suspendUpdate)
                    continue;

                var llh = _navigation.LongitudeLatitudeHeight;
                var task = _tileset.SampleHeightMostDetailed(new double3(llh.x, llh.y, 0d));
                yield return new WaitUntil(() => task.IsCompleted);
                if (task.IsFaulted || task.IsCanceled)
                    continue;
                var result = task.Result;
                if (result.sampleSuccess == null || result.sampleSuccess.Length == 0 || !result.sampleSuccess[0])
                    continue;

                var surfaceLlh = result.longitudeLatitudeHeightPositions[0];
                var ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(surfaceLlh);
                _sampledSurfaceEcef = ecef;
            }
        }

        private Vector3 EcefToWorld(double3 ecef)
        {
            var local = _georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            return _georeference.transform.TransformPoint(
                new Vector3((float)local.x, (float)local.y, (float)local.z));
        }
    }
}
