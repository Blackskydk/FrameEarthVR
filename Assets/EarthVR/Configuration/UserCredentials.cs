using System;
using System.IO;
using UnityEngine;

namespace EarthVR.Configuration
{
    /// <summary>User-provided credentials are stored on this device, never in the APK.</summary>
    public static class UserCredentials
    {
        [Serializable]
        private sealed class Configuration { public string accessToken; }

        public static string FilePath => Path.Combine(Application.persistentDataPath, "EarthVR", CesiumIonConfiguration.LocalFileName);

        public static bool IsUsable(string token) => !string.IsNullOrWhiteSpace(token) &&
            token.Trim().Length <= 8192 && token.IndexOf("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) < 0 &&
            token.Trim().IndexOfAny(new[] { ' ', '\n', '\r', '\t' }) < 0;

        public static string ReadToken()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var token = JsonUtility.FromJson<Configuration>(File.ReadAllText(FilePath))?.accessToken;
                return IsUsable(token) ? token.Trim() : null;
            }
            catch (Exception) { return null; }
        }

        public static void SaveToken(string token)
        {
            if (!IsUsable(token)) throw new ArgumentException("Enter a token without spaces.");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(new Configuration { accessToken = token.Trim() }));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temporaryPath, FilePath);
        }

        public static void ForgetToken()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
    }
}
