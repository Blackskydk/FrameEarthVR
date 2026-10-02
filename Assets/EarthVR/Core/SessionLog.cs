using System;
using System.IO;
using System.Threading;
using EarthVR.Configuration;
using UnityEngine;

namespace EarthVR.Core
{
    /// <summary>
    /// A small per-launch log file written by the game itself, next to the saved
    /// token: persistentDataPath/EarthVR/session-log.txt. It records the exact
    /// settings the game started with (including any settings-override.json), the
    /// XR runtime facts and periodic performance lines, so a capture is
    /// self-describing and does not depend on the device's shared log buffer. The
    /// previous launch's file is kept as session-log.prev.txt.
    /// </summary>
    public static class SessionLog
    {
        public const string FileName = "session-log.txt";
        private const long MaximumBytes = 1024 * 1024;
        private static readonly object Gate = new object();
        private static string _path;
        private static long _bytes;
        private static int _bakeWarnings;
        private static float _nextPanelSnapshot;

        /// <summary>Starts a new log for this launch. Safe to call once, early.</summary>
        public static void Begin()
        {
            try
            {
                var folder = Path.Combine(Application.persistentDataPath, "EarthVR");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, FileName);
                if (File.Exists(path))
                    File.Copy(path, Path.Combine(folder, "session-log.prev.txt"), true);
                File.WriteAllText(path, string.Empty);
                lock (Gate)
                {
                    _path = path;
                    _bytes = 0;
                }
                // Threaded: Cesium reports from worker threads.
                Application.logMessageReceivedThreaded += OnLog;
                Write("session started " + DateTime.UtcNow.ToString("o"));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("EarthVR session log unavailable: " + exception.Message);
            }
        }

        /// <summary>Appends a line. Never logs to Unity, so it cannot recurse.</summary>
        public static void Write(string message)
        {
            lock (Gate)
            {
                if (_path == null || _bytes > MaximumBytes)
                    return;
                try
                {
                    var line = DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + message + "\n";
                    File.AppendAllText(_path, line);
                    _bytes += line.Length;
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>Logs to Unity (logcat) and the session file.</summary>
        public static void Info(string message)
        {
            Debug.Log(message);
            Write(message);
        }

        /// <summary>Cesium's mesh-collider warnings are counted, not written, since
        /// they arrive hundreds of times a minute. Returns the count since last call.</summary>
        public static int TakeBakeWarnings() => Interlocked.Exchange(ref _bakeWarnings, 0);

        /// <summary>Records the settings the game booted with. Call after the
        /// override file and quality profile have been applied.</summary>
        public static void WriteConfig(EarthVRSettings settings)
        {
            Write(
                $"build={Application.version} unity={Application.unityVersion} platform={Application.platform} " +
                $"debugBuild={Debug.isDebugBuild} mobile={Application.isMobilePlatform}");
            Write("override file: " + (string.IsNullOrEmpty(SettingsOverrides.LastOverrideJson)
                ? "none"
                : SettingsOverrides.LastOverrideJson));
            Write("effective settings: " + JsonUtility.ToJson(settings));
        }

        /// <summary>Records what the hand-menu performance panel is showing, at
        /// most every ten seconds.</summary>
        public static void PanelSnapshot(string text)
        {
            if (Time.unscaledTime < _nextPanelSnapshot)
                return;
            _nextPanelSnapshot = Time.unscaledTime + 10f;
            Write("performance panel shows:\n" + text);
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning && condition != null &&
                condition.StartsWith("Detected one or more triangles", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _bakeWarnings);
                return;
            }

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                Write("[" + type + "] " + condition +
                      (string.IsNullOrEmpty(stackTrace) ? string.Empty : "\n" + stackTrace));
            }
            else if (type == LogType.Warning)
            {
                Write("[Warning] " + condition);
            }
        }
    }
}
