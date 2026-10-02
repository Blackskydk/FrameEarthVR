using System.Collections;
using System.Collections.Generic;
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
            var msaa = NormalizeMsaa(
                UserQualityPreferences.GetInt(UserQualityPreferences.Msaa) ??
                (mobile ? settings.standaloneMsaa : settings.pcMsaa));
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

        /// <summary>Changes MSAA while running.</summary>
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
            SetGazeAllowed(displays, allowed);
        }

        /// <summary>The foveation level now in effect, or -1 when no XR display is running.</summary>
        public static float CurrentFoveationLevel()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            return displays.Count > 0 && displays[0].running ? displays[0].foveatedRenderingLevel : -1f;
        }

        /// <summary>Changes the foveation level while running (0 turns foveation off).</summary>
        public static void SetFoveationLevelNow(float level)
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            SetFoveationLevel(displays, Mathf.Clamp01(level));
        }

        /// <summary>Applies the headset's foveation once the XR display is running: the level
        /// and eye tracking chosen on the settings page, else the settings' level (a negative
        /// level leaves the build-time one untouched). Valve's startup feature sets its own
        /// values, so everything is applied again a few seconds later in case it ran last.</summary>
        public static IEnumerator ApplyFoveation(EarthVRSettings settings)
        {
            var level = UserQualityPreferences.GetFloat(UserQualityPreferences.FoveationLevel) ??
                        settings.standaloneFoveationLevelOverride;
            var gaze = UserQualityPreferences.GetBool(UserQualityPreferences.EyeTracking);
            if (level < 0f && gaze == null)
                yield break;

            var displays = new List<XRDisplaySubsystem>();
            var deadline = Time.unscaledTime + 30f;
            while (Time.unscaledTime < deadline)
            {
                SubsystemManager.GetSubsystems(displays);
                if (displays.Count > 0 && displays[0].running)
                    break;
                yield return null;
            }

            ApplyFoveation(displays, level, gaze);
            yield return new WaitForSecondsRealtime(3f);
            SubsystemManager.GetSubsystems(displays);
            ApplyFoveation(displays, level, gaze);
        }

        private static void ApplyFoveation(List<XRDisplaySubsystem> displays, float level, bool? gaze)
        {
            if (level >= 0f)
                SetFoveationLevel(displays, Mathf.Clamp01(level));
            if (gaze.HasValue)
                SetGazeAllowed(displays, gaze.Value);
        }

        private static void SetFoveationLevel(List<XRDisplaySubsystem> displays, float level)
        {
            foreach (var display in displays)
            {
                if (display != null && display.running)
                    display.foveatedRenderingLevel = level;
            }
        }

        private static void SetGazeAllowed(List<XRDisplaySubsystem> displays, bool allowed)
        {
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
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"EarthVR: could not change eye-tracked foveation: {exception.Message}");
            }
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
