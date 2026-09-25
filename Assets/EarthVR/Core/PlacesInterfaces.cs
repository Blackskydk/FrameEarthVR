using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
        IReadOnlyList<GeographicPlace> Load();
        void Save(GeographicPlace place);
        void Remove(GeographicPlace place);
    }
}
