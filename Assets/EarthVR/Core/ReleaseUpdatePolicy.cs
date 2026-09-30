using System;

namespace EarthVR.Core
{
    [Serializable]
    public sealed class PublishedRelease
    {
        public string tag_name;
        public string html_url;
        public bool draft;
        public bool prerelease;
        public ReleaseAsset[] assets;
    }

    [Serializable]
    public sealed class ReleaseAsset
    {
        public string name;
        public string state;
        public long size;
        public string browser_download_url;
        public string digest;
    }

    public static class ReleaseUpdatePolicy
    {
        public const string RepositoryUrl = "https://github.com/Blackskydk/FrameEarthVR";

        public static PublishedRelease FindUpdate(string installed, PublishedRelease[] releases, string assetName = "FrameEarthVR.apk")
        {
            if (!ReleaseVersion.TryParse(installed, out var current) || releases == null) return null;
            PublishedRelease best = null;
            var bestVersion = current;
            foreach (var release in releases)
            {
                if (release == null || release.draft || !ReleaseVersion.TryParse(release.tag_name, out var candidate) ||
                    (!current.IsPrerelease && (release.prerelease || candidate.IsPrerelease)) ||
                    candidate.CompareTo(bestVersion) <= 0 || FindAsset(release, assetName) == null || !IsReleaseUrl(release.html_url)) continue;
                best = release;
                bestVersion = candidate;
            }
            return best;
        }

        public static bool IsReleaseUrl(string url) => IsRepositoryPath(url, "/releases/tag/");

        private static bool IsRepositoryPath(string url, string suffix)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
                uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort &&
                uri.UserInfo.Length == 0 && uri.AbsolutePath.StartsWith("/Blackskydk/FrameEarthVR" + suffix, StringComparison.Ordinal);
        }

        public static ReleaseAsset FindAsset(PublishedRelease release, string assetName)
        {
            if (release?.assets == null) return null;
            foreach (var asset in release.assets)
                if (asset != null && asset.name == assetName && asset.state == "uploaded" && asset.size > 0 &&
                    asset.size <= 2147483648L && IsRepositoryPath(asset.browser_download_url, "/releases/download/")) return asset;
            return null;
        }

        public static bool HasDigest(ReleaseAsset asset) => asset != null &&
            System.Text.RegularExpressions.Regex.IsMatch(asset.digest ?? "", @"\Asha256:[0-9a-fA-F]{64}\z");
    }
}
