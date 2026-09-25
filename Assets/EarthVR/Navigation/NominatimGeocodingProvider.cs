using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EarthVR.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace EarthVR.Navigation
{
    /// <summary>
    /// Small, user-initiated place search client for the public OSM Nominatim service.
    /// It deliberately does not autocomplete and enforces the public service's one request/second limit.
    /// </summary>
    public sealed class NominatimGeocodingProvider : MonoBehaviour, IGeocodingProvider
    {
        [Serializable]
        private sealed class SearchResult
        {
            public string display_name;
            public string lat;
            public string lon;
        }

        [Serializable]
        private sealed class SearchResponse
        {
            public SearchResult[] results;
        }

        private readonly Dictionary<string, IReadOnlyList<GeographicPlace>> _cache = new();
        private readonly SemaphoreSlim _requestGate = new(1, 1);
        private DateTime _lastRequestUtc = DateTime.MinValue;

        public async Task<IReadOnlyList<GeographicPlace>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            query = query?.Trim();
            if (string.IsNullOrEmpty(query))
                return Array.Empty<GeographicPlace>();

            if (_cache.TryGetValue(query, out var cached))
                return cached;

            await _requestGate.WaitAsync(cancellationToken);
            try
            {
                if (_cache.TryGetValue(query, out cached))
                    return cached;

                var delay = TimeSpan.FromSeconds(1.05) - (DateTime.UtcNow - _lastRequestUtc);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);

                var url = "https://nominatim.openstreetmap.org/search" +
                          "?format=jsonv2&limit=4&q=" + UnityWebRequest.EscapeURL(query);
                using var request = UnityWebRequest.Get(url);
                request.SetRequestHeader("User-Agent", "FrameEarthVR/0.1 (personal Unity VR application)");
                request.SetRequestHeader("Accept-Language", CultureInfo.CurrentCulture.TwoLetterISOLanguageName + ",en");
                var operation = request.SendWebRequest();
                _lastRequestUtc = DateTime.UtcNow;

                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                if (request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException($"Location search failed: {request.error}");

                var response = JsonUtility.FromJson<SearchResponse>(
                    "{\"results\":" + request.downloadHandler.text + "}");
                var places = new List<GeographicPlace>();
                if (response?.results != null)
                {
                    foreach (var result in response.results)
                    {
                        if (!double.TryParse(result.lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
                            !double.TryParse(result.lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude))
                            continue;
                        places.Add(new GeographicPlace(result.display_name, longitude, latitude));
                    }
                }

                _cache[query] = places;
                return places;
            }
            finally
            {
                _requestGate.Release();
            }
        }
    }
}
