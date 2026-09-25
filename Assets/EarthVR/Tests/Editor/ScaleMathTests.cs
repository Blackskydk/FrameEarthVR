using EarthVR.Scaling;
using NUnit.Framework;

namespace EarthVR.Tests
{
    public sealed class ScaleMathTests
    {
        [TestCase(1f, 0f)]
        [TestCase(10f, 0.25f)]
        [TestCase(100f, 0.5f)]
        [TestCase(10000f, 1f)]
        public void LogScaleMapsAcrossOrdersOfMagnitude(float scale, float normalized)
        {
            Assert.That(ScaleMath.ToNormalizedLog(scale, 1f, 10000f), Is.EqualTo(normalized).Within(0.0001f));
            Assert.That(ScaleMath.FromNormalizedLog(normalized, 1f, 10000f), Is.EqualTo(scale).Within(0.01f));
        }

        [Test]
        public void PhysicalMovementIsMultipliedByUserScale()
        {
            Assert.That(ScaleMath.PhysicalToGeographicMeters(1f, 100f), Is.EqualTo(100f));
            Assert.That(ScaleMath.GeographicToUnityMeters(100f, 100f), Is.EqualTo(1f));
        }

        [Test]
        public void HeightAndCesiumScaleAreConsistent()
        {
            Assert.That(ScaleMath.ApproximateEyeHeight(1.75f, 100f), Is.EqualTo(175f));
            Assert.That(ScaleMath.CesiumGlobeScale(100f), Is.EqualTo(0.01d).Within(0.000001d));
        }

        [TestCase(-1f, 1f)]
        [TestCase(5f, 5f)]
        [TestCase(5000f, 1000f)]
        public void ScaleIsClamped(float input, float expected)
        {
            Assert.That(ScaleMath.Clamp(input, 1f, 1000f), Is.EqualTo(expected));
        }
    }
}

