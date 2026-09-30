using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace EarthVR.Configuration
{
    public static class CesiumIonConfiguration
    {
        [Serializable]
        private sealed class LocalConfiguration
        {
            public string accessToken;
        }

        public const string LocalFileName = "cesium-ion.local.json";
        private const string Placeholder = "REPLACE_WITH_YOUR_CESIUM_ION_ACCESS_TOKEN";

        public static IEnumerator LoadAccessToken(Action<string> completed)
        {
            var userToken = UserCredentials.ReadToken();
            if (IsUsable(userToken))
            {
                completed(userToken);
                yield break;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var environmentValue = Environment.GetEnvironmentVariable("EARTHVR_CESIUM_ION_ACCESS_TOKEN");
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
                    "EarthVR is waiting for a Cesium ion token. " +
                    $"Copy cesium-ion.example.json to {LocalFileName} and add an assets:read token. " +
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

            completed(IsUsable(configuration?.accessToken) ? configuration.accessToken.Trim() : null);
#else
            // A distributed release only accepts credentials entered by its user.
            completed(null);
            yield break;
#endif
        }

        private static bool IsUsable(string value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.IndexOf(Placeholder, StringComparison.OrdinalIgnoreCase) < 0;
    }
}
