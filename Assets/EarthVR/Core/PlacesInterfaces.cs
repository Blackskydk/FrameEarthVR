using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EarthVR.Navigation;

namespace EarthVR.Core
{
    public readonly struct GeographicPlace
    {
        public GeographicPlace(string name, double longitude, double latitude, double heightMeters = 0d)
        {
            Name = name;
            Longitude = longitude;
            Latitude = latitude;
            HeightMeters = heightMeters;
        }

        public string Name { get; }
        public double Longitude { get; }
        public double Latitude { get; }
        public double HeightMeters { get; }
    }

    public interface IGeocodingProvider
    {
        Task<IReadOnlyList<GeographicPlace>> SearchAsync(string query, CancellationToken cancellationToken);
    }

    public interface IBookmarkProvider
    {
        IReadOnlyList<SavedPlace> Bookmarks { get; }
        IReadOnlyList<SavedPlace> RecentPlaces { get; }
        event Action Changed;
        void SaveBookmark(SavedPlace place);
        void RemoveBookmark(string id);
        void RecordRecent(SavedPlace place);
    }

    /// <summary>A complete, persistent VR viewpoint rather than only a map pin.</summary>
    [Serializable]
    public sealed class SavedPlace
    {
        public string id;
        public string name;
        public double longitude;
        public double latitude;
        public double heightMeters;
        public float headingDegrees;
        public float userScale = 1f;
        public MovementMode movementMode = MovementMode.Flight;
        public long utcTimeTicks;
        public long createdUtcTicks;
        public long lastVisitedUtcTicks;

        public SavedPlace Copy() => (SavedPlace)MemberwiseClone();
    }
}
