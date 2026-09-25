using System;
using EarthVR.Sky;
using NUnit.Framework;

namespace EarthVR.Tests.Editor
{
    public sealed class SolarPositionTests
    {
        [Test]
        public void EquatorialEquinoxNoonIsNearZenith()
        {
            var position = SolarPositionCalculator.Calculate(
                new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc),
                0d,
                0d);

            Assert.That(position.ElevationDegrees, Is.GreaterThan(87d));
        }

        [Test]
        public void CopenhagenSummerSunIsHigherThanWinterSun()
        {
            var summer = SolarPositionCalculator.Calculate(
                new DateTime(2026, 6, 21, 11, 0, 0, DateTimeKind.Utc),
                55.6761d,
                12.5683d);
            var winter = SolarPositionCalculator.Calculate(
                new DateTime(2026, 12, 21, 11, 0, 0, DateTimeKind.Utc),
                55.6761d,
                12.5683d);

            Assert.That(summer.ElevationDegrees, Is.GreaterThan(50d));
            Assert.That(winter.ElevationDegrees, Is.LessThan(15d));
            Assert.That(summer.ElevationDegrees, Is.GreaterThan(winter.ElevationDegrees));
        }

        [Test]
        public void UnitySunDirectionIsNormalized()
        {
            var direction = SolarPositionCalculator.Calculate(
                    new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc),
                    55.6761d,
                    12.5683d)
                .ToUnityDirection();

            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(0.0001f));
        }
    }
}
