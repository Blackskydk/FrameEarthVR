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
        private GeographicOriginRebaser _originRebaser;
        private double3? _sampledSurfaceEcef;
        private float _verticalVelocity;
        private bool _scaleTransitionActive;
        private bool _modeTransitionPending;
        private float _scaleTransitionElapsed;
        private float _scaleTransitionStart;
        private float _scaleTransitionTarget;
        private int _transitionRequestId;
        private bool _suppressModeTransition;
        private readonly RaycastHit[] _groundHits = new RaycastHit[32];

        public bool HasGroundSolution { get; private set; }

        public void SetOriginRebaser(GeographicOriginRebaser originRebaser) =>
            _originRebaser = originRebaser;

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
            if (_suppressModeTransition)
                return;
            _transitionRequestId++; // Invalidate any in-flight async request from a previous switch.
            // This reference-frame change is visually invisible, but places the
            // viewer at the scale center. The following animation can therefore
            // change only apparent world/person scale without translating eyes.
            _originRebaser?.RebaseNow();
            if (IsSurfaceMode(mode))
                _navigation.ResetUpright();
            _modeTransitionPending = true;
            _scaling.GroundedScalingEnabled = false;
            _scaling.InteractionsEnabled = false;
            _navigation.NavigationEnabled = false;
            if (IsSurfaceMode(mode))
                StartCoroutine(BeginTransitionToAltitudeHeight(_transitionRequestId));
            else
                CompleteTransitionWithoutScaling();
        }

        /// <summary>Flight changes the locomotion constraint, not the size of
        /// Earth. Retaining the current user scale preserves the same visible
        /// curvature (and therefore the same seamless horizon) that the user
        /// established in Grounded mode.</summary>
        private void CompleteTransitionWithoutScaling()
        {
            _scaleTransitionActive = false;
            _modeTransitionPending = false;
            _verticalVelocity = 0f;
            _scaling.GroundedScalingEnabled = true;
            _scaling.InteractionsEnabled = true;
            _navigation.NavigationEnabled = true;
        }

        /// <summary>Restores a saved movement mode without starting the normal
        /// flight/grounded perspective animation. The saved viewpoint already
        /// contains the scale and altitude that animation would otherwise solve.</summary>
        public void RestoreModeForArrival(MovementMode mode)
        {
            _transitionRequestId++;
            _scaleTransitionActive = false;
            _modeTransitionPending = false;
            _verticalVelocity = 0f;
            _suppressModeTransition = true;
            _navigation.State.SetMode(mode);
            _suppressModeTransition = false;
            if (IsSurfaceMode(mode))
                _navigation.ResetUpright();
            _scaling.GroundedScalingEnabled = true;
        }

        /// <summary>Entering Grounded mode should feel like the ground rising to
        /// meet planted feet while the eyes never move at all. Flight altitude
        /// can be far higher than any physics raycast usefully reaches (or
        /// where terrain colliders are even loaded), so this tries a raycast
        /// first (instant, works for the common low-altitude case) and falls
        /// back to Cesium's own unbounded height query otherwise — at the cost
        /// of a short asynchronous wait before the perspective shift begins.
        /// Captures the eye/floor reference before any wait, so nothing about
        /// "where the eyes currently are" can drift while it is in flight.</summary>
        private IEnumerator BeginTransitionToAltitudeHeight(int requestId)
        {
            var targetWorldY = _rig.TrackingOrigin.position.y;
            var startScale = _scaling.UserScale;
            var probeDistance = ScaleMath.GeographicToUnityMeters(_settings.groundProbeDistanceMeters, startScale);

            double3? groundEcef = null;
            if (TryRaycastTerrain(_rig.Camera.transform.position, Mathf.Max(10f, probeDistance * 4f), out var hit))
            {
                groundEcef = WorldToEcef(hit.point);
            }
            else if (_tileset != null)
            {
                var llh = _navigation.LongitudeLatitudeHeight;
                var task = _tileset.SampleHeightMostDetailed(new double3(llh.x, llh.y, 0d));
                var timeoutAt = Time.unscaledTime + _settings.modeGroundSampleTimeoutSeconds;
                while (!task.IsCompleted && Time.unscaledTime < timeoutAt)
                    yield return null;

                // Stale if another mode switch happened while this was in flight.
                if (requestId != _transitionRequestId)
                    yield break;

                if (task.IsCompleted && !task.IsFaulted && !task.IsCanceled)
                {
                    var result = task.Result;
                    if (result.sampleSuccess != null && result.sampleSuccess.Length > 0 && result.sampleSuccess[0])
                        groundEcef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                            result.longitudeLatitudeHeightPositions[0]);
                }
            }

            if (requestId != _transitionRequestId)
                yield break;

            if (!groundEcef.HasValue)
            {
                // Never leave Grounded mode without a floor. When detailed
                // terrain is temporarily unavailable, use the WGS84 surface;
                // continuous grounding will refine to loaded photogrammetry.
                var llh = _navigation.LongitudeLatitudeHeight;
                groundEcef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                    new double3(llh.x, llh.y, 0d));
            }

            if (!groundEcef.HasValue)
            {
                _scaling.GroundedScalingEnabled = true;
                _scaling.InteractionsEnabled = true;
                _navigation.NavigationEnabled = true;
                _modeTransitionPending = false;
                yield break; // No ground found below at all, or a newer request superseded this one.
            }

            var solvedScale = SolveScaleForWorldHeight(groundEcef.Value, targetWorldY, startScale);
            if (solvedScale.HasValue)
            {
                Debug.Log($"EarthVR: Flight→Grounded scale solve: startScale={startScale:F3} targetFloorY={targetWorldY:F3} solvedScale={solvedScale.Value:F3}");
                BeginScaleTransition(solvedScale.Value);
            }
            else
            {
                Debug.LogWarning($"EarthVR: Flight→Grounded scale solve failed (startScale={startScale:F3} targetFloorY={targetWorldY:F3}); scale left unchanged.");
                _scaling.GroundedScalingEnabled = true;
                _scaling.InteractionsEnabled = true;
                _navigation.NavigationEnabled = true;
                _modeTransitionPending = false;
            }
        }

        /// <summary>Solves for the scale at which the fixed ECEF point
        /// <paramref name="ecef"/> maps to exactly <paramref name="targetWorldY"/>.
        /// A point's mapped world position is exactly linear in *1/scale*
        /// (not in scale itself) under any uniform scale-about-a-fixed-center
        /// transform, which is what this is — so two direct samples of the
        /// real Cesium scale-to-world mapping (not an assumed formula for its
        /// shape, only this one structural fact about it) solve for it exactly
        /// in closed form, no iteration. An earlier version searched in raw
        /// scale-space with a generic root-finder, which converges poorly
        /// against a relationship that is actually a hyperbola in scale — that
        /// was very likely the source of wrong (occasionally wildly wrong)
        /// results. Temporarily changes and restores the georeference scale to
        /// take the second sample; nothing is rendered mid-call, so this is
        /// not visible.</summary>
        private float? SolveScaleForWorldHeight(double3 ecef, float targetWorldY, float currentScale)
        {
            var minScale = _settings.minimumUserScale;
            var maxScale = _settings.maximumUserScale;
            var s0 = Mathf.Clamp(currentScale, minScale, maxScale);
            var s1 = Mathf.Clamp(s0 * 2f, minScale, maxScale);
            if (Mathf.Approximately(s1, s0))
                s1 = Mathf.Clamp(s0 * 0.5f, minScale, maxScale);
            if (Mathf.Approximately(s1, s0))
                return null; // Scale range too narrow to take a second, distinct sample.

            var previousGeoreferenceScale = _georeference.scale;
            _georeference.scale = ScaleMath.CesiumGlobeScale(s0);
            var y0 = EcefToWorld(ecef).y;
            _georeference.scale = ScaleMath.CesiumGlobeScale(s1);
            var y1 = EcefToWorld(ecef).y;
            _georeference.scale = previousGeoreferenceScale;

            var x0 = 1f / s0;
            var x1 = 1f / s1;
            var b = (y1 - y0) / (x1 - x0);
            var a = y0 - b * x0;

            var denominator = targetWorldY - a;
            if (Mathf.Abs(denominator) < 1e-6f)
                return null;

            var solved = b / denominator;
            if (float.IsNaN(solved) || float.IsInfinity(solved))
                return null;

            Debug.Log($"EarthVR: scale-solve calibration s0={s0:F3} y0={y0:F3} s1={s1:F3} y1={y1:F3} a={a:F3} b={b:F3} target={targetWorldY:F3} rawSolved={solved:F3}");
            return Mathf.Clamp(solved, minScale, maxScale);
        }

        private void BeginScaleTransition(float targetScale)
        {
            _modeTransitionPending = true;
            _scaleTransitionStart = _scaling.UserScale;
            _scaleTransitionTarget = targetScale;
            _scaleTransitionElapsed = 0f;
            _scaleTransitionActive = true;
        }

        /// <summary>Animates the mode-transition scale change over
        /// <see cref="EarthVRSettings.modeTransitionSeconds"/> instead of
        /// snapping instantly — a visible, felt sense of the world growing or
        /// shrinking around a fixed viewpoint, rather than an instant cut. Runs
        /// independently of the current mode, never touches Navigation Space's
        /// position, and yields immediately if the player
        /// starts grabbing the world, which takes priority over an in-progress
        /// transition.</summary>
        private void Update()
        {
            if (!_scaleTransitionActive)
                return;
            if (_scaling.IsDraggingEarth)
            {
                _scaleTransitionActive = false;
                _modeTransitionPending = false;
                _scaling.GroundedScalingEnabled = true;
                _scaling.InteractionsEnabled = true;
                _navigation.NavigationEnabled = true;
                return;
            }

            _scaleTransitionElapsed += Time.deltaTime;
            var duration = Mathf.Max(0.05f, _settings.modeTransitionSeconds);
            var t = Mathf.Clamp01(_scaleTransitionElapsed / duration);
            var eased = t * t * (3f - 2f * t);

            // Interpolating in log space makes the perceived rate of change
            // even across a large scale ratio — the same principle as the
            // grounded-scale gesture's doublings-per-second, rather than a
            // linear blend that would front-load most of the visual change
            // into the first instant of a big scale jump.
            var logStart = Mathf.Log(Mathf.Max(0.0001f, _scaleTransitionStart));
            var logTarget = Mathf.Log(Mathf.Max(0.0001f, _scaleTransitionTarget));
            _scaling.SetUserScaleKeepingObserverFixed(Mathf.Exp(Mathf.Lerp(logStart, logTarget, eased)));

            if (t >= 1f)
            {
                _scaleTransitionActive = false;
                _modeTransitionPending = false;
                _scaling.GroundedScalingEnabled = true;
                _scaling.InteractionsEnabled = true;
                _navigation.NavigationEnabled = true;
            }
        }

        private void LateUpdate()
        {
            if (_navigation == null || !IsSurfaceMode(_navigation.State.Mode))
                return;
            if (_modeTransitionPending || _scaleTransitionActive)
            {
                // The perspective-shift transition owns scale exclusively for
                // its short duration; terrain-following must not also move
                // position underneath it while the eyes are meant to be
                // completely still.
                _verticalVelocity = 0f;
                return;
            }
            if (_scaling.IsDraggingEarth || _scaling.IsScalingGrounded)
            {
                // Grab and manual resizing own the geographic foot anchor.
                // Resampling streamed rooftops during either gesture would
                // move that anchor and make scaling drift or jump.
                _verticalVelocity = 0f;
                return;
            }
            if (!TryComputeDesiredFloor(out var desiredFloor))
            {
                HasGroundSolution = false;
                return;
            }

            HasGroundSolution = true;
            var currentFloor = _rig.TrackingOrigin.position.y;
            // Ascending (a rooftop rising into view) still eases in, just over
            // a much shorter time than descending — fast enough that a single
            // step onto a curb or roof edge reads as immediate, but not a
            // literal one-frame teleport, so a skyline's rooftop/gap/rooftop
            // pattern at speed doesn't read as a jump cut every few metres.
            // Combined with the forward-looking sample in
            // TryComputeDesiredFloor, most of an upcoming rooftop's rise is
            // already anticipated before the camera is directly over it.
            var smoothSeconds = desiredFloor > currentFloor
                ? _settings.groundCorrectionAscendSeconds
                : _settings.groundCorrectionSeconds;
            var corrected = Mathf.SmoothDamp(
                currentFloor,
                desiredFloor,
                ref _verticalVelocity,
                Mathf.Max(0.01f, smoothSeconds),
                1f);
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
            var surfaceBelow = SampleSurfaceY(trackingOriginPosition, probeDistance);

            // A short lookahead sample along the current heading picks up an
            // upcoming rooftop before the camera is already over it, so the
            // floor has already started rising by the time it matters instead
            // of reacting only once directly on top of the new surface. Taking
            // the higher of the two samples is the same "never let a nearer
            // danger get masked by a farther clear reading" principle as the
            // Flight-mode ground-approach safety.
            float? surfaceAhead = null;
            var headingFlat = Vector3.ProjectOnPlane(_navigation.HorizontalTravelDirection, Vector3.up);
            if (headingFlat.sqrMagnitude > 0.0001f)
            {
                var lookaheadUnity = Mathf.Min(
                    probeDistance,
                    ScaleMath.GeographicToUnityMeters(_navigation.CurrentGeographicSpeed, scale) *
                    Mathf.Max(0f, _settings.groundedFloorLookaheadSeconds));
                if (lookaheadUnity > 0.01f)
                    surfaceAhead = SampleSurfaceY(trackingOriginPosition + headingFlat.normalized * lookaheadUnity, probeDistance);
            }

            float? surfaceY = surfaceBelow;
            if (surfaceAhead.HasValue)
                surfaceY = surfaceY.HasValue ? Mathf.Max(surfaceY.Value, surfaceAhead.Value) : surfaceAhead;

            if (!surfaceY.HasValue && _sampledSurfaceEcef.HasValue)
            {
                var sampledWorld = EcefToWorld(_sampledSurfaceEcef.Value);
                // An asynchronous query belongs to its original location. Hold
                // the current floor instead of applying an old rooftop elsewhere.
                if (Vector3.ProjectOnPlane(sampledWorld - trackingOriginPosition, Vector3.up).magnitude < 0.5f)
                    surfaceY = sampledWorld.y;
            }

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

        private float? SampleSurfaceY(Vector3 origin, float probeDistance)
        {
            var start = origin + Vector3.up * Mathf.Max(2f, probeDistance * 0.25f);
            if (TryRaycastTerrain(start, Mathf.Max(10f, probeDistance * 1.5f), out var hit))
                return hit.point.y;
            return null;
        }

        private bool TryRaycastTerrain(Vector3 start, float distance, out RaycastHit nearest)
        {
            nearest = default;
            if (_tileset == null)
                return false;
            var count = Physics.RaycastNonAlloc(start, Vector3.down, _groundHits,
                distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var nearestDistance = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.collider.GetComponentInParent<Cesium3DTileset>() != _tileset ||
                    hit.distance >= nearestDistance)
                    continue;
                nearest = hit;
                nearestDistance = hit.distance;
            }
            return nearestDistance < float.PositiveInfinity;
        }

        private IEnumerator SampleHeightLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(_settings.heightSampleIntervalSeconds);
                if (!IsSurfaceMode(_navigation.State.Mode) || _tileset == null || _tileset.suspendUpdate ||
                    _scaling.IsScalingGrounded)
                    continue;

                var llh = _navigation.LongitudeLatitudeHeight;
                var task = _tileset.SampleHeightMostDetailed(new double3(llh.x, llh.y, 0d));
                var timeoutAt = Time.unscaledTime + Mathf.Max(
                    _settings.modeGroundSampleTimeoutSeconds,
                    _settings.heightSampleIntervalSeconds);
                while (!task.IsCompleted && Time.unscaledTime < timeoutAt)
                    yield return null;
                if (!task.IsCompleted)
                    continue;
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

        private double3 WorldToEcef(Vector3 world)
        {
            var local = _georeference.transform.InverseTransformPoint(world);
            return _georeference.TransformUnityPositionToEarthCenteredEarthFixed(
                new double3(local.x, local.y, local.z));
        }

        private Vector3 EcefToWorld(double3 ecef)
        {
            var local = _georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            return _georeference.transform.TransformPoint(
                new Vector3((float)local.x, (float)local.y, (float)local.z));
        }

        private static bool IsSurfaceMode(MovementMode mode) =>
            mode == MovementMode.Grounded || mode == MovementMode.Car;
    }
}
