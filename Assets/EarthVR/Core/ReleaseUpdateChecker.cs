using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace EarthVR.Core
{
    public sealed class ReleaseUpdateChecker : MonoBehaviour
    {
        [Serializable] private sealed class ReleaseList { public PublishedRelease[] releases; }
        private bool _isChecking;
        private float _nextCheckAllowed;
        private DeviceUpdateInstaller _installer;
        public bool IsChecking => _isChecking;
        public bool IsUpdating => _installer != null && _installer.IsBusy;
        public PublishedRelease AvailableRelease { get; private set; }
        public string Status { get; private set; } = "Startup update check pending";
        public event Action Changed;

        private void Awake()
        {
            _installer = gameObject.AddComponent<DeviceUpdateInstaller>();
            _installer.Changed += () => { Status = _installer.Status; Changed?.Invoke(); };
        }

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(8f);
            CheckNow();
        }

        public void CheckNow()
        {
            if (IsChecking || IsUpdating || Time.unscaledTime < _nextCheckAllowed) return;
            _nextCheckAllowed = Time.unscaledTime + 60f;
            _isChecking = true;
            StartCoroutine(Check());
        }

        private IEnumerator Check()
        {
            Status = "Checking for updates…";
            Changed?.Invoke();
            using (var request = UnityWebRequest.Get("https://api.github.com/repos/Blackskydk/FrameEarthVR/releases?per_page=100"))
            {
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "FrameEarthVR/" + ReleaseBuildStamp.Version);
                request.SetRequestHeader("X-GitHub-Api-Version", "2026-03-10");
                request.timeout = 12;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                    Status = request.responseCode == 404 ? "Update feed unavailable (private repository)"
                        : request.responseCode == 403 || request.responseCode == 429 ? "Update check rate limited; try later"
                        : "Update check unavailable; try again later";
                else
                {
                    try
                    {
                        var response = request.downloadHandler.text;
                        if (!response.TrimStart().StartsWith("[", StringComparison.Ordinal)) throw new FormatException();
                        var list = JsonUtility.FromJson<ReleaseList>("{\"releases\":" + response + "}");
                        AvailableRelease = ReleaseUpdatePolicy.FindUpdate(ReleaseBuildStamp.Version, list?.releases, DeviceUpdateInstaller.AssetName);
                        Status = AvailableRelease == null ? "Up to date · " + ReleaseBuildStamp.Version
                            : "Update available: " + AvailableRelease.tag_name;
                    }
                    catch (Exception) { Status = "Could not read update information"; }
                }
            }
            _isChecking = false;
            Changed?.Invoke();
        }

        public void OpenAvailableRelease()
        {
            if (AvailableRelease != null)
                _installer.Install(AvailableRelease);
            else CheckNow();
        }
    }
}
