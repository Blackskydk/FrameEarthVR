using System;
using System.Collections;
using System.Collections.Generic;
using EarthVR.Configuration;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace EarthVR.Core
{
    /// <summary>
    /// Writes XR runtime facts and (development builds only) periodic performance
    /// lines to the Unity log, so a captured logcat shows which OpenXR extensions
    /// the runtime actually enabled (foveation, eye tracking) and how the display
    /// is configured. Lines start with "EarthVR-XR".
    /// </summary>
    public static class XrDiagnostics
    {
        private const string Tag = "EarthVR-XR";

        /// <summary>Logs the runtime, enabled OpenXR extensions and display state
        /// once the XR display is running.</summary>
        public static IEnumerator LogStartup()
        {
            var displays = new List<XRDisplaySubsystem>();
            var deadline = Time.unscaledTime + 30f;
            while (Time.unscaledTime < deadline)
            {
                SubsystemManager.GetSubsystems(displays);
                if (displays.Count > 0 && displays[0].running)
                    break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(3f);

            try
            {
                WriteStartupLog(displays);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{Tag} diagnostics failed: {exception.Message}");
            }
        }

        /// <summary>Development headset builds only: one line every few seconds
        /// with frame time, CPU/GPU split and render settings.</summary>
        public static IEnumerator LogPerformance(float intervalSeconds)
        {
            if (!Debug.isDebugBuild || Application.isEditor)
                yield break;

            var next = Time.unscaledTime + intervalSeconds;
            while (true)
            {
                RuntimeQuality.SampleFrameTiming();
                if (Time.unscaledTime >= next)
                {
                    next = Time.unscaledTime + intervalSeconds;
                    Debug.Log(
                        $"{Tag} perf frame={Time.unscaledDeltaTime * 1000f:0.0}ms " +
                        $"{RuntimeQuality.DescribeFrameTiming()} | {RuntimeQuality.Describe()}");
                }
                yield return null;
            }
        }

        private static void WriteStartupLog(List<XRDisplaySubsystem> displays)
        {
            Debug.Log(
                $"{Tag} build={Application.version} unity={Application.unityVersion} device={SystemInfo.deviceModel} " +
                $"gpu={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceVersion}");
            Debug.Log(
                $"{Tag} openxr runtime={OpenXRRuntime.name} version={OpenXRRuntime.version} " +
                $"api={OpenXRRuntime.apiVersion} plugin={OpenXRRuntime.pluginVersion}");

            var extensions = new List<string>(OpenXRRuntime.GetEnabledExtensions());
            Debug.Log($"{Tag} enabled extensions ({extensions.Count}): {string.Join(" ", extensions)}");

            for (var i = 0; i < displays.Count; i++)
            {
                var display = displays[i];
                object flags = null;
                try
                {
                    // Read by name so a missing property is reported, not a build error.
                    flags = typeof(XRDisplaySubsystem).GetProperty("foveatedRenderingFlags")?.GetValue(display, null);
                }
                catch (Exception)
                {
                }

                var description = XRSettings.eyeTextureDesc;
                Debug.Log(
                    $"{Tag} display[{i}] running={display.running} foveationLevel={display.foveatedRenderingLevel:0.00} " +
                    $"foveationFlags={(flags != null ? flags.ToString() : "n/a")} layout={display.textureLayout} " +
                    $"eye={description.width}x{description.height} depthBits={description.depthBufferBits} " +
                    $"msaa={description.msaaSamples} scale={XRSettings.eyeTextureResolutionScale:0.00}");
            }
        }
    }
}
