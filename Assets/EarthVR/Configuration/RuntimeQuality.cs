using System.Collections;
using System.Collections.Generic;
using EarthVR.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace EarthVR.Configuration
{
    /// <summary>
    /// Applies the per-platform quality profile. URP reads MSAA and (in XR) the
    /// render scale from its pipeline asset, so the profile is written there
    /// as well as to QualitySettings / XRSettings. PC and headset share one
    /// asset; applying at startup is what lets them differ.
    /// </summary>
    public static class RuntimeQuality
    {
        public static int NormalizeMsaa(int requested)
        {
            if (requested >= 8) return 8;
            if (requested >= 4) return 4;
            if (requested >= 2) return 2;
            return 1;
        }

        public static float ClampRenderScale(float requested) => Mathf.Clamp(requested, 0.5f, 2f);

        public static void Apply(EarthVRSettings settings)
        {
            var mobile = Application.isMobilePlatform;
            var msaa = NormalizeMsaa(mobile ? settings.standaloneMsaa : settings.pcMsaa);
            var scale = ClampRenderScale(mobile ? settings.standaloneRenderScale : settings.pcRenderScale);

            var pipeline = UniversalRenderPipeline.asset;
            if (pipeline != null)
            {
                RememberEditorValues(pipeline);
                pipeline.msaaSampleCount = msaa;
                pipeline.renderScale = scale;
            }
            // Kept in step for the built-in paths; URP writes the same scale to
            // the XR display from its asset every frame.
            QualitySettings.antiAliasing = msaa > 1 ? msaa : 0;
            XRSettings.eyeTextureResolutionScale = scale;
        }

        public static int CurrentMsaa
        {
            get
            {
                var pipeline = UniversalRenderPipeline.asset;
                return pipeline != null ? pipeline.msaaSampleCount : Mathf.Max(1, QualitySettings.antiAliasing);
            }
        }

        /// <summary>Changes MSAA while running (for in-headset A/B testing).</summary>
        public static void SetMsaa(int samples)
        {
            var msaa = NormalizeMsaa(samples);
            var pipeline = UniversalRenderPipeline.asset;
            if (pipeline != null)
            {
                RememberEditorValues(pipeline);
                pipeline.msaaSampleCount = msaa;
            }
            QualitySettings.antiAliasing = msaa > 1 ? msaa : 0;
            SessionLog.Info($"EarthVR MSAA set to {msaa}x");
        }

        /// <summary>Whether eye-tracked foveation is allowed right now, or null when the flag cannot
        /// be read. Read by name so a missing property cannot break the build.</summary>
        public static bool? IsGazeAllowed()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            if (displays.Count == 0 || !displays[0].running)
                return null;
            try
            {
                var property = typeof(XRDisplaySubsystem).GetProperty("foveatedRenderingFlags");
                var value = property?.GetValue(displays[0], null);
                return value == null ? (bool?)null : value.ToString().Contains("GazeAllowed");
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>Allows or forbids eye-tracked foveation while running.</summary>
        public static void SetGazeAllowed(bool allowed)
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            try
            {
                var property = typeof(XRDisplaySubsystem).GetProperty("foveatedRenderingFlags");
                if (property == null)
                    return;
                var value = System.Enum.Parse(property.PropertyType, allowed ? "GazeAllowed" : "None");
                foreach (var display in displays)
                {
                    if (display != null && display.running)
                        property.SetValue(display, value, null);
                }
                SessionLog.Info($"EarthVR eye-tracked foveation {(allowed ? "on" : "off")}");
            }
            catch (System.Exception exception)
            {
                SessionLog.Write($"EarthVR eye-tracking toggle failed: {exception.Message}");
            }
        }

        /// <summary>The foveation level now in effect, or -1 when no XR display is running.</summary>
        public static float CurrentFoveationLevel()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            return displays.Count > 0 && displays[0].running ? displays[0].foveatedRenderingLevel : -1f;
        }

        /// <summary>Changes the foveation level while running (for in-headset A/B testing).</summary>
        public static void SetFoveationLevelNow(float level)
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            SetFoveationLevel(displays, Mathf.Clamp01(level));
        }

        /// <summary>Applies an optional foveation level after the XR display
        /// is running. Valve's startup feature sets the build-time level (0.5)
        /// itself, so a negative level leaves that untouched. The level is
        /// applied again a few seconds later in case that feature ran last.</summary>
        public static IEnumerator ApplyFoveationLevel(float level)
        {
            if (level < 0f)
                yield break;
            level = Mathf.Clamp01(level);

            var displays = new List<XRDisplaySubsystem>();
            var deadline = Time.unscaledTime + 30f;
            while (Time.unscaledTime < deadline)
            {
                SubsystemManager.GetSubsystems(displays);
                if (displays.Count > 0 && displays[0].running)
                    break;
                yield return null;
            }

            SetFoveationLevel(displays, level);
            yield return new WaitForSecondsRealtime(3f);
            SubsystemManager.GetSubsystems(displays);
            SetFoveationLevel(displays, level);
        }

        private static void SetFoveationLevel(List<XRDisplaySubsystem> displays, float level)
        {
            foreach (var display in displays)
            {
                if (display != null && display.running)
                    display.foveatedRenderingLevel = level;
            }
            SessionLog.Info($"EarthVR foveation level set to {level:0.00}");
        }

        private static readonly FrameTiming[] LatestTiming = new FrameTiming[1];
        private static float _smoothedCpuMain;
        private static float _smoothedCpuRender;
        private static float _smoothedGpu;
        private static bool _hasTiming;

        /// <summary>Call once per frame while the performance panel is open.
        /// Requires "Enable Frame Timing Stats" in Player Settings; the GPU
        /// figure is 0 on devices without GPU timestamp support.</summary>
        public static void SampleFrameTiming()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, LatestTiming) == 0)
                return;
            var timing = LatestTiming[0];
            if (!_hasTiming)
            {
                _smoothedCpuMain = (float)timing.cpuMainThreadFrameTime;
                _smoothedCpuRender = (float)timing.cpuRenderThreadFrameTime;
                _smoothedGpu = (float)timing.gpuFrameTime;
                _hasTiming = true;
                return;
            }
            _smoothedCpuMain = Mathf.Lerp(_smoothedCpuMain, (float)timing.cpuMainThreadFrameTime, 0.05f);
            _smoothedCpuRender = Mathf.Lerp(_smoothedCpuRender, (float)timing.cpuRenderThreadFrameTime, 0.05f);
            _smoothedGpu = Mathf.Lerp(_smoothedGpu, (float)timing.gpuFrameTime, 0.05f);
        }

        /// <summary>Which side limits the frame: a GPU time near the frame
        /// time means pixel/geometry bound; CPU main or render thread near it
        /// means tile processing or draw submission bound.</summary>
        public static string DescribeFrameTiming()
        {
            if (!_hasTiming)
                return "Frame timing: unavailable";
            var gpu = _smoothedGpu > 0.01f ? $"{_smoothedGpu:0.0}" : "n/a";
            return $"CPU {_smoothedCpuMain:0.0} · render {_smoothedCpuRender:0.0} · GPU {gpu} ms";
        }

        /// <summary>Bit depth of the XR eye depth buffer (16/24/32). Fixed-point
        /// 24-bit depth gives far coarser resolution at distance than float.</summary>
        public static string DescribeDepth()
        {
            var bits = XRSettings.eyeTextureDesc.depthBufferBits;
            return bits > 0 ? $"depth {bits}-bit" : "depth unknown";
        }

        /// <summary>One line for the in-headset performance panel showing what
        /// the renderer is actually using, so overrides can be confirmed. MSAA
        /// and scale are the values written to the URP asset, not a readback of
        /// what the GPU path does; the eye texture size is the real check.</summary>
        public static string Describe()
        {
            var pipeline = UniversalRenderPipeline.asset;
            var msaa = pipeline != null ? pipeline.msaaSampleCount : QualitySettings.antiAliasing;
            var scale = pipeline != null ? pipeline.renderScale : XRSettings.eyeTextureResolutionScale;
            var text = $"MSAA {msaa}x · scale {scale:0.00} · eye {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}";

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            if (displays.Count > 0 && displays[0].running)
                text += $" · fov {displays[0].foveatedRenderingLevel:0.00}";
            return text;
        }

#if UNITY_EDITOR
        // Editing the shared pipeline asset in the Editor would otherwise
        // outlive play mode, so remember its authored values and restore them.
        private static UniversalRenderPipelineAsset _editedAsset;
        private static int _originalMsaa;
        private static float _originalScale;
        private static bool _restoreRegistered;

        private static void RememberEditorValues(UniversalRenderPipelineAsset pipeline)
        {
            if (_editedAsset == null)
            {
                _editedAsset = pipeline;
                _originalMsaa = pipeline.msaaSampleCount;
                _originalScale = pipeline.renderScale;
            }
            if (_restoreRegistered)
                return;
            _restoreRegistered = true;
            Application.quitting += RestoreEditorValues;
        }

        private static void RestoreEditorValues()
        {
            Application.quitting -= RestoreEditorValues;
            _restoreRegistered = false;
            if (_editedAsset == null)
                return;
            _editedAsset.msaaSampleCount = _originalMsaa;
            _editedAsset.renderScale = _originalScale;
            _editedAsset = null;
        }
#else
        private static void RememberEditorValues(UniversalRenderPipelineAsset pipeline) { }
#endif
    }
}
