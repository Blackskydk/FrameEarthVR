using System;
using UnityEngine;

namespace EarthVR.Sky
{
    public readonly struct SolarPosition
    {
        public SolarPosition(double azimuthDegrees, double elevationDegrees)
        {
            AzimuthDegrees = azimuthDegrees;
            ElevationDegrees = elevationDegrees;
        }

        public double AzimuthDegrees { get; }
        public double ElevationDegrees { get; }

        public Vector3 ToUnityDirection()
        {
            var azimuth = AzimuthDegrees * Math.PI / 180d;
            var elevation = ElevationDegrees * Math.PI / 180d;
            var horizontal = Math.Cos(elevation);
            return new Vector3(
                (float)(Math.Sin(azimuth) * horizontal),
                (float)Math.Sin(elevation),
                (float)(Math.Cos(azimuth) * horizontal)).normalized;
        }
    }

    /// <summary>
    /// NOAA's fractional-year approximation for solar azimuth and elevation.
    /// Longitude is positive east and the supplied time is UTC.
    /// </summary>
    public static class SolarPositionCalculator
    {
        public static SolarPosition Calculate(DateTime utcTime, double latitudeDegrees, double longitudeDegrees)
        {
            utcTime = utcTime.Kind == DateTimeKind.Utc ? utcTime : utcTime.ToUniversalTime();
            latitudeDegrees = Math.Max(-89.999d, Math.Min(89.999d, latitudeDegrees));

            var daysInYear = DateTime.IsLeapYear(utcTime.Year) ? 366d : 365d;
            var fractionalHour = utcTime.Hour + utcTime.Minute / 60d + utcTime.Second / 3600d;
            var gamma = 2d * Math.PI / daysInYear *
                        (utcTime.DayOfYear - 1d + (fractionalHour - 12d) / 24d);

            var equationOfTime = 229.18d *
                                 (0.000075d +
                                  0.001868d * Math.Cos(gamma) -
                                  0.032077d * Math.Sin(gamma) -
                                  0.014615d * Math.Cos(2d * gamma) -
                                  0.040849d * Math.Sin(2d * gamma));
            var declination = 0.006918d -
                              0.399912d * Math.Cos(gamma) +
                              0.070257d * Math.Sin(gamma) -
                              0.006758d * Math.Cos(2d * gamma) +
                              0.000907d * Math.Sin(2d * gamma) -
                              0.002697d * Math.Cos(3d * gamma) +
                              0.00148d * Math.Sin(3d * gamma);

            var trueSolarMinutes = fractionalHour * 60d + equationOfTime + 4d * longitudeDegrees;
            trueSolarMinutes %= 1440d;
            if (trueSolarMinutes < 0d)
                trueSolarMinutes += 1440d;

            var hourAngleDegrees = trueSolarMinutes / 4d - 180d;
            var hourAngle = hourAngleDegrees * Math.PI / 180d;
            var latitude = latitudeDegrees * Math.PI / 180d;
            var cosZenith = Math.Sin(latitude) * Math.Sin(declination) +
                            Math.Cos(latitude) * Math.Cos(declination) * Math.Cos(hourAngle);
            cosZenith = Math.Max(-1d, Math.Min(1d, cosZenith));
            var zenith = Math.Acos(cosZenith);
            var elevation = 90d - zenith * 180d / Math.PI;

            var azimuth = Math.Atan2(
                              Math.Sin(hourAngle),
                              Math.Cos(hourAngle) * Math.Sin(latitude) -
                              Math.Tan(declination) * Math.Cos(latitude)) *
                          180d / Math.PI + 180d;
            azimuth %= 360d;
            if (azimuth < 0d)
                azimuth += 360d;

            return new SolarPosition(azimuth, elevation);
        }
    }
}
