using EarthVR.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace EarthVR.Tests
{
    public sealed class RuntimeQualityTests
    {
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 2)]
        [TestCase(4, 4)]
        [TestCase(7, 4)]
        [TestCase(8, 8)]
        [TestCase(16, 8)]
        public void MsaaIsMappedToASupportedSampleCount(int requested, int expected)
        {
            Assert.That(RuntimeQuality.NormalizeMsaa(requested), Is.EqualTo(expected));
        }

        [TestCase(0.1f, 0.5f)]
        [TestCase(1f, 1f)]
        [TestCase(1.25f, 1.25f)]
        [TestCase(5f, 2f)]
        public void RenderScaleStaysInsideTheSupportedRange(float requested, float expected)
        {
            Assert.That(RuntimeQuality.ClampRenderScale(requested), Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void HeadsetKeepsItsTunedProfileWhilePcDefaultsAreHigher()
        {
            var settings = ScriptableObject.CreateInstance<EarthVRSettings>();
            try
            {
                Assert.That(settings.pcMaximumScreenSpaceError, Is.LessThan(settings.standaloneMaximumScreenSpaceError));
                Assert.That(settings.pcCacheMegabytes, Is.GreaterThan(settings.standaloneCacheMegabytes));
                Assert.That(settings.pcMsaa, Is.GreaterThan(settings.standaloneMsaa));
                Assert.That(settings.pcRenderScale, Is.GreaterThan(settings.standaloneRenderScale));
                Assert.That(settings.standaloneBloom, Is.False);
                Assert.That(settings.pcBloom, Is.True);
                Assert.That(settings.standaloneFlatTileLighting, Is.False);
                Assert.That(settings.pcFlatTileLighting, Is.False);
                Assert.That(settings.standaloneFoveationLevelOverride, Is.EqualTo(0.25f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
