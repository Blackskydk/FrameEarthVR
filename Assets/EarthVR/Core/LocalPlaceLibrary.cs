using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EarthVR.Core
{
    /// <summary>Stores bookmarks and bounded recent-place history as local JSON.</summary>
    public sealed class LocalPlaceLibrary : IBookmarkProvider
    {
        [Serializable]
        private sealed class PlaceData
        {
            public List<SavedPlace> bookmarks = new();
            public List<SavedPlace> recentPlaces = new();
        }

        private const int MaximumBookmarks = 64;
        private const int MaximumRecentPlaces = 12;
        private readonly string _path;
        private PlaceData _data;

        public LocalPlaceLibrary(string path = null)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Application.persistentDataPath, "earthvr-places.json")
                : path;
            _data = LoadData(_path);
        }

        public IReadOnlyList<SavedPlace> Bookmarks => _data.bookmarks;
        public IReadOnlyList<SavedPlace> RecentPlaces => _data.recentPlaces;
        public event Action Changed;

        public void SaveBookmark(SavedPlace place)
        {
            if (!PlaceLibraryRules.IsValid(place))
                return;

            var saved = PlaceLibraryRules.Prepare(place);
            var existing = _data.bookmarks.FindIndex(candidate =>
                candidate.id == saved.id || PlaceLibraryRules.IsSameDestination(candidate, saved));
            if (existing >= 0)
            {
                saved.id = _data.bookmarks[existing].id;
                saved.createdUtcTicks = _data.bookmarks[existing].createdUtcTicks;
                _data.bookmarks[existing] = saved;
            }
            else
            {
                _data.bookmarks.Insert(0, saved);
            }

            if (_data.bookmarks.Count > MaximumBookmarks)
                _data.bookmarks.RemoveRange(MaximumBookmarks, _data.bookmarks.Count - MaximumBookmarks);
            Persist();
        }

        public void RemoveBookmark(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;
            if (_data.bookmarks.RemoveAll(place => place.id == id) == 0)
                return;
            Persist();
        }

        public void RecordRecent(SavedPlace place)
        {
            if (!PlaceLibraryRules.IsValid(place))
                return;

            var recent = PlaceLibraryRules.Prepare(place);
            recent.lastVisitedUtcTicks = DateTime.UtcNow.Ticks;
            _data.recentPlaces.RemoveAll(candidate => PlaceLibraryRules.IsSameDestination(candidate, recent));
            _data.recentPlaces.Insert(0, recent);
            if (_data.recentPlaces.Count > MaximumRecentPlaces)
                _data.recentPlaces.RemoveRange(MaximumRecentPlaces, _data.recentPlaces.Count - MaximumRecentPlaces);
            Persist();
        }

        private static PlaceData LoadData(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new PlaceData();
                var loaded = JsonUtility.FromJson<PlaceData>(File.ReadAllText(path));
                if (loaded == null)
                    return new PlaceData();
                loaded.bookmarks ??= new List<SavedPlace>();
                loaded.recentPlaces ??= new List<SavedPlace>();
                loaded.bookmarks.RemoveAll(place => !PlaceLibraryRules.IsValid(place));
                loaded.recentPlaces.RemoveAll(place => !PlaceLibraryRules.IsValid(place));
                // JSON files are user-editable and older builds may have written
                // incomplete viewpoint fields. Normalize every surviving entry
                // before it can feed NaN/Infinity into navigation or scaling.
                for (var i = 0; i < loaded.bookmarks.Count; i++)
                    loaded.bookmarks[i] = PlaceLibraryRules.Prepare(loaded.bookmarks[i]);
                for (var i = 0; i < loaded.recentPlaces.Count; i++)
                    loaded.recentPlaces[i] = PlaceLibraryRules.Prepare(loaded.recentPlaces[i]);
                return loaded;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"EarthVR could not load saved places: {exception.Message}");
                return new PlaceData();
            }
        }

        private void Persist()
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                var temporaryPath = _path + ".tmp";
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(_data, true));
                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(temporaryPath, _path, null);
                    }
                    catch (Exception exception) when (
                        exception is PlatformNotSupportedException || exception is IOException)
                    {
                        File.Copy(temporaryPath, _path, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, _path);
                }
                Changed?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"EarthVR could not save places: {exception.Message}");
            }
        }
    }

    public static class PlaceLibraryRules
    {
        private const double SameDestinationDegrees = 0.0001d;

        public static bool IsValid(SavedPlace place) =>
            place != null &&
            !double.IsNaN(place.longitude) && !double.IsInfinity(place.longitude) &&
            !double.IsNaN(place.latitude) && !double.IsInfinity(place.latitude) &&
            place.longitude >= -180d && place.longitude <= 180d &&
            place.latitude >= -90d && place.latitude <= 90d;

        public static bool IsSameDestination(SavedPlace a, SavedPlace b) =>
            IsValid(a) && IsValid(b) &&
            LongitudeDistanceDegrees(a.longitude, b.longitude) <= SameDestinationDegrees &&
            Math.Abs(a.latitude - b.latitude) <= SameDestinationDegrees &&
            string.Equals(a.name?.Trim(), b.name?.Trim(), StringComparison.OrdinalIgnoreCase);

        public static bool IsSameDestination(
            SavedPlace saved,
            string name,
            double longitude,
            double latitude) =>
            IsValid(saved) &&
            !double.IsNaN(longitude) && !double.IsInfinity(longitude) &&
            !double.IsNaN(latitude) && !double.IsInfinity(latitude) &&
            LongitudeDistanceDegrees(saved.longitude, longitude) <= SameDestinationDegrees &&
            Math.Abs(saved.latitude - latitude) <= SameDestinationDegrees &&
            string.Equals(saved.name?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static double LongitudeDistanceDegrees(double first, double second)
        {
            var difference = Math.Abs(first - second) % 360d;
            return Math.Min(difference, 360d - difference);
        }

        public static SavedPlace Prepare(SavedPlace source)
        {
            var result = source.Copy();
            var now = DateTime.UtcNow.Ticks;
            if (string.IsNullOrWhiteSpace(result.id))
                result.id = Guid.NewGuid().ToString("N");
            result.name = string.IsNullOrWhiteSpace(result.name)
                ? $"{result.latitude:F4}°, {result.longitude:F4}°"
                : result.name.Trim();
            result.longitude = Math.Max(-180d, Math.Min(180d, result.longitude));
            result.latitude = Math.Max(-90d, Math.Min(90d, result.latitude));
            if (double.IsNaN(result.heightMeters) || double.IsInfinity(result.heightMeters))
                result.heightMeters = 0d;
            result.userScale = float.IsNaN(result.userScale) || float.IsInfinity(result.userScale)
                ? 1f
                : Mathf.Max(0.01f, result.userScale);
            result.headingDegrees = float.IsNaN(result.headingDegrees) || float.IsInfinity(result.headingDegrees)
                ? 0f
                : Mathf.Repeat(result.headingDegrees, 360f);
            if (!Enum.IsDefined(typeof(EarthVR.Navigation.MovementMode), result.movementMode))
                result.movementMode = EarthVR.Navigation.MovementMode.Flight;
            if (result.createdUtcTicks <= 0)
                result.createdUtcTicks = now;
            if (result.utcTimeTicks <= 0 || result.utcTimeTicks > DateTime.MaxValue.Ticks)
                result.utcTimeTicks = DateTime.UtcNow.Ticks;
            return result;
        }
    }
}
