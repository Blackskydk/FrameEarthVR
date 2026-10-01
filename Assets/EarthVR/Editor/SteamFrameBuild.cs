using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace EarthVR.Editor
{
    public static class SteamFrameBuild
    {
        private const string ScenePath = "Assets/EarthVR/Scenes/EarthVR.unity";
        private const string OutputPath = "Builds/SteamFrame/FrameEarthVR.apk";

        private const string ForcePerformanceMenu = "EarthVR/Steam Frame/Force Performance Features On Build";
        private const string ForcePerformanceKey = "EarthVR.SteamFrame.ForcePerformanceFeatures";

        /// <summary>When true (the default) every Android build re-applies
        /// foveation, render regions, symmetric projection and buffer discards.</summary>
        internal static bool ForcePerformanceFeatures => EditorPrefs.GetBool(ForcePerformanceKey, true);

        private const string EyeTrackedFoveationMenu = "EarthVR/Steam Frame/Eye-Tracked Foveation";
        private const string EyeTrackedFoveationKey = "EarthVR.SteamFrame.EyeTrackedFoveation";

        /// <summary>Build-time foveation level handed to Valve's foveation
        /// feature. 0.5 looked too aggressive on Steam Frame; the runtime value
        /// in EarthVRSettings is applied over it once XR is running.</summary>
        internal const float DefaultFoveationLevel = 0.25f;

        /// <summary>Lets Valve's foveation feature move the sharp region with
        /// the eyes instead of fixing it at the centre. Experimental: it
        /// depends on the runtime exposing eye-tracked foveation; off by default
        /// because it left half of one eye blurry (gaze centre misplaced).</summary>
        internal static bool EyeTrackedFoveation => EditorPrefs.GetBool(EyeTrackedFoveationKey, false);

        [MenuItem(EyeTrackedFoveationMenu, priority = 31)]
        private static void ToggleEyeTrackedFoveation() =>
            EditorPrefs.SetBool(EyeTrackedFoveationKey, !EyeTrackedFoveation);

        [MenuItem(EyeTrackedFoveationMenu, true)]
        private static bool ValidateEyeTrackedFoveation()
        {
            Menu.SetChecked(EyeTrackedFoveationMenu, EyeTrackedFoveation);
            return true;
        }

        // Each Android-only OpenXR optimization can be switched off for a build to
        // find which one causes an artifact. All default to on (current behavior).
        private const string FeatureMenuRoot = "EarthVR/Steam Frame/Android Features/";
        private const string FoveationMenu = FeatureMenuRoot + "Foveated Rendering";
        private const string RenderRegionsMenu = FeatureMenuRoot + "Render Regions + Symmetric Projection";
        private const string BufferDiscardsMenu = FeatureMenuRoot + "Buffer Discards";
        private const string LateLatchingMenu = FeatureMenuRoot + "Late Latching";

        private static bool GetFeature(string key) => EditorPrefs.GetBool("EarthVR.SteamFrame.Feature." + key, true);
        private static void ToggleFeature(string key) => EditorPrefs.SetBool("EarthVR.SteamFrame.Feature." + key, !GetFeature(key));
        private static bool CheckFeature(string menu, string key)
        {
            Menu.SetChecked(menu, GetFeature(key));
            return true;
        }

        internal static bool FoveatedRenderingEnabled => GetFeature("Foveation");
        internal static bool RenderRegionsEnabled => GetFeature("RenderRegions");
        internal static bool BufferDiscardsEnabled => GetFeature("BufferDiscards");
        internal static bool LateLatchingEnabled => GetFeature("LateLatching");

        [MenuItem(FoveationMenu, priority = 40)]
        private static void ToggleFoveation() => ToggleFeature("Foveation");
        [MenuItem(FoveationMenu, true)]
        private static bool ValidateFoveation() => CheckFeature(FoveationMenu, "Foveation");

        [MenuItem(RenderRegionsMenu, priority = 41)]
        private static void ToggleRenderRegions() => ToggleFeature("RenderRegions");
        [MenuItem(RenderRegionsMenu, true)]
        private static bool ValidateRenderRegions() => CheckFeature(RenderRegionsMenu, "RenderRegions");

        [MenuItem(BufferDiscardsMenu, priority = 42)]
        private static void ToggleBufferDiscards() => ToggleFeature("BufferDiscards");
        [MenuItem(BufferDiscardsMenu, true)]
        private static bool ValidateBufferDiscards() => CheckFeature(BufferDiscardsMenu, "BufferDiscards");

        [MenuItem(LateLatchingMenu, priority = 43)]
        private static void ToggleLateLatching() => ToggleFeature("LateLatching");
        [MenuItem(LateLatchingMenu, true)]
        private static bool ValidateLateLatching() => CheckFeature(LateLatchingMenu, "LateLatching");

        /// <summary>Short code shown in the in-game panel so a headset build can be
        /// identified: F foveation, R render regions + symmetric projection,
        /// B buffer discards, L late latching, E eye-tracked foveation (1 = on).</summary>
        internal static string FeatureTag() =>
            $"F{(FoveatedRenderingEnabled ? 1 : 0)}R{(RenderRegionsEnabled ? 1 : 0)}" +
            $"B{(BufferDiscardsEnabled ? 1 : 0)}L{(LateLatchingEnabled ? 1 : 0)}E{(EyeTrackedFoveation ? 1 : 0)}";

        [MenuItem(ForcePerformanceMenu, priority = 30)]
        private static void ToggleForcePerformanceFeatures() =>
            EditorPrefs.SetBool(ForcePerformanceKey, !ForcePerformanceFeatures);

        [MenuItem(ForcePerformanceMenu, true)]
        private static bool ValidateForcePerformanceFeatures()
        {
            Menu.SetChecked(ForcePerformanceMenu, ForcePerformanceFeatures);
            return true;
        }

        [MenuItem("EarthVR/Steam Frame/Configure Android", priority = 20)]
        public static void ConfigureAndroid()
        {
            EarthVRSetupWizard.ConfigureAndroidPlayer();
            var loaderAssigned = EarthVRSetupWizard.ConfigureOpenXRForBuildTarget(BuildTargetGroup.Android);
            EditorUserBuildSettings.buildAppBundle = false;
            AssetDatabase.SaveAssets();

            var hasSteamFrameProfile = HasSteamFrameControllerProfile();
            if (!hasSteamFrameProfile)
            {
                Debug.LogWarning(
                    "Steam Frame Controller Profile is not available yet. Wait for Package Manager to finish importing " +
                    "com.valvesoftware.openxr.utils, then run this command again.");
            }

            if (loaderAssigned && hasSteamFrameProfile)
            {
                Debug.Log(
                    "Steam Frame Android configuration is ready: Vulkan, ARM64, IL2CPP, API 29+, OpenXR, " +
                    "single-pass multiview, foveated rendering, and the Steam Frame controller profile.");
            }
        }

        [MenuItem("EarthVR/Steam Frame/Build Development APK", priority = 21)]
        public static void BuildDevelopmentApk()
        {
            BuildApk(true);
        }

        [MenuItem("EarthVR/Steam Frame/Build Release APK", priority = 22)]
        public static void BuildReleaseApk()
        {
            WithPrivateCredentialsExcluded(() => BuildApk(false));
        }

        public static void WithPrivateCredentialsExcluded(Action build)
        {
            // Keep the developer's files intact, but never copy them into a
            // distributable APK. Restore them even if Unity reports a failure.
            var backup = Path.Combine("Library", "EarthVRPrivateBuildBackup");
            Directory.CreateDirectory(backup);
            if (Directory.EnumerateFiles(backup).Any())
                throw new InvalidOperationException("A previous private-file backup needs restoring from Library/EarthVRPrivateBuildBackup before building.");
            var moved = new System.Collections.Generic.List<string>();
            try
            {
                foreach (var name in new[] { "cesium-ion.local.json", "google-maps.local.json" })
                    foreach (var suffix in new[] { "", ".meta" })
                    {
                        var path = Path.Combine("Assets", "StreamingAssets", "EarthVR", name + suffix);
                        if (!File.Exists(path)) continue;
                        File.Move(path, Path.Combine(backup, name + suffix));
                        moved.Add(path);
                    }
                AssetDatabase.Refresh();
                build();
            }
            finally
            {
                foreach (var path in moved)
                    File.Move(Path.Combine(backup, Path.GetFileName(path)), path);
                AssetDatabase.Refresh();
            }
        }

        private static void BuildApk(bool development)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                throw new InvalidOperationException(
                    "Android Build Support is not installed for this Unity editor. In Unity Hub, add Android Build " +
                    "Support plus the Android SDK & NDK Tools and OpenJDK modules for Unity 6000.3.14f1.");
            }

            ConfigureAndroid();
            PlayerSettings.bundleVersion = development
                ? EarthVR.Core.ReleaseBuildStamp.Version + "+" + FeatureTag()
                : EarthVR.Core.ReleaseBuildStamp.Version;
            PlayerSettings.Android.bundleVersionCode = EarthVR.Core.ReleaseBuildStamp.AndroidVersionCode;
            if (!HasSteamFrameControllerProfile())
            {
                throw new InvalidOperationException(
                    "Valve OpenXR Utilities have not finished importing. Resolve packages, then run the build again.");
            }

            EnsureBuildScene();
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unity could not switch the active build target to Android.");

            var absoluteOutputPath = Path.GetFullPath(OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutputPath) ?? "Builds");

            var options = development
                ? BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler
                : BuildOptions.CompressWithLz4HC;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = absoluteOutputPath,
                targetGroup = BuildTargetGroup.Android,
                target = BuildTarget.Android,
                options = options
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Steam Frame APK build failed with {report.summary.totalErrors} error(s). See the Unity Console.");
            }

            Debug.Log(
                $"Steam Frame APK built successfully: {absoluteOutputPath} " +
                $"({report.summary.totalSize / (1024f * 1024f):F1} MiB)");
        }

        private static bool HasSteamFrameControllerProfile()
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            return settings != null && settings.GetFeatures()
                .Any(feature => feature.GetType().Name == "SteamFrameControllerProfile" && feature.enabled);
        }

        private static void EnsureBuildScene()
        {
            if (!File.Exists(ScenePath))
                throw new FileNotFoundException("Run EarthVR > Setup Project and Main Scene first.", ScenePath);

            if (EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == ScenePath))
                return;

            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
