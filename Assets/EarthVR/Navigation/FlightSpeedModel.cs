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
            return CalculateGeographicSpeed(
                altitudeMeters,
                userScale,
                baseSpeed,
                altitudeMultiplier,
                scaleExponent,
                maximumSpeed,
                0f);
        }

        public static float CalculateGeographicSpeed(
            float altitudeMeters,
            float userScale,
            float baseSpeed,
            float altitudeMultiplier,
            float scaleExponent,
            float maximumSpeed,
            float altitudeCruiseFractionPerSecond)
        {
            var altitude = Math.Max(0f, altitudeMeters);
            var scale = Math.Max(1f, userScale);
            var altitudeFactor = 1f + (float)Math.Log10(1f + altitude / 100f) * Math.Max(1f, altitudeMultiplier);
            var scaleFactor = (float)Math.Pow(scale, Math.Max(0f, scaleExponent));
            var logarithmicSpeed = Math.Max(0f, baseSpeed) * altitudeFactor * scaleFactor;
            // At planetary altitude, a logarithmic curve alone still takes many
            // minutes to cross a country. Cover a stable fraction of the current
            // altitude each second so speed grows naturally with visible scale.
            var altitudeCruiseSpeed = altitude * Math.Max(0f, altitudeCruiseFractionPerSecond);
            return Math.Min(
                Math.Max(0f, maximumSpeed),
                Math.Max(logarithmicSpeed, altitudeCruiseSpeed));
        }
    }
}
