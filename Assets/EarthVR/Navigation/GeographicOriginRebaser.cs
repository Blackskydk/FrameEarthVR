using CesiumForUnity;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Scaling;
using EarthVR.Sky;
using Unity.Mathematics;
using UnityEngine;

namespace EarthVR.Navigation
{
    /// <summary>
    /// Keeps Unity-space coordinates small while retaining the viewer's exact
    /// ECEF position and orientation. Both Earth and Navigation Space change
    /// reference frames in the same LateUpdate, so the visible view is stable.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class GeographicOriginRebaser : MonoBehaviour
    {
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private CesiumGeoreference _georeference;
        private WorldManipulationController _scaling;
        private SunSkyController _sunAndSky;
        private float _nextAllowedRebaseTime;

        public int RebaseCount { get; private set; }
        public float DistanceFromOriginUnityMeters { get; private set; }

        public void Initialize(
            EarthVRSettings settings,
            EarthVRRig rig,
            CesiumGeoreference georeference,
            WorldManipulationController scaling,
            SunSkyController sunAndSky)
        {
            _settings = settings;
            _rig = rig;
            _georeference = georeference;
            _scaling = scaling;
            _sunAndSky = sunAndSky;
        }

        private void LateUpdate()
        {
            if (_settings == null || _rig == null || _georeference == null || _scaling == null)
                return;

            var camera = _rig.Camera.transform;
            var cameraLocal = _georeference.transform.InverseTransformPoint(camera.position);
            DistanceFromOriginUnityMeters = cameraLocal.magnitude;
            if (DistanceFromOriginUnityMeters < _settings.originRebaseDistanceUnityMeters ||
                Time.unscaledTime < _nextAllowedRebaseTime ||
                _scaling.IsDraggingEarth)
                return;

            RebaseNow();
            _nextAllowedRebaseTime = Time.unscaledTime + _settings.originRebaseCooldownSeconds;
        }

        /// <summary>Immediately moves the local reference frame to the viewer
        /// while preserving the exact visible ECEF pose. Mode transitions call
        /// this first so scale changes are centered on the eyes.</summary>
        public void RebaseNow()
        {
            if (_rig == null || _georeference == null || _scaling == null)
                return;
            var camera = _rig.Camera.transform;
            RebaseAtEcef(WorldPositionToEcef(camera.position));
        }

        /// <summary>Moves the local reference frame to an explicit geographic
        /// point while preserving the rendered camera pose exactly. Planetary
        /// travel uses the destination surface so a globe-to-city descent never
        /// accumulates multi-million-metre Unity coordinates.</summary>
        public void RebaseAtEcef(double3 newOriginEcef)
        {
            if (_rig == null || _georeference == null || _scaling == null)
                return;
            var camera = _rig.Camera.transform;
            var oldCameraPosition = camera.position;
            var oldCameraRotation = camera.rotation;
            var cameraEcef = WorldPositionToEcef(oldCameraPosition);
            var forwardEcef = WorldDirectionToEcef(camera.forward);
            var upEcef = WorldDirectionToEcef(camera.up);

            _georeference.SetOriginEarthCenteredEarthFixed(
                newOriginEcef.x,
                newOriginEcef.y,
                newOriginEcef.z);

            var desiredCameraPosition = EcefPositionToWorld(cameraEcef);
            var desiredForward = EcefDirectionToWorld(forwardEcef).normalized;
            var desiredUp = EcefDirectionToWorld(upEcef).normalized;
            if (Vector3.Cross(desiredForward, desiredUp).sqrMagnitude < 0.0001f)
                desiredUp = Vector3.up;
            var desiredCameraRotation = Quaternion.LookRotation(desiredForward, desiredUp);

            NavigationPoseMath.CalculateRootPose(
                _rig.NavigationSpace.position,
                _rig.NavigationSpace.rotation,
                oldCameraPosition,
                oldCameraRotation,
                desiredCameraPosition,
                desiredCameraRotation,
                out var navigationPosition,
                out var navigationRotation,
                out var rotationDelta);
            _rig.NavigationSpace.SetPositionAndRotation(navigationPosition, navigationRotation);
            _scaling.ApplyOriginRebaseRotation(rotationDelta);
            _sunAndSky?.RefreshForReferenceFrameChange();
            Physics.SyncTransforms();
            RebaseCount++;
        }

        private double3 WorldPositionToEcef(Vector3 worldPosition)
        {
            var local = _georeference.transform.InverseTransformPoint(worldPosition);
            return _georeference.TransformUnityPositionToEarthCenteredEarthFixed(
                new double3(local.x, local.y, local.z));
        }

        private double3 WorldDirectionToEcef(Vector3 worldDirection)
        {
            var local = _georeference.transform.InverseTransformDirection(worldDirection);
            return _georeference.TransformUnityDirectionToEarthCenteredEarthFixed(
                new double3(local.x, local.y, local.z));
        }

        private Vector3 EcefPositionToWorld(double3 ecef)
        {
            var local = _georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            return _georeference.transform.TransformPoint((Vector3)(float3)local);
        }

        private Vector3 EcefDirectionToWorld(double3 ecefDirection)
        {
            var local = _georeference.TransformEarthCenteredEarthFixedDirectionToUnity(ecefDirection);
            return _georeference.transform.TransformDirection((Vector3)(float3)local);
        }
    }

    public static class NavigationPoseMath
    {
        public static void CalculateRootPose(
            Vector3 rootPosition,
            Quaternion rootRotation,
            Vector3 childPosition,
            Quaternion childRotation,
            Vector3 desiredChildPosition,
            Quaternion desiredChildRotation,
            out Vector3 newRootPosition,
            out Quaternion newRootRotation,
            out Quaternion rotationDelta)
        {
            rotationDelta = desiredChildRotation * Quaternion.Inverse(childRotation);
            newRootRotation = rotationDelta * rootRotation;
            newRootPosition = desiredChildPosition + rotationDelta * (rootPosition - childPosition);
        }
    }
}
