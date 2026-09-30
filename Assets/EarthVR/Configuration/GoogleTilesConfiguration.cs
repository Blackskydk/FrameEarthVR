using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace EarthVR.Configuration
{
    public static class GoogleTilesConfiguration
    {
        [Serializable]
        private sealed class LocalConfiguration
        {
            public string apiKey;
        }

        public const string LocalFileName = "google-maps.local.json";
        private const string Placeholder = "REPLACE_WITH_A_RESTRICTED_MAP_TILES_API_KEY";

        public static IEnumerator LoadApiKey(Action<string> completed)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var environmentValue = Environment.GetEnvironmentVariable("EARTHVR_GOOGLE_MAPS_API_KEY");
            if (IsUsable(environmentValue))
            {
                completed(environmentValue.Trim());
                yield break;
            }

            var path = $"{Application.streamingAssetsPath}/EarthVR/{LocalFileName}";
            if (!path.Contains("://"))
                path = "file:///" + path.Replace('\\', '/');
            using var request = UnityWebRequest.Get(path);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"EarthVR is running without Google Photorealistic 3D Tiles. " +
                    $"Copy google-maps.example.json to {LocalFileName} and add a restricted key. " +
                    $"Details: {request.error}");
                completed(null);
                yield break;
            }

            LocalConfiguration configuration;
            try
            {
                configuration = JsonUtility.FromJson<LocalConfiguration>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not parse {LocalFileName}: {exception.Message}");
                completed(null);
                yield break;
            }

            completed(IsUsable(configuration?.apiKey) ? configuration.apiKey.Trim() : null);
#else
            // The public player uses Cesium ion. Never reuse publisher keys.
            completed(null);
            yield break;
#endif
        }

        public static string BuildRootUrl(string apiKey)
        {
            if (!IsUsable(apiKey))
                throw new ArgumentException("A Google Map Tiles API key is required.", nameof(apiKey));
            return "https://tile.googleapis.com/v1/3dtiles/root.json?key=" + Uri.EscapeDataString(apiKey.Trim());
        }

        private static bool IsUsable(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.IndexOf(Placeholder, StringComparison.OrdinalIgnoreCase) < 0;
    }
}
