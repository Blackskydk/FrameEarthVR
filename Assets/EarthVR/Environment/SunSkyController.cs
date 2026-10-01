using System;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Input;
using EarthVR.Navigation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EarthVR.Sky
{
    public sealed class CelestialDragHandle : MonoBehaviour
    {
        public bool RepresentsMoon { get; private set; }

        public void Configure(bool representsMoon) => RepresentsMoon = representsMoon;
    }

    [DefaultExecutionOrder(-100)]
    public sealed class SunSkyController : MonoBehaviour
    {
        private const int MinutesPerDay = 1440;
        private const int PathStepMinutes = 15;
        private const float CelestialDistance = 500f;

        private IEarthVRInput _input;
        private EarthVRSettings _settings;
        private EarthVRRig _rig;
        private NavigationController _navigation;
        private Light _sunLight;
        private Light _moonLight;
        private Material _skyMaterial;
        private Material _sunMaterial;
        private Material _moonMaterial;
        private Material _pathMaterial;
        private Transform _sunHandle;
        private Transform _moonHandle;
        private Transform _pathRoot;
        private LineRenderer _sunPath;
        private Transform _dragHand;
        private bool _draggingMoon;
        private DateTime _utcTime;
        private DateTime[] _solarPathTimes;
        private Vector3[] _solarPathDirections;
        private DateTime _pathDate;
        private double _pathLatitude = double.NaN;
        private double _pathLongitude = double.NaN;
        private Vector3 _sunDirection = Vector3.up;
        private float _sunElevation;
        private bool _pathRepresentsMoon;
        private float _pathVisibility;
        private bool _overviewSunOverride;
        private Vector3 _overviewSunDirection = Vector3.up;
        private readonly Gradient _pathGradient = new();
        private readonly GradientColorKey[] _pathColorKeys =
        {
            new(Color.white, 0f),
            new(Color.white, 1f)
        };
        private readonly GradientAlphaKey[] _pathAlphaKeys = new GradientAlphaKey[8];
        private VolumeProfile _volumeProfile;
        private ColorAdjustments _colorAdjustments;
        private Bloom _bloom;

        public DateTime UtcTime => _utcTime;
        public float SunElevationDegrees => _sunElevation;
        public bool IsDraggingSun => _dragHand != null;

        public void Initialize(
            IEarthVRInput input,
            EarthVRSettings settings,
            EarthVRRig rig,
            NavigationController navigation)
        {
            _input = input;
            _settings = settings;
            _rig = rig;
            _navigation = navigation;
            // Start new sessions in useful daylight instead of inheriting the
            // computer clock (which made evening launches begin in darkness).
            _utcTime = FindSolarNoonUtc(
                DateTime.UtcNow.Date,
                settings.startLatitude,
                settings.startLongitude);

            CreateLights();
            CreateSkybox();
            CreatePostProcessing();
            CreateCelestialHandles();
            CreateSunPath();
            _rig.Camera.clearFlags = CameraClearFlags.Skybox;
            ApplyEnvironment();
        }

        private static DateTime FindSolarNoonUtc(DateTime date, double latitude, double longitude)
        {
            var bestTime = DateTime.SpecifyKind(date.AddHours(12d), DateTimeKind.Utc);
            var bestElevation = double.NegativeInfinity;
            for (var minute = 0; minute < MinutesPerDay; minute += 5)
            {
                var candidate = DateTime.SpecifyKind(date.AddMinutes(minute), DateTimeKind.Utc);
                var elevation = SolarPositionCalculator.Calculate(candidate, latitude, longitude)
                    .ElevationDegrees;
                if (elevation <= bestElevation)
                    continue;
                bestElevation = elevation;
                bestTime = candidate;
            }
            return bestTime;
        }

        /// <summary>Returns local solar noon for the requested date. During
        /// polar night, where even noon is dark, it moves to the same year's
        /// summer solstice so every arrival has useful daylight.</summary>
        public static DateTime CalculateDaylightArrivalUtc(
            DateTime date,
            double longitude,
            double latitude)
        {
            var utcDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
            var solarNoon = FindSolarNoonUtc(utcDate, latitude, longitude);
            var elevation = SolarPositionCalculator.Calculate(
                solarNoon,
                latitude,
                longitude).ElevationDegrees;
            if (elevation >= 5d)
                return solarNoon;

            var summerMonth = latitude >= 0d ? 6 : 12;
            var summerSolstice = new DateTime(
                utcDate.Year,
                summerMonth,
                21,
                0,
                0,
                0,
                DateTimeKind.Utc);
            return FindSolarNoonUtc(summerSolstice, latitude, longitude);
        }

        public bool IsConsumingTrigger(Transform hand) => _dragHand == hand;

        /// <summary>Temporarily places the Sun behind the viewer so the visible
        /// side of the tilted Earth is always illuminated. Astronomical time is
        /// not changed and resumes immediately when the override is cleared.</summary>
        public void SetOverviewSunDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.000001f)
                return;
            _overviewSunOverride = true;
            _overviewSunDirection = direction.normalized;
            ApplyEnvironment();
        }

        public void ClearOverviewSunDirection()
        {
            if (!_overviewSunOverride)
                return;
            _overviewSunOverride = false;
            ApplyEnvironment();
        }

        public void SetUtcTime(DateTime utcTime)
        {
            _utcTime = utcTime.Kind == DateTimeKind.Utc ? utcTime : utcTime.ToUniversalTime();
            _pathDate = default;
            ApplyEnvironment();
        }

        public void SetDaylightForLocation(double longitude, double latitude)
        {
            SetUtcTime(CalculateDaylightArrivalUtc(
                _utcTime == default ? DateTime.UtcNow : _utcTime,
                longitude,
                latitude));
        }

        public void RefreshForReferenceFrameChange()
        {
            _pathLatitude = double.NaN;
            EnsureSolarPath();
            ApplyEnvironment();
        }

        private void Update()
        {
            if (_input == null || _rig == null || _navigation == null)
                return;

            EnsureSolarPath();
            var triggerHand = ActiveTriggerHand();
            if (_dragHand != null)
            {
                if (triggerHand != _dragHand)
                    _dragHand = null;
                else
                    DragCelestialBodyAlongSolarPath(_dragHand);
            }
            else if (triggerHand != null && TryGetPointedHandle(triggerHand, out var handle))
            {
                _dragHand = triggerHand;
                _draggingMoon = handle.RepresentsMoon;
                DragCelestialBodyAlongSolarPath(_dragHand);
            }
            else
            {
                _utcTime = _utcTime.AddSeconds(Time.unscaledDeltaTime);
            }

            ApplyEnvironment();
        }

        private void LateUpdate()
        {
            if (_rig == null)
                return;

            if (_pathRoot != null)
            {
                _pathRoot.position = _rig.Camera.transform.position;
                _pathRoot.rotation = Quaternion.identity;
            }

            var nightAmount = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, -7f, _sunElevation));
            UpdatePathPresentation(nightAmount);
            var sunVisible = _sunElevation > -5f || (_dragHand != null && !_draggingMoon);
            var moonVisible = nightAmount > 0.02f || (_dragHand != null && _draggingMoon);

            if (_sunHandle != null)
            {
                _sunHandle.gameObject.SetActive(sunVisible);
                _sunHandle.position = _rig.Camera.transform.position + _sunDirection * CelestialDistance;
                var sunAim = ControllerAimAmount(_sunDirection);
                _sunHandle.localScale = Vector3.one * Mathf.Lerp(5f, 8.5f, sunAim);
            }

            if (_moonHandle != null)
            {
                var moonDirection = -_sunDirection;
                _moonHandle.gameObject.SetActive(moonVisible && moonDirection.y > -0.08f);
                _moonHandle.position = _rig.Camera.transform.position + moonDirection * CelestialDistance;
                var moonAim = ControllerAimAmount(moonDirection);
                _moonHandle.localScale = Vector3.one * Mathf.Lerp(7f, 12f, moonAim);
            }
        }

        private float ControllerAimAmount(Vector3 bodyDirection)
        {
            var leftAngle = Vector3.Angle(_rig.LeftController.forward, bodyDirection);
            var rightAngle = Vector3.Angle(_rig.RightController.forward, bodyDirection);
            var closestAngle = Mathf.Min(leftAngle, rightAngle);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.6f, 0.45f, closestAngle));
        }

        private Transform ActiveTriggerHand()
        {
            if (_input.RightTriggerHeld)
                return _rig.RightController;
            return _input.LeftTriggerHeld ? _rig.LeftController : null;
        }

        private bool TryGetPointedHandle(Transform hand, out CelestialDragHandle handle)
        {
            handle = null;
            if (!Physics.Raycast(
                    hand.position,
                    hand.forward,
                    out var hit,
                    _settings.pointerMaximumLengthMeters,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
                return false;

            handle = hit.collider.GetComponent<CelestialDragHandle>();
            return handle != null;
        }

        private void EnsureSolarPath()
        {
            var llh = _navigation.LongitudeLatitudeHeight;
            var date = _utcTime.Date;
            if (_solarPathDirections != null &&
                date == _pathDate &&
                Math.Abs(llh.y - _pathLatitude) < 0.05d &&
                Math.Abs(llh.x - _pathLongitude) < 0.05d)
                return;

            BuildSolarPath(date, llh.y, llh.x);
        }

        private void BuildSolarPath(DateTime date, double latitude, double longitude)
        {
            _pathDate = date;
            _pathLatitude = latitude;
            _pathLongitude = longitude;
            _solarPathTimes = new DateTime[MinutesPerDay];
            _solarPathDirections = new Vector3[MinutesPerDay];
            for (var minute = 0; minute < MinutesPerDay; minute++)
            {
                var time = DateTime.SpecifyKind(date.AddMinutes(minute), DateTimeKind.Utc);
                _solarPathTimes[minute] = time;
                _solarPathDirections[minute] = SolarPositionCalculator
                    .Calculate(time, latitude, longitude)
                    .ToUnityDirection();
            }

            RebuildVisiblePath(_pathRepresentsMoon);
        }

        private void RebuildVisiblePath(bool representsMoon)
        {
            if (_sunPath == null || _solarPathDirections == null)
                return;

            _pathRepresentsMoon = representsMoon;
            var sampleCount = MinutesPerDay / PathStepMinutes;
            var aboveCount = 0;
            var firstRise = -1;
            for (var i = 0; i < sampleCount; i++)
            {
                var currentAbove = DisplayPathDirection(i, representsMoon).y >= 0f;
                if (currentAbove)
                    aboveCount++;
                var previous = (i + sampleCount - 1) % sampleCount;
                if (currentAbove && DisplayPathDirection(previous, representsMoon).y < 0f)
                    firstRise = i;
            }

            if (aboveCount == 0)
            {
                _sunPath.positionCount = 0;
                _sunPath.enabled = false;
                return;
            }

            _sunPath.enabled = true;
            _sunPath.loop = aboveCount == sampleCount;
            _sunPath.positionCount = aboveCount;
            var start = firstRise >= 0 ? firstRise : 0;
            for (var point = 0; point < aboveCount; point++)
            {
                var index = (start + point) % sampleCount;
                _sunPath.SetPosition(point, DisplayPathDirection(index, representsMoon) * CelestialDistance);
            }
        }

        private Vector3 DisplayPathDirection(int sampleIndex, bool representsMoon)
        {
            var direction = _solarPathDirections[sampleIndex * PathStepMinutes];
            return representsMoon ? -direction : direction;
        }

        private void UpdatePathPresentation(float nightAmount)
        {
            if (_sunPath == null || _solarPathDirections == null)
                return;

            var shouldShowMoonPath = nightAmount > 0.55f;
            if (shouldShowMoonPath != _pathRepresentsMoon)
                RebuildVisiblePath(shouldShowMoonPath);
            if (!_sunPath.enabled || _sunPath.positionCount == 0)
                return;

            var activeBodyDirection = _pathRepresentsMoon ? -_sunDirection : _sunDirection;
            var bodyGazeDot = Vector3.Dot(_rig.Camera.transform.forward, activeBodyDirection);

            var looking = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    Mathf.Cos(10f * Mathf.Deg2Rad),
                    Mathf.Cos(3f * Mathf.Deg2Rad),
                    bodyGazeDot));
            if (_dragHand != null)
                looking = 1f;

            var targetVisibility = 0.98f * looking;
            var fade = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.12f);
            _pathVisibility = Mathf.Lerp(_pathVisibility, targetVisibility, fade);

            var closestBodyPoint = 0;
            var closestBodyDot = -1f;
            for (var i = 0; i < _sunPath.positionCount; i++)
            {
                var dot = Vector3.Dot(activeBodyDirection, _sunPath.GetPosition(i).normalized);
                if (dot <= closestBodyDot)
                    continue;
                closestBodyDot = dot;
                closestBodyPoint = i;
            }

            var bodyPosition = _sunPath.positionCount <= 1
                ? 0.5f
                : closestBodyPoint / (float)(_sunPath.positionCount - 1);
            for (var i = 0; i < _pathAlphaKeys.Length; i++)
            {
                var position = i / (float)(_pathAlphaKeys.Length - 1);
                var distance = Mathf.Abs(position - bodyPosition);
                if (_sunPath.loop)
                    distance = Mathf.Min(distance, 1f - distance);
                var nearBody = Mathf.Exp(-0.5f * Mathf.Pow(distance / 0.16f, 2f));
                _pathAlphaKeys[i] = new GradientAlphaKey(
                    _pathVisibility * Mathf.Lerp(0.015f, 1f, nearBody),
                    position);
            }
            _pathGradient.SetKeys(_pathColorKeys, _pathAlphaKeys);
            _sunPath.colorGradient = _pathGradient;
            _sunPath.widthMultiplier = Mathf.Lerp(0.72f, 1.12f, looking);
        }

        private void DragCelestialBodyAlongSolarPath(Transform hand)
        {
            EnsureSolarPath();
            var desiredSunDirection = (_draggingMoon ? -hand.forward : hand.forward).normalized;
            var bestIndex = 0;
            var bestDot = float.NegativeInfinity;
            for (var i = 0; i < _solarPathDirections.Length; i++)
            {
                var dot = Vector3.Dot(desiredSunDirection, _solarPathDirections[i]);
                if (dot <= bestDot)
                    continue;
                bestDot = dot;
                bestIndex = i;
            }
            _utcTime = _solarPathTimes[bestIndex];
        }

        private void ApplyEnvironment()
        {
            var llh = _navigation.LongitudeLatitudeHeight;
            var solar = SolarPositionCalculator.Calculate(_utcTime, llh.y, llh.x);
            _sunDirection = _overviewSunOverride
                ? _overviewSunDirection
                : solar.ToUnityDirection();
            _sunElevation = _overviewSunOverride
                ? 90f
                : (float)solar.ElevationDegrees;
            var moonDirection = -_sunDirection;

            var daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-8f, 6f, _sunElevation));
            var highSun = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 35f, _sunElevation));
            var sunset = Mathf.Clamp01(1f - Mathf.Abs(_sunElevation - 1f) / 17f) * daylight;
            var night = 1f - daylight;

            _sunLight.transform.rotation = Quaternion.LookRotation(-_sunDirection, Vector3.up);
            _sunLight.intensity = 1.25f * daylight;
            _sunLight.color = Color.Lerp(
                new Color(1f, 0.20f, 0.055f),
                new Color(1f, 0.97f, 0.88f),
                highSun);
            _sunLight.shadows = _settings.SunShadowsEnabled && daylight > 0.04f
                ? LightShadows.Soft
                : LightShadows.None;

            _moonLight.transform.rotation = Quaternion.LookRotation(-moonDirection, Vector3.up);
            _moonLight.intensity = 0.075f * night;
            _moonLight.color = new Color(0.40f, 0.50f, 0.72f);
            _moonLight.shadows = LightShadows.None;

            var nightAmbient = new Color(0.0015f, 0.0025f, 0.008f);
            var dayAmbient = new Color(0.48f, 0.52f, 0.60f);
            var sunsetAmbient = new Color(0.30f, 0.065f, 0.018f) * sunset;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(nightAmbient, dayAmbient, daylight) + sunsetAmbient;
            RenderSettings.ambientIntensity = Mathf.Lerp(0.08f, 1f, daylight);
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.04f, 1f, daylight);
            RenderSettings.sun = _sunLight;

            if (_settings.FlatTileLightingEnabled)
            {
                // Flat, direction-free ambient only: tiles show their own baked
                // lighting instead of being re-lit by the sun (no facet shading,
                // specular or sun shadows). Night still dims the scene.
                _sunLight.intensity = 0f;
                _sunLight.shadows = LightShadows.None;
                _moonLight.intensity = 0f;
                RenderSettings.ambientLight = Color.white * Mathf.Lerp(0.10f, 1f, daylight);
                RenderSettings.ambientIntensity = 1f;
                RenderSettings.reflectionIntensity = 0f;
            }

            if (_colorAdjustments != null)
            {
                // Keep nighttime controls and celestial handles readable; the sky,
                // ambient light and terrain lighting still provide the darkness.
                _colorAdjustments.postExposure.value = Mathf.Lerp(-1.8f, 0f, daylight);
                var baseFilter = Color.Lerp(new Color(0.30f, 0.38f, 0.62f), Color.white, daylight);
                _colorAdjustments.colorFilter.value = Color.Lerp(
                    baseFilter,
                    new Color(1f, 0.67f, 0.43f),
                    sunset * 0.72f);
                _colorAdjustments.saturation.value = Mathf.Lerp(-28f, 0f, daylight);
                _colorAdjustments.contrast.value = Mathf.Lerp(18f, 5f, daylight);
            }
            if (_bloom != null)
                _bloom.intensity.value = Mathf.Lerp(0.22f, 0.48f, sunset);

            if (_skyMaterial != null)
            {
                _skyMaterial.SetVector("_SunDirection", ToVector4(_sunDirection));
                _skyMaterial.SetVector("_MoonDirection", ToVector4(moonDirection));
                _skyMaterial.SetColor("_SunColor", _sunLight.color);
                _skyMaterial.SetFloat("_DayAmount", daylight);
                _skyMaterial.SetFloat("_NightAmount", night);
                _skyMaterial.SetFloat("_HorizonWarmth", sunset);
            }

            if (_sunMaterial != null)
                SetMaterialColor(_sunMaterial, Color.Lerp(new Color(1f, 0.15f, 0.02f), new Color(1f, 0.93f, 0.58f), daylight));
            if (_moonMaterial != null)
                SetMaterialColor(_moonMaterial, new Color(0.66f, 0.75f, 0.92f));
        }

        private void CreateLights()
        {
            _sunLight = CreateDirectionalLight("Astronomical Directional Sun", new Color(1f, 0.95f, 0.85f));
            _moonLight = CreateDirectionalLight("Moon Fill Light", new Color(0.4f, 0.5f, 0.72f));
        }

        private Light CreateDirectionalLight(string objectName, Color color)
        {
            var lightObject = new GameObject(objectName, typeof(Light));
            lightObject.transform.SetParent(transform, false);
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.shadows = LightShadows.None;
            return light;
        }

        private void CreateSkybox()
        {
            var shader = Resources.Load<Shader>("EarthVRProceduralSky");
            if (shader == null)
                shader = Shader.Find("EarthVR/ProceduralSky");
            if (shader == null)
            {
                Debug.LogError("EarthVR procedural sky shader was not found.");
                return;
            }
            _skyMaterial = new Material(shader) { name = "EarthVR Dynamic Sky (Runtime)" };
            RenderSettings.skybox = _skyMaterial;
        }

        private void CreatePostProcessing()
        {
            var cameraData = _rig.Camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            var volumeObject = new GameObject("EarthVR Day Night Color Grade", typeof(Volume));
            volumeObject.transform.SetParent(transform, false);
            var volume = volumeObject.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            _volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = _volumeProfile;
            _colorAdjustments = _volumeProfile.Add<ColorAdjustments>(true);
            _colorAdjustments.postExposure.Override(0f);
            _colorAdjustments.colorFilter.Override(Color.white);
            _colorAdjustments.saturation.Override(0f);
            _colorAdjustments.contrast.Override(0f);
            // Bloom is skipped when disabled (headset default): in this LDR
            // pipeline its 1.1 threshold is above the brightest output, so it
            // costs several full-screen passes for almost no visible change.
            if (!_settings.BloomEnabled)
                return;
            _bloom = _volumeProfile.Add<Bloom>(true);
            _bloom.threshold.Override(1.1f);
            _bloom.intensity.Override(0.25f);
            _bloom.scatter.Override(0.65f);
        }

        private void CreateCelestialHandles()
        {
            _sunHandle = CreateCelestialHandle("Draggable Sun", false, new Color(1f, 0.85f, 0.38f), out _sunMaterial);
            _moonHandle = CreateCelestialHandle("Draggable Moon", true, new Color(0.66f, 0.75f, 0.92f), out _moonMaterial);
        }

        private Transform CreateCelestialHandle(string objectName, bool moon, Color color, out Material material)
        {
            var handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handle.name = objectName;
            handle.transform.SetParent(transform, false);
            handle.AddComponent<CelestialDragHandle>().Configure(moon);
            // A slightly larger invisible hit area makes distant celestial bodies
            // selectable without visually inflating them all the time.
            handle.GetComponent<SphereCollider>().radius = 1f;
            var renderer = handle.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var shader = moon ? Resources.Load<Shader>("EarthVRMoon") : null;
            if (shader == null && moon)
                shader = Shader.Find("EarthVR/Moon");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            material = shader == null ? null : new Material(shader) { name = objectName + " Material" };
            if (material != null)
            {
                SetMaterialColor(material, color);
                renderer.sharedMaterial = material;
            }
            return handle.transform;
        }

        private void CreateSunPath()
        {
            _pathRoot = new GameObject("Solar Path Root").transform;
            _pathRoot.SetParent(transform, false);
            var pathObject = new GameObject("Sun Daily Path", typeof(LineRenderer));
            pathObject.transform.SetParent(_pathRoot, false);
            _sunPath = pathObject.GetComponent<LineRenderer>();
            _sunPath.useWorldSpace = false;
            _sunPath.loop = false;
            // This is a sky-scale guide, not a thin world-space wire. At 500 m,
            // a sub-metre line is effectively invisible in a headset.
            _sunPath.startWidth = 1.8f;
            _sunPath.endWidth = 1.8f;
            _sunPath.numCornerVertices = 6;
            _sunPath.numCapVertices = 4;
            _sunPath.alignment = LineAlignment.View;
            _sunPath.textureMode = LineTextureMode.Stretch;
            _sunPath.shadowCastingMode = ShadowCastingMode.Off;
            _sunPath.receiveShadows = false;
            _sunPath.sortingOrder = 20;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                _pathMaterial = new Material(shader) { name = "Solar Path Material" };
                SetMaterialColor(_pathMaterial, Color.white);
                _pathMaterial.renderQueue = (int)RenderQueue.Transparent;
                _sunPath.sharedMaterial = _pathMaterial;
            }
            _sunPath.startColor = new Color(1f, 1f, 1f, 0f);
            _sunPath.endColor = new Color(1f, 1f, 1f, 0f);
        }

        private static Vector4 ToVector4(Vector3 direction) =>
            new(direction.x, direction.y, direction.z, 0f);

        private static void SetMaterialColor(Material material, Color color)
        {
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
        }

        private void OnDestroy()
        {
            if (_skyMaterial != null)
                Destroy(_skyMaterial);
            if (_sunMaterial != null)
                Destroy(_sunMaterial);
            if (_moonMaterial != null)
                Destroy(_moonMaterial);
            if (_pathMaterial != null)
                Destroy(_pathMaterial);
            if (_volumeProfile != null)
                Destroy(_volumeProfile);
        }
    }
}
