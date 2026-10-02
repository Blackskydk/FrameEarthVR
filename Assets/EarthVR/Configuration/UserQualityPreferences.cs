using UnityEngine;

namespace EarthVR.Configuration
{
    /// <summary>
    /// Graphics choices made on the wrist menu's settings page, remembered between
    /// launches. A missing value means nothing was chosen, so the build's defaults apply.
    /// </summary>
    public static class UserQualityPreferences
    {
        private const string Prefix = "EarthVR.Quality.";

        /// <summary>SunShadowPreference as an int.</summary>
        public const string SunShadows = "SunShadows";
        public const string FlatTileLighting = "FlatTileLighting";
        /// <summary>Foveation level 0-1; 0 means foveated rendering is off.</summary>
        public const string FoveationLevel = "FoveationLevel";
        public const string EyeTracking = "EyeTracking";
        public const string Msaa = "Msaa";

        public static int? GetInt(string name) =>
            PlayerPrefs.HasKey(Prefix + name) ? PlayerPrefs.GetInt(Prefix + name) : (int?)null;

        public static float? GetFloat(string name) =>
            PlayerPrefs.HasKey(Prefix + name) ? PlayerPrefs.GetFloat(Prefix + name) : (float?)null;

        public static bool? GetBool(string name)
        {
            var value = GetInt(name);
            return value.HasValue ? value.Value != 0 : (bool?)null;
        }

        public static void SetInt(string name, int value)
        {
            PlayerPrefs.SetInt(Prefix + name, value);
            PlayerPrefs.Save();
        }

        public static void SetFloat(string name, float value)
        {
            PlayerPrefs.SetFloat(Prefix + name, value);
            PlayerPrefs.Save();
        }

        public static void SetBool(string name, bool value) => SetInt(name, value ? 1 : 0);
    }
}
