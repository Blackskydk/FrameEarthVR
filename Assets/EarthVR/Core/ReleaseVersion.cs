using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EarthVR.Core
{
    public static class ReleaseBuildStamp
    {
        public const string Version = "1.0.0-preview.5";
        public const int AndroidVersionCode = 5;
    }

    /// <summary>Semantic release comparison, including preview.9 versus preview.10.</summary>
    public sealed class ReleaseVersion : IComparable<ReleaseVersion>
    {
        private readonly int[] _core;
        private readonly string[] _preview;
        public bool IsPrerelease => _preview.Length > 0;

        private ReleaseVersion(int[] core, string[] preview) { _core = core; _preview = preview; }

        public static bool TryParse(string text, out ReleaseVersion version)
        {
            version = null;
            if (string.IsNullOrEmpty(text) || text.Length > 128) return false;
            var match = Regex.Match(text, @"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$");
            if (!match.Success) return false;
            var core = new int[3];
            for (var i = 0; i < 3; i++)
                if (!int.TryParse(match.Groups[i + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out core[i])) return false;
            var preview = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : Array.Empty<string>();
            foreach (var identifier in preview)
                if (IsNumeric(identifier) && identifier.Length > 1 && identifier[0] == '0') return false;
            version = new ReleaseVersion(core, preview);
            return true;
        }

        private static bool IsNumeric(string value)
        {
            foreach (var character in value) if (character < '0' || character > '9') return false;
            return value.Length > 0;
        }

        public int CompareTo(ReleaseVersion other)
        {
            if (other == null) return 1;
            for (var i = 0; i < 3; i++)
            {
                var comparison = _core[i].CompareTo(other._core[i]);
                if (comparison != 0) return comparison;
            }
            if (!IsPrerelease || !other.IsPrerelease)
                return IsPrerelease == other.IsPrerelease ? 0 : (IsPrerelease ? -1 : 1);
            for (var i = 0; i < Math.Min(_preview.Length, other._preview.Length); i++)
            {
                var left = _preview[i]; var right = other._preview[i];
                var leftNumeric = IsNumeric(left); var rightNumeric = IsNumeric(right);
                var comparison = leftNumeric && rightNumeric
                    ? (left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right))
                    : (leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1) : string.CompareOrdinal(left, right));
                if (comparison != 0) return comparison;
            }
            return _preview.Length.CompareTo(other._preview.Length);
        }
    }
}
