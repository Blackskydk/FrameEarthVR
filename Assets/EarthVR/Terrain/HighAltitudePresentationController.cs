using System;
using EarthVR.Configuration;
using EarthVR.Navigation;
using EarthVR.Scaling;
using EarthVR.Sky;
using UnityEngine;

namespace EarthVR.Terrain
{
    /// <summary>Blends the geometric horizon into the current sky as altitude
    /// increases.</summary>
    /// <remarks>This must not adjust Cesium3DTileset properties such as
    /// maximumScreenSpaceError at runtime. Every Cesium for Unity tileset
    /// property setter calls RecreateTileset(), which discards all loaded tiles
    /// and requests the root again, so a per-frame change keeps the tileset
    /// permanently at 0%. Cesium's distance-based screen-space error already
    /// selects coarser tiles as altitude increases.</remarks>
    public sealed class HighAltitudePresentationController : MonoBehaviour
    {
        public const double EarthRadiusMeters = 6378137d;

        private EarthVRSettings _settings;
        private NavigationController _navigation;
        private WorldManipulationController _scaling;
        private SunSkyController _sunSky;

        public void Initialize(
            EarthVRSettings settings,
            NavigationController navigation,
            WorldManipulationController scaling,
            SunSkyController sunSky)
        {
            _settings = settings;
            _navigation = navigation;
            _scaling = scaling;
            _sunSky = sunSky;
        }

        private void Update()
        {
            if (_settings == null || _navigation == null || _scaling == null)
                return;

            UpdateHorizonFog(Mathf.Max(0f, _navigation.AltitudeMeters));
        }

        private void UpdateHorizonFog(float altitudeMeters)
        {
            if (altitudeMeters < _settings.horizonFogStartAltitudeMeters)
            {
                RenderSettings.fog = false;
                return;
            }

            var horizonMeters = ComputeGeometricHorizonDistance(altitudeMeters);
            var startMeters = horizonMeters * _settings.horizonFogStartFraction;
            var endMeters = Mathf.Max(
                startMeters + 1000f,
                horizonMeters * _settings.horizonFogEndFraction);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = ScaleMath.GeographicToUnityMeters(
                startMeters,
                _scaling.UserScale);
            RenderSettings.fogEndDistance = ScaleMath.GeographicToUnityMeters(
                endMeters,
                _scaling.UserScale);

            var sunElevation = _sunSky == null ? 20f : _sunSky.SunElevationDegrees;
            var daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-8f, 8f, sunElevation));
            var sunset = Mathf.Clamp01(1f - Mathf.Abs(sunElevation - 1f) / 16f) * daylight;
            var nightColor = new Color(0.003f, 0.007f, 0.018f);
            var dayColor = new Color(0.32f, 0.53f, 0.68f);
            var sunsetColor = new Color(0.55f, 0.18f, 0.07f);
            RenderSettings.fogColor = Color.Lerp(nightColor, dayColor, daylight) + sunsetColor * (sunset * 0.35f);
        }

        public static float ComputeGeometricHorizonDistance(float altitudeMeters)
        {
            var altitude = Math.Max(0d, altitudeMeters);
            return (float)Math.Sqrt(altitude * (2d * EarthRadiusMeters + altitude));
        }

        private void OnDisable()
        {
            RenderSettings.fog = false;
        }
    }
}
