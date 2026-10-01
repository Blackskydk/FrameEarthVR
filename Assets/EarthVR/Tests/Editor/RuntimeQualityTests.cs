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
                Assert.That(settings.standaloneFoveationLevelOverride, Is.LessThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void OverrideFileReplacesOnlyTheFieldsItNames()
        {
            var settings = ScriptableObject.CreateInstance<EarthVRSettings>();
            try
            {
                var tileLoads = settings.maximumSimultaneousTileLoads;
                var applied = SettingsOverrides.TryApply(
                    settings, "{\"humanNearClipMeters\":0.1,\"standaloneFoveationLevelOverride\":0}");

                Assert.That(applied, Is.True);
                Assert.That(settings.humanNearClipMeters, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(settings.standaloneFoveationLevelOverride, Is.EqualTo(0f));
                Assert.That(settings.maximumSimultaneousTileLoads, Is.EqualTo(tileLoads));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void OverrideValuesAreClampedToSafeRanges()
        {
            var settings = ScriptableObject.CreateInstance<EarthVRSettings>();
            try
            {
                SettingsOverrides.TryApply(
                    settings,
                    "{\"pcMsaa\":99,\"standaloneMaximumScreenSpaceError\":-5,\"maximumSimultaneousTileLoads\":0," +
                    "\"humanNearClipMeters\":0,\"pcRenderScale\":9}");

                Assert.That(settings.pcMsaa, Is.EqualTo(8));
                Assert.That(settings.standaloneMaximumScreenSpaceError, Is.EqualTo(1f));
                Assert.That(settings.maximumSimultaneousTileLoads, Is.EqualTo(1));
                Assert.That(settings.humanNearClipMeters, Is.GreaterThan(0f));
                Assert.That(settings.pcRenderScale, Is.EqualTo(2f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("not json")]
        public void MissingOrMalformedOverrideLeavesSettingsUntouched(string json)
        {
            var settings = ScriptableObject.CreateInstance<EarthVRSettings>();
            try
            {
                var before = settings.humanNearClipMeters;
                Assert.That(SettingsOverrides.TryApply(settings, json), Is.False);
                Assert.That(settings.humanNearClipMeters, Is.EqualTo(before));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
