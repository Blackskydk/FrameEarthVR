using System;

namespace EarthVR.Navigation
{
    public static class FlightSpeedModel
    {
        public static float CalculateGeographicSpeed(
            float altitudeMeters,
            float userScale,
            float baseSpeed,
            float altitudeMultiplier,
            float scaleExponent,
            float maximumSpeed)
        {
            var altitude = Math.Max(0f, altitudeMeters);
            var scale = Math.Max(1f, userScale);
            var altitudeFactor = 1f + (float)Math.Log10(1f + altitude / 100f) * Math.Max(1f, altitudeMultiplier);
            var scaleFactor = (float)Math.Pow(scale, Math.Max(0f, scaleExponent));
            return Math.Min(Math.Max(0f, maximumSpeed), Math.Max(0f, baseSpeed) * altitudeFactor * scaleFactor);
        }
    }
}
