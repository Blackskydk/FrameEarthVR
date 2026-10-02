using System;
using System.Collections;
using System.Collections.Generic;
using EarthVR.Configuration;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;

namespace EarthVR.Core
{
    /// <summary>
    /// Writes XR runtime facts and periodic performance lines to the session log
    /// and the Unity log, so a capture shows which OpenXR extensions the runtime
    /// enabled (foveation, eye tracking), how the display is configured and how the
    /// frame is spent. Lines start with "EarthVR-XR".
    /// </summary>
    public static class XrDiagnostics
    {
        private const string Tag = "EarthVR-XR";
        private static ProfilerRecorder _triangles;
        private static ProfilerRecorder _drawCalls;
        private static ProfilerRecorder _batches;

        private static string DescribeRenderStats() => _triangles.Valid
            ? $"tris {_triangles.LastValue / 1000}k draws {_drawCalls.LastValue} batches {_batches.LastValue}"
            : "render stats n/a";

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
                SessionLog.Write($"{Tag} diagnostics failed: {exception.Message}");
            }
        }

        /// <summary>One line every few seconds: average and worst frame time over
        /// the window, the CPU/GPU split, render counters (development builds),
        /// Cesium collider warnings and where in the world the user is.</summary>
        public static IEnumerator LogPerformance(float intervalSeconds, Func<string> context)
        {
            if (Application.isEditor)
                yield break;

            if (Debug.isDebugBuild)
            {
                _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            }

            var next = Time.unscaledTime + intervalSeconds;
            var sum = 0f;
            var count = 0;
            var worst = 0f;
            while (true)
            {
                RuntimeQuality.SampleFrameTiming();
                var delta = Time.unscaledDeltaTime;
                sum += delta;
                count++;
                if (delta > worst)
                    worst = delta;

                if (Time.unscaledTime >= next)
                {
                    next = Time.unscaledTime + intervalSeconds;
                    var average = count > 0 ? sum / count : 0f;
                    string where;
                    try
                    {
                        where = context != null ? context() : string.Empty;
                    }
                    catch (Exception)
                    {
                        where = "position n/a";
                    }

                    SessionLog.Info(
                        $"{Tag} perf avg={average * 1000f:0.0}ms ({(average > 0f ? 1f / average : 0f):0} fps) " +
                        $"worst={worst * 1000f:0.0}ms | {RuntimeQuality.DescribeFrameTiming()} | " +
                        $"{DescribeRenderStats()} | colliderWarnings={SessionLog.TakeBakeWarnings()} | " +
                        $"{RuntimeQuality.Describe()} | {where}");
                    sum = 0f;
                    count = 0;
                    worst = 0f;
                }
                yield return null;
            }
        }

        private static void WriteStartupLog(List<XRDisplaySubsystem> displays)
        {
            SessionLog.Info(
                $"{Tag} device={SystemInfo.deviceModel} gpu={SystemInfo.graphicsDeviceName} " +
                $"api={SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceVersion}");
            SessionLog.Info(
                $"{Tag} openxr runtime={OpenXRRuntime.name} version={OpenXRRuntime.version} " +
                $"api={OpenXRRuntime.apiVersion} plugin={OpenXRRuntime.pluginVersion}");

            var extensions = new List<string>(OpenXRRuntime.GetEnabledExtensions());
            SessionLog.Info($"{Tag} enabled extensions ({extensions.Count}): {string.Join(" ", extensions)}");

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
                SessionLog.Info(
                    $"{Tag} display[{i}] running={display.running} foveationLevel={display.foveatedRenderingLevel:0.00} " +
                    $"foveationFlags={(flags != null ? flags.ToString() : "n/a")} layout={display.textureLayout} " +
                    $"eye={description.width}x{description.height} depthBits={description.depthBufferBits} " +
                    $"msaa={description.msaaSamples} scale={XRSettings.eyeTextureResolutionScale:0.00}");
            }
        }
    }
}
