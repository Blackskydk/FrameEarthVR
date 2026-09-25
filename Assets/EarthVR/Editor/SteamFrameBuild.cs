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
            BuildApk(false);
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
