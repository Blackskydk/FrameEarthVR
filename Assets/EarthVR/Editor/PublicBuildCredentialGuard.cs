using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace EarthVR.Editor
{
    /// <summary>Also protects release builds started through Unity's Build Profiles.</summary>
    public sealed class PublicBuildCredentialGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if ((report.summary.options & BuildOptions.Development) != 0) return;
            if (Directory.Exists("Assets/StreamingAssets"))
                foreach (var path in Directory.EnumerateFiles("Assets/StreamingAssets", "*.local.json", SearchOption.AllDirectories))
                    throw new BuildFailedException("Release build contains local credentials. Use EarthVR > Steam Frame > Build Release APK to exclude them safely. File: " + path);
            foreach (var path in Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories))
            {
                if (!path.EndsWith(".asset") && !path.EndsWith(".unity") && !path.EndsWith(".prefab")) continue;
                var content = File.ReadAllText(path);
                if (PublicReleasePolicy.ContainsSerializedCredential(content))
                    throw new BuildFailedException("Remove serialized credentials from this asset before sharing a release: " + path);
            }
        }
    }
}
