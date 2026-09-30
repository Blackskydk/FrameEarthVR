using System;

namespace EarthVR.Scaling
{
    public static class ScaleMath
    {
        /// <summary>Grounded resizing needs deliberate vertical aim and stick input.
        /// Both locomotion and resizing use this decision, so they cannot fight.</summary>
        public static float GroundedScaleIntent(float forwardY, float stickY)
        {
            if (Math.Abs(forwardY) < 0.94f || Math.Abs(stickY) <= 0.2f) return 0f;
            var strength = Math.Min(1f, (Math.Abs(stickY) - 0.2f) / 0.8f);
            return Math.Sign(forwardY) * Math.Sign(stickY) * strength;
        }

        public static float Clamp(float scale, float minimum, float maximum)
        {
            if (minimum <= 0f)
                throw new ArgumentOutOfRangeException(nameof(minimum), "Scale must be positive.");
            if (maximum < minimum)
                throw new ArgumentOutOfRangeException(nameof(maximum));
            return Math.Max(minimum, Math.Min(maximum, scale));
        }

        public static float ToNormalizedLog(float scale, float minimum, float maximum)
        {
            scale = Clamp(scale, minimum, maximum);
            var minLog = Math.Log10(minimum);
            var range = Math.Log10(maximum) - minLog;
            return range <= double.Epsilon ? 0f : (float)((Math.Log10(scale) - minLog) / range);
        }

        public static float FromNormalizedLog(float normalized, float minimum, float maximum)
        {
            if (minimum <= 0f || maximum < minimum)
                throw new ArgumentOutOfRangeException(nameof(minimum));
            normalized = Math.Max(0f, Math.Min(1f, normalized));
            var exponent = Math.Log10(minimum) + normalized * (Math.Log10(maximum) - Math.Log10(minimum));
            return (float)Math.Pow(10d, exponent);
        }

        public static float PhysicalToGeographicMeters(float physicalMeters, float userScale) =>
            physicalMeters * Math.Max(userScale, 0f);

        public static float GeographicToUnityMeters(float geographicMeters, float userScale) =>
            userScale <= 0f ? 0f : geographicMeters / userScale;

        public static float ApproximateEyeHeight(float realEyeHeightMeters, float userScale) =>
            Math.Max(realEyeHeightMeters, 0f) * Math.Max(userScale, 0f);

        public static double CesiumGlobeScale(float userScale) =>
            userScale <= 0f ? 1d : 1d / userScale;
    }
}
