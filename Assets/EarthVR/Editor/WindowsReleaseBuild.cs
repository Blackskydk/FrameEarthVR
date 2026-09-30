using System;
using System.IO;
using System.Linq;
using EarthVR.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthVR.Editor
{
    public static class WindowsReleaseBuild
    {
        [MenuItem("EarthVR/Windows/Build Release", priority = 23)]
        public static void BuildRelease()
        {
            SteamFrameBuild.WithPrivateCredentialsExcluded(Build);
        }

        private static void Build()
        {
            PlayerSettings.bundleVersion = ReleaseBuildStamp.Version;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            EarthVRSetupWizard.ConfigureOpenXRForBuildTarget(BuildTargetGroup.Standalone);
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Unity could not select Windows x64");
            var output = Path.GetFullPath("Builds/Windows/FrameEarthVR.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = output, target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.CompressWithLz4HC
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows release build failed; see the Unity log");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "earthvr-version.txt"), ReleaseBuildStamp.Version);
            Debug.Log("Windows release built: " + output);
        }
    }
}
