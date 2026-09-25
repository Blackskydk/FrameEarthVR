using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Input;
using EarthVR.Navigation;
using UnityEngine;

namespace EarthVR.Sound
{
    /// <summary>
    /// Generates a continuous, non-spatial wind bed at runtime. Volume and pitch
    /// follow the viewer's physical flight speed, so it conveys motion without an
    /// external audio asset or a world-fixed sound source.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class FlightAudioController : MonoBehaviour
    {
        private const int SampleRate = 48000;
        private IEarthVRInput _input;
        private EarthVRSettings _settings;
        private NavigationController _navigation;
        private AudioSource _source;
        private AudioClip _windClip;
        private float _currentVolume;
        private uint _noiseState = 0xA341316Cu;
        private float _slowNoise;
        private float _fastNoise;
        private float _gustPhase;

        public void Initialize(
            IEarthVRInput input,
            EarthVRSettings settings,
            EarthVRRig rig,
            NavigationController navigation)
        {
            _input = input;
            _settings = settings;
            _navigation = navigation;

            var audioObject = new GameObject("Soaring Wind Audio", typeof(AudioSource));
            audioObject.transform.SetParent(rig.Camera.transform, false);
            _source = audioObject.GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 0f;
            _source.dopplerLevel = 0f;
            _source.volume = 0f;
            _source.pitch = 0.8f;

            _windClip = AudioClip.Create(
                "EarthVR Procedural Soaring Wind",
                SampleRate * 2,
                1,
                SampleRate,
                true,
                FillWindSamples);
            _source.clip = _windClip;
            _source.Play();
        }

        private void Update()
        {
            if (_source == null || _navigation == null || _input == null)
                return;

            var flying = _navigation.State.Mode == MovementMode.Flight &&
                         _navigation.IsActivelyMoving;
            var speed01 = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    0.4f,
                    Mathf.Max(0.5f, _settings.soaringWindFullSpeedMetersPerSecond),
                    _navigation.PhysicalSpeedMetersPerSecond));
            var intent = Mathf.Clamp01(_input.Fly.magnitude);
            var targetVolume = flying
                ? _settings.soaringWindMaximumVolume * Mathf.Lerp(0.16f, 1f, speed01) * intent
                : 0f;
            var response = Mathf.Max(0.01f, _settings.soaringWindResponseSeconds);
            var blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / response);
            _currentVolume = Mathf.Lerp(_currentVolume, targetVolume, blend);
            _source.volume = _currentVolume;
            _source.pitch = Mathf.Lerp(0.76f, 1.38f, speed01);
        }

        private void FillWindSamples(float[] samples)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                _noiseState = _noiseState * 1664525u + 1013904223u;
                var white = ((_noiseState >> 8) / 8388607.5f) - 1f;

                // Two filters form a broad, soft band of turbulent air. A slow
                // sinusoidal envelope supplies natural gusts without obvious looping.
                _slowNoise += (white - _slowNoise) * 0.0045f;
                _fastNoise += (white - _fastNoise) * 0.075f;
                var windBand = _fastNoise - _slowNoise;
                _gustPhase += 2f * Mathf.PI * 0.17f / SampleRate;
                if (_gustPhase > 2f * Mathf.PI)
                    _gustPhase -= 2f * Mathf.PI;
                var gust = 0.76f + 0.24f * Mathf.Sin(_gustPhase);
                samples[i] = Mathf.Clamp(windBand * gust * 0.72f + white * 0.025f, -1f, 1f);
            }
        }

        private void OnDestroy()
        {
            if (_source != null)
                _source.Stop();
            if (_windClip != null)
                Destroy(_windClip);
        }
    }
}
