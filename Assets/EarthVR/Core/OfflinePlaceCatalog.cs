using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EarthVR.Core
{
    /// <summary>
    /// Small, bundled navigation index used for instant suggestions and globe
    /// labels. It is intentionally local: querying it never contacts a service.
    /// </summary>
    public static class OfflinePlaceCatalog
    {
        public readonly struct CatalogPlace
        {
            public CatalogPlace(
                string name,
                double longitude,
                double latitude,
                string category,
                bool showOnGlobe = false)
            {
                Name = name;
                Longitude = longitude;
                Latitude = latitude;
                Category = category;
                ShowOnGlobe = showOnGlobe;
            }

            public string Name { get; }
            public double Longitude { get; }
            public double Latitude { get; }
            public string Category { get; }
            public bool ShowOnGlobe { get; }
            public GeographicPlace ToGeographicPlace() =>
                new GeographicPlace(Name, Longitude, Latitude);
        }

        public readonly struct PlaceSuggestion
        {
            public PlaceSuggestion(GeographicPlace place, string category)
            {
                Place = place;
                Category = category;
            }

            public GeographicPlace Place { get; }
            public string Category { get; }
        }

        public readonly struct StartingPlace
        {
            public StartingPlace(
                string name,
                double longitude,
                double latitude,
                float heightMeters)
            {
                Name = name;
                Longitude = longitude;
                Latitude = latitude;
                HeightMeters = heightMeters;
            }

            public string Name { get; }
            public double Longitude { get; }
            public double Latitude { get; }
            public float HeightMeters { get; }
        }

        private sealed class RankedSuggestion
        {
            public PlaceSuggestion suggestion;
            public int score;
        }

        public static IReadOnlyList<CatalogPlace> All => Places;
        public static IReadOnlyList<StartingPlace> StartingPlaces => InterestingStartingPlaces;

        // A deliberately varied launch reel. Heights keep the first frame safely
        // airborne while still close enough to recognize the destination.
        private static readonly StartingPlace[] InterestingStartingPlaces =
        {
            Start("Zion National Park", -112.9874, 37.2982, 2600f),
            Start("Yosemite National Park", -119.5383, 37.8651, 3200f),
            Start("Grand Canyon National Park", -112.1129, 36.1069, 3300f),
            Start("Yellowstone National Park", -110.5885, 44.4280, 3400f),
            Start("Banff National Park", -115.5708, 51.4968, 3600f),
            Start("Torres del Paine National Park", -72.9875, -50.9423, 3500f),
            Start("Fiordland National Park", 167.7180, -45.4150, 2400f),
            Start("Cape Town", 18.4241, -33.9249, 1800f),
            Start("New York", -74.0060, 40.7128, 750f),
            Start("Tokyo", 139.6917, 35.6895, 750f),
            Start("Sydney", 151.2093, -33.8688, 700f),
            Start("Rio de Janeiro", -43.1729, -22.9068, 1600f),
            Start("Paris", 2.3522, 48.8566, 650f),
            Start("Singapore", 103.8198, 1.3521, 700f),
            Start("Dubai", 55.2708, 25.2048, 750f),
            Start("Reykjavik", -21.9426, 64.1466, 650f)
        };

        private static readonly CatalogPlace[] Places =
        {
            City("Copenhagen", 12.5683, 55.6761), City("London", -0.1278, 51.5074),
            City("Paris", 2.3522, 48.8566), City("New York", -74.0060, 40.7128),
            City("Los Angeles", -118.2437, 34.0522), City("Mexico City", -99.1332, 19.4326),
            City("Sao Paulo", -46.6333, -23.5505), City("Buenos Aires", -58.3816, -34.6037),
            City("Cairo", 31.2357, 30.0444), City("Lagos", 3.3792, 6.5244),
            City("Cape Town", 18.4241, -33.9249), City("Dubai", 55.2708, 25.2048),
            City("Delhi", 77.1025, 28.7041), City("Singapore", 103.8198, 1.3521),
            City("Beijing", 116.4074, 39.9042), City("Tokyo", 139.6917, 35.6895),
            City("Sydney", 151.2093, -33.8688), City("Auckland", 174.7633, -36.8485),
            City("Reykjavik", -21.9426, 64.1466), City("Madrid", -3.7038, 40.4168),
            City("Rome", 12.4964, 41.9028), City("Berlin", 13.4050, 52.5200),
            City("Istanbul", 28.9784, 41.0082), City("Moscow", 37.6173, 55.7558),
            City("Nairobi", 36.8219, -1.2921), City("Mumbai", 72.8777, 19.0760),
            City("Bangkok", 100.5018, 13.7563), City("Jakarta", 106.8456, -6.2088),
            City("Shanghai", 121.4737, 31.2304), City("Seoul", 126.9780, 37.5665),
            City("Manila", 120.9842, 14.5995), City("Melbourne", 144.9631, -37.8136),
            City("Honolulu", -157.8583, 21.3069), City("San Francisco", -122.4194, 37.7749),
            City("Vancouver", -123.1207, 49.2827), City("Toronto", -79.3832, 43.6532),
            City("Chicago", -87.6298, 41.8781), City("Miami", -80.1918, 25.7617),
            City("Bogota", -74.0721, 4.7110), City("Lima", -77.0428, -12.0464),
            City("Santiago", -70.6693, -33.4489),

            Place("Greenland", -42.6, 72.0, "Region", true),
            Place("Sahara Desert", 13.0, 23.0, "Natural area", true),
            Place("Amazon Rainforest", -62.0, -4.0, "Natural area", true),
            Place("Himalayas", 86.0, 28.2, "Mountain range", true),
            Place("Alps", 10.0, 46.6, "Mountain range", true),
            Place("Serengeti", 34.8, -2.3, "Natural area", true),
            Place("Grand Canyon", -112.1, 36.1, "Natural landmark", true),
            Place("Great Barrier Reef", 147.7, -18.3, "Natural landmark", true),
            Place("Patagonia", -71.0, -47.0, "Region", true),
            Place("Antarctica", 0.0, -82.0, "Region", true),

            Park("Yellowstone National Park", -110.5885, 44.4280),
            Park("Zion National Park", -112.9874, 37.2982),
            Park("Yosemite National Park", -119.5383, 37.8651),
            Park("Grand Canyon National Park", -112.1129, 36.1069),
            Park("Banff National Park", -115.5708, 51.4968),
            Park("Jasper National Park", -117.9543, 52.8734),
            Park("Torres del Paine National Park", -72.9875, -50.9423),
            Park("Iguazu National Park", -54.4367, -25.6953),
            Park("Galapagos National Park", -90.5250, -0.8293),
            Park("Serengeti National Park", 34.6857, -2.3333),
            Park("Kruger National Park", 31.5547, -23.9884),
            Park("Table Mountain National Park", 18.4098, -33.9628),
            Park("Etosha National Park", 15.9150, -18.8556),
            Park("Kilimanjaro National Park", 37.3556, -3.0674),
            Park("Plitvice Lakes National Park", 15.6167, 44.8800),
            Park("Vatnajokull National Park", -16.9680, 64.4200),
            Park("Thingvellir National Park", -21.1297, 64.2559),
            Park("Goreme National Park", 34.8289, 38.6431),
            Park("Sagarmatha National Park", 86.7120, 27.9320),
            Park("Ranthambore National Park", 76.5026, 26.0173),
            Park("Zhangjiajie National Forest Park", 110.4792, 29.3150),
            Park("Fuji-Hakone-Izu National Park", 138.7280, 35.3606),
            Park("Komodo National Park", 119.4897, -8.5500),
            Park("Kakadu National Park", 132.4213, -12.8375),
            Park("Fiordland National Park", 167.7180, -45.4150),
            Park("Great Barrier Reef Marine Park", 147.7000, -18.2861),
            Park("Uluru-Kata Tjuta National Park", 131.0369, -25.3444),
            Park("Corcovado National Park", -83.5775, 8.5380),

            Place("Machu Picchu", -72.5450, -13.1631, "Historic site"),
            Place("Petra", 35.4444, 30.3285, "Historic site"),
            Place("Angkor Wat", 103.8670, 13.4125, "Historic site"),
            Place("Taj Mahal", 78.0421, 27.1751, "Landmark"),
            Place("Great Wall of China", 116.5704, 40.4319, "Historic site"),
            Place("Eiffel Tower", 2.2945, 48.8584, "Landmark"),
            Place("Statue of Liberty", -74.0445, 40.6892, "Landmark"),
            Place("Victoria Falls", 25.8572, -17.9243, "Natural landmark"),
            Place("Wadi Rum", 35.4194, 29.5321, "Protected area"),
            Place("Mount Everest", 86.9250, 27.9881, "Mountain")
        };

        public static IReadOnlyList<PlaceSuggestion> FindSuggestions(
            string query,
            IReadOnlyList<SavedPlace> bookmarks,
            IReadOnlyList<SavedPlace> recents,
            int maximumResults = 3)
        {
            var normalizedQuery = Normalize(query);
            if (normalizedQuery.Length < 2 || maximumResults <= 0)
                return Array.Empty<PlaceSuggestion>();

            var candidates = new List<RankedSuggestion>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            AddSavedCandidates(candidates, usedNames, bookmarks, normalizedQuery, "Favorite", 0);
            AddSavedCandidates(candidates, usedNames, recents, normalizedQuery, "Recent", 1);
            for (var i = 0; i < Places.Length; i++)
            {
                var place = Places[i];
                var normalizedName = Normalize(place.Name);
                var matchScore = MatchScore(normalizedName, normalizedQuery);
                if (matchScore < 0 || !usedNames.Add(normalizedName))
                    continue;
                candidates.Add(new RankedSuggestion
                {
                    suggestion = new PlaceSuggestion(place.ToGeographicPlace(), place.Category),
                    score = matchScore * 10 + 5
                });
            }

            candidates.Sort((left, right) =>
            {
                var scoreComparison = left.score.CompareTo(right.score);
                return scoreComparison != 0
                    ? scoreComparison
                    : string.Compare(
                        left.suggestion.Place.Name,
                        right.suggestion.Place.Name,
                        StringComparison.OrdinalIgnoreCase);
            });
            var count = Math.Min(maximumResults, candidates.Count);
            var result = new PlaceSuggestion[count];
            for (var i = 0; i < count; i++)
                result[i] = candidates[i].suggestion;
            return result;
        }

        public static int NormalizeStartingPlaceIndex(int candidateIndex, int previousIndex)
        {
            if (InterestingStartingPlaces.Length == 0)
                return -1;
            var index = ((candidateIndex % InterestingStartingPlaces.Length) + InterestingStartingPlaces.Length) %
                        InterestingStartingPlaces.Length;
            if (InterestingStartingPlaces.Length > 1 && index == previousIndex)
                index = (index + 1) % InterestingStartingPlaces.Length;
            return index;
        }

        private static void AddSavedCandidates(
            List<RankedSuggestion> candidates,
            HashSet<string> usedNames,
            IReadOnlyList<SavedPlace> places,
            string query,
            string category,
            int sourceScore)
        {
            if (places == null)
                return;
            for (var i = 0; i < places.Count; i++)
            {
                var saved = places[i];
                if (!PlaceLibraryRules.IsValid(saved))
                    continue;
                var normalizedName = Normalize(saved.name);
                var matchScore = MatchScore(normalizedName, query);
                if (matchScore < 0 || !usedNames.Add(normalizedName))
                    continue;
                candidates.Add(new RankedSuggestion
                {
                    suggestion = new PlaceSuggestion(
                        new GeographicPlace(saved.name, saved.longitude, saved.latitude, saved.heightMeters),
                        category),
                    score = matchScore * 10 + sourceScore
                });
            }
        }

        private static int MatchScore(string name, string query)
        {
            if (name == query)
                return 0;
            if (name.StartsWith(query, StringComparison.Ordinal))
                return 1;
            if (name.IndexOf(query, StringComparison.Ordinal) >= 0)
                return 2;

            var queryWords = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var nameWords = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var queryIndex = 0; queryIndex < queryWords.Length; queryIndex++)
            {
                var found = false;
                for (var nameIndex = 0; nameIndex < nameWords.Length; nameIndex++)
                {
                    if (!nameWords[nameIndex].StartsWith(queryWords[queryIndex], StringComparison.Ordinal))
                        continue;
                    found = true;
                    break;
                }
                if (!found)
                    return -1;
            }
            return queryWords.Length > 0 ? 3 : -1;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            var pendingSpace = false;
            for (var i = 0; i < decomposed.Length; i++)
            {
                var character = decomposed[i];
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;
                if (char.IsLetterOrDigit(character))
                {
                    if (pendingSpace && builder.Length > 0)
                        builder.Append(' ');
                    builder.Append(character);
                    pendingSpace = false;
                }
                else
                {
                    pendingSpace = true;
                }
            }
            return builder.ToString();
        }

        private static CatalogPlace City(string name, double longitude, double latitude) =>
            new CatalogPlace(name, longitude, latitude, "City", true);

        private static StartingPlace Start(
            string name,
            double longitude,
            double latitude,
            float heightMeters) =>
            new(name, longitude, latitude, heightMeters);

        private static CatalogPlace Park(string name, double longitude, double latitude) =>
            new CatalogPlace(name, longitude, latitude, "National park");

        private static CatalogPlace Place(
            string name,
            double longitude,
            double latitude,
            string category,
            bool showOnGlobe = false) =>
            new CatalogPlace(name, longitude, latitude, category, showOnGlobe);
    }
}
