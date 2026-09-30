using System.Text.RegularExpressions;

namespace EarthVR.Editor
{
    public static class PublicReleasePolicy
    {
        public static bool ContainsSerializedCredential(string content)
        {
            foreach (Match match in Regex.Matches(content,
                @"(?m)^[ \t]*(?:_?ionAccessToken|_?defaultIonAccessToken|accessToken|apiKey):[ \t]*([^\r\n]*)$"))
            {
                var value = match.Groups[1].Value.Trim().Trim('"', '\'');
                if (value.Length > 0 && value != "null") return true;
            }
            return false;
        }
    }
}
