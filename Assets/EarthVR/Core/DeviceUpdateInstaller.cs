using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace EarthVR.Core
{
    /// <summary>Download in the game; replace host files only after the game exits.</summary>
    public sealed class DeviceUpdateInstaller : MonoBehaviour
    {
        [Serializable] private sealed class Bridge { public int schema; public string platform; }
        [Serializable] private sealed class Request
        {
            public int schema = 1;
            public string id, version, currentVersion, platform, digest;
        }
        [Serializable] private sealed class Result { public string id, state, message; }
        public bool IsBusy { get; private set; }
        public string Status { get; private set; }
        public event Action Changed;
        private float _heartbeatTime;
        private void Start()
        {
            try
            {
                var result = JsonUtility.FromJson<Result>(File.ReadAllText(Path.Combine(Inbox, "result.json")));
                if (result?.state == "error") SetStatus("Previous update: " + result.message);
                else if (result?.state == "installed") SetStatus("Update installed · " + ReleaseBuildStamp.Version);
            }
            catch (Exception) { }
        }
        private string Inbox => Path.Combine(Application.persistentDataPath, "EarthVR", "Updates");
        public static string Platform => Application.platform == RuntimePlatform.Android ? "apk" : "windows";
        public static string AssetName => Platform == "apk" ? "FrameEarthVR.apk" : "FrameEarthVR-Windows.zip";
        [DllImport("ntdll.dll", EntryPoint = "wine_get_version")] private static extern IntPtr WineVersion();
        private static bool NeedsHostHelper
        {
            get
            {
                if (Application.platform == RuntimePlatform.Android) return true;
                if (Application.platform != RuntimePlatform.WindowsPlayer) return true;
                try { return WineVersion() != IntPtr.Zero; }
                catch (DllNotFoundException) { return false; }
                catch (EntryPointNotFoundException) { return false; }
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _heartbeatTime) return;
            _heartbeatTime = Time.unscaledTime + 1f;
            try
            {
                Directory.CreateDirectory(Inbox);
                File.WriteAllText(Path.Combine(Inbox, "game-running"), "running");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Install(PublishedRelease release)
        {
            if (IsBusy || release == null) return;
            var asset = ReleaseUpdatePolicy.FindAsset(release, AssetName);
            if (!ReleaseUpdatePolicy.HasDigest(asset)) { SetStatus("This release has no verified update download"); return; }
            if (Application.isEditor) { SetStatus("Install updates from a built game"); return; }
            if (NeedsHostHelper && !HasHostHelper()) { SetStatus("Run the one-time Frame updater setup first"); return; }
            if (!NeedsHostHelper && !File.Exists(Path.Combine(Application.streamingAssetsPath, "EarthVR", "ApplyWindowsUpdate.ps1")))
            { SetStatus("Windows updater is missing; reinstall this build"); return; }
            IsBusy = true;
            StartCoroutine(GuardedDownload(release, asset));
        }

        private IEnumerator GuardedDownload(PublishedRelease release, ReleaseAsset asset)
        {
            var operation = Download(release, asset);
            while (true)
            {
                bool more;
                object current = null;
                try { more = operation.MoveNext(); if (more) current = operation.Current; }
                catch (Exception) { IsBusy = false; SetStatus("Update failed; check free space and try again"); break; }
                if (!more) break;
                yield return current;
            }
            (operation as IDisposable)?.Dispose();
        }

        private bool HasHostHelper()
        {
            try
            {
                var path = Path.Combine(Inbox, "bridge.json");
                if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds > 15) return false;
                var bridge = JsonUtility.FromJson<Bridge>(File.ReadAllText(path));
                return bridge.schema == 1 && bridge.platform == Platform;
            }
            catch (Exception) { return false; }
        }

        private void SetStatus(string text) { Status = text; Changed?.Invoke(); }
        private IEnumerator Download(PublishedRelease release, ReleaseAsset asset)
        {
            string storageError = null;
            try { Directory.CreateDirectory(Inbox); }
            catch (Exception) { storageError = "Cannot write update files; check storage permissions"; }
            if (storageError != null) { IsBusy = false; SetStatus(storageError); yield break; }
            var payload = Path.Combine(Inbox, Platform == "apk" ? "payload.apk" : "payload.zip");
            var temporary = payload + ".part";
            using (var request = UnityWebRequest.Get(asset.browser_download_url))
            {
                request.downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = true };
                request.timeout = 300;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    SetStatus("Downloading update · " + Mathf.RoundToInt(request.downloadProgress * 100f) + "%");
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                if (request.result != UnityWebRequest.Result.Success)
                { IsBusy = false; SetStatus("Download failed; retry when connected"); yield break; }
            }
            SetStatus("Verifying update…");
            var verify = Task.Run(() =>
            {
                using (var file = File.OpenRead(temporary))
                using (var sha = SHA256.Create())
                    return file.Length == asset.size &&
                        "sha256:" + BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant() == asset.digest.ToLowerInvariant();
            });
            while (!verify.IsCompleted) yield return null;
            if (verify.IsFaulted || !verify.Result)
            { IsBusy = false; SetStatus("Update verification failed; nothing installed"); yield break; }
            string preparationError = null;
            var update = new Request { id = Guid.NewGuid().ToString("N"), version = release.tag_name,
                currentVersion = ReleaseBuildStamp.Version, platform = Platform, digest = asset.digest.ToLowerInvariant() };
            try
            {
                if (File.Exists(payload)) File.Delete(payload);
                File.Move(temporary, payload);
                var requestPath = Path.Combine(Inbox, "request.json");
                File.WriteAllText(requestPath + ".tmp", JsonUtility.ToJson(update));
                if (File.Exists(requestPath)) File.Delete(requestPath);
                File.Move(requestPath + ".tmp", requestPath);
                if (!NeedsHostHelper) LaunchWindowsHelper();
            }
            catch (Exception) { preparationError = "Could not stage update; check free space and folder permissions"; }
            if (preparationError != null) { IsBusy = false; SetStatus(preparationError); yield break; }
            SetStatus("Preparing update; waiting for updater…");
            var deadline = Time.unscaledTime + 45f;
            while (Time.unscaledTime < deadline)
            {
                Result result = null;
                try { result = JsonUtility.FromJson<Result>(File.ReadAllText(Path.Combine(Inbox, "result.json"))); }
                catch (Exception) { }
                if (result != null && result.id == update.id)
                {
                    if (result.state == "ready")
                    {
                        SetStatus("Update ready · closing game. Reopen from your library.");
                        yield return new WaitForSecondsRealtime(1f);
                        Application.Quit();
                        yield break;
                    }
                    if (result.state == "error") { IsBusy = false; SetStatus(result.message); yield break; }
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            // Remove an unaccepted request before allowing a retry; never exit the game on timeout.
            try { File.Delete(Path.Combine(Inbox, "request.json")); } catch (IOException) { }
            IsBusy = false; SetStatus("Updater did not respond; game remains open");
        }

        private void LaunchWindowsHelper()
        {
            var script = Path.Combine(Inbox, "ApplyWindowsUpdate.ps1");
            File.Copy(Path.Combine(Application.streamingAssetsPath, "EarthVR", "ApplyWindowsUpdate.ps1"), script, true);
            var root = Path.GetDirectoryName(Application.dataPath);
            var args = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script) + " -Inbox " + Quote(Inbox) +
                " -InstallRoot " + Quote(root) + " -GamePid " + Process.GetCurrentProcess().Id;
            Process.Start(new ProcessStartInfo("powershell.exe", args)
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        private static string Quote(string value) => "\"" + value.Replace("\"", "") + "\"";
    }
}
