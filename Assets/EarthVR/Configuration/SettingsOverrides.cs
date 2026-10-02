using System;
using System.IO;
using EarthVR.Core;
using UnityEngine;

namespace EarthVR.Configuration
{
    /// <summary>
    /// Optional on-device tuning without rebuilding. When a small JSON file
    /// exists in the app's persistent data folder, any public
    /// <see cref="EarthVRSettings"/> field it names replaces the built-in
    /// value; fields it omits keep their defaults.
    /// </summary>
    public static class SettingsOverrides
    {
        public const string FileName = "settings-override.json";
        private const long MaximumBytes = 64 * 1024;

        /// <summary>The JSON text of the override that was applied this launch, or null.</summary>
        public static string LastOverrideJson { get; private set; }

        public static string FilePath => Path.Combine(Application.persistentDataPath, "EarthVR", FileName);

        /// <summary>Applies the override file if one exists. Never throws:
        /// a missing or malformed file leaves the settings untouched.</summary>
        public static bool TryApplyFromDisk(EarthVRSettings settings)
        {
            try
            {
                var path = FilePath;
                if (!File.Exists(path) || new FileInfo(path).Length > MaximumBytes)
                    return false;
                if (!TryApply(settings, File.ReadAllText(path)))
                    return false;
                SessionLog.Info($"EarthVR settings override applied from {path}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"EarthVR settings override ignored: {exception.Message}");
                return false;
            }
        }

        public static bool TryApply(EarthVRSettings settings, string json)
        {
            if (settings == null || string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                JsonUtility.FromJsonOverwrite(json, settings);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"EarthVR settings override is not valid JSON: {exception.Message}");
                return false;
            }
            settings.ClampToSafeRanges();
            LastOverrideJson = json.Trim();
            return true;
        }
    }
}
