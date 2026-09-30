using System.Collections;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Input;
using EarthVR.Navigation;
using EarthVR.Scaling;
using EarthVR.Terrain;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace EarthVR.UI
{
    /// <summary>First-run account setup usable with only the headset controllers.</summary>
    public sealed class CredentialSetupPanel : MonoBehaviour
    {
        private EarthVRRig _rig;
        private IEarthVRInput _input;
        private CesiumEarthProvider _earth;
        private NavigationController _navigation;
        private WorldManipulationController _scaling;
        private GameObject _panel;
        private Text _status;
        private Text _tokenLabel;
        private string _token = string.Empty;
        private bool _uppercase;
        private bool _triggerHeld;
        private bool _validating;
        private WorldSpaceButton _hovered;
        private TouchScreenKeyboard _keyboard;
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        public bool IsOpen => _panel != null && _panel.activeSelf;

        public void Initialize(EarthVRRig rig, IEarthVRInput input, CesiumEarthProvider earth,
            NavigationController navigation, WorldManipulationController scaling)
        {
            _rig = rig; _input = input; _earth = earth; _navigation = navigation; _scaling = scaling;
            _panel = new GameObject("Your Cesium Account", typeof(RectTransform), typeof(Canvas), typeof(Image));
            _panel.transform.SetParent(transform, false);
            var canvas = _panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = rig.Camera;
            _panel.GetComponent<RectTransform>().sizeDelta = new Vector2(900, 670);
            _panel.transform.localScale = Vector3.one * 0.001f;
            _panel.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.08f, 1f);
            TextAt("FRAME EARTH VR — YOUR ACCOUNT", 280, 29);
            TextAt("Use your own Cesium ion assets:read token for asset 2275207.\nRun setup-token.ps1 on your PC to save it on this device.\nOr use the controller keyboard below. Tokens survive updates.", 200, 20);
            _tokenLabel = TextAt("No token entered", 115, 23);
            ButtonAt(Application.platform == RuntimePlatform.Android ? "PC SETUP HELP" : "PASTE", -300, 50, PasteToken);
            ButtonAt("CLEAR", -100, 50, () => { _token = string.Empty; RefreshToken(); });
            ButtonAt("BACKSPACE", 100, 50, () => { if (_token.Length > 0) _token = _token[..^1]; RefreshToken(); });
            ButtonAt("a / A", 300, 50, () => { _uppercase = !_uppercase; RefreshToken(); });
            var rows = new[] { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm-_." };
            for (var row = 0; row < rows.Length; row++)
            {
                var keys = rows[row];
                for (var i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    ButtonAt(char.IsLetter(key) ? $"{key}/{char.ToUpperInvariant(key)}" : key.ToString(),
                        (i - (keys.Length - 1) * 0.5f) * 76, -20 - row * 58,
                        () => { if (_token.Length < 8192) _token += _uppercase ? char.ToUpperInvariant(key) : key; RefreshToken(); }, 68);
                }
            }
            ButtonAt("SAVE & START", -315, -260, () => { if (!_validating) StartCoroutine(ValidateAndSave()); }, 195);
            ButtonAt("SYSTEM KEYBOARD", -105, -260, OpenKeyboard, 195);
            ButtonAt("FORGET TOKEN", 105, -260, ForgetToken, 195);
            ButtonAt("CANCEL", 315, -260, () => { if (!_validating && _earth.HasAccessToken) Close(); }, 195);
            _status = TextAt("Tokens stay on this headset. They are never included in downloads.", -310, 18);
            _panel.SetActive(false);
            StartCoroutine(CheckFirstRun());
        }

        private IEnumerator CheckFirstRun()
        {
            yield return new WaitForSecondsRealtime(1f);
            if (!_earth.HasAccessToken) Open();
        }

        public void Open()
        {
            _token = string.Empty;
            RefreshToken();
            Recenter();
            _panel.SetActive(true);
            _navigation.NavigationEnabled = false;
            _scaling.InteractionsEnabled = false;
            _scaling.GroundedScalingEnabled = false;
            _triggerHeld = _input.RightTriggerHeld;
        }

        private void Recenter()
        {
            var forward = Vector3.ProjectOnPlane(_rig.Camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            _panel.transform.position = _rig.Camera.transform.position + forward * 1.1f;
            _panel.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void RefreshToken() => _tokenLabel.text = _token.Length == 0
            ? "No token entered" : $"Token entered: {_token.Length} characters (hidden) · Case: {(_uppercase ? "A" : "a")}";

        private void PasteToken()
        {
            // Lepton's clipboard bridge can terminate the native player. Do not
            // enter Unity's clipboard API on Android, even inside a try/catch.
            if (Application.platform == RuntimePlatform.Android)
            {
                _status.text = "Close the game, run setup-token.ps1 on your paired PC, then reopen from Steam.";
                return;
            }
            try
            {
                var value = (GUIUtility.systemCopyBuffer ?? string.Empty).Trim();
                if (!UserCredentials.IsUsable(value)) { _status.text = "Clipboard does not contain a valid token."; return; }
                _token = value;
                RefreshToken();
            }
            catch (System.Exception) { _status.text = "Clipboard unavailable. Use PC setup or the controller keyboard."; }
        }

        private void OpenKeyboard()
        {
            if (!TouchScreenKeyboard.isSupported) { _status.text = "Use Paste or the controller keyboard above."; return; }
            _keyboard = TouchScreenKeyboard.Open(_token, TouchScreenKeyboardType.ASCIICapable, false, false, true);
        }

        private void ForgetToken()
        {
            if (_validating) return;
            try { UserCredentials.ForgetToken(); _earth.ReloadUserCredentials(); _token = string.Empty; RefreshToken(); _status.text = "Saved token removed. Enter a new token to continue."; }
            catch (System.Exception) { _status.text = "Could not remove the saved token."; }
        }

        private IEnumerator ValidateAndSave()
        {
            var token = _token.Trim();
            if (!UserCredentials.IsUsable(token)) { _status.text = "Enter a token without spaces or placeholder text."; yield break; }
            _validating = true;
            _status.text = "Checking access to Google Photorealistic 3D Tiles…";
            using (var request = UnityWebRequest.Get("https://api.cesium.com/v1/assets/2275207/endpoint"))
            {
                request.SetRequestHeader("Authorization", "Bearer " + token);
                request.timeout = 20;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    _status.text = $"Access check failed (HTTP {request.responseCode}). Check network, assets:read, and asset 2275207.";
                    _validating = false;
                    yield break;
                }
            }
            try { UserCredentials.SaveToken(token); }
            catch (System.Exception) { _status.text = "Could not save the token on this device."; _validating = false; yield break; }
            _earth.ReloadUserCredentials();
            Close();
            _validating = false;
        }

        private void Close()
        {
            _token = string.Empty;
            if (_keyboard != null) _keyboard.active = false;
            _keyboard = null;
            _hovered?.SetPointed(false);
            _hovered = null;
            _panel.SetActive(false);
            _navigation.NavigationEnabled = true;
            _scaling.InteractionsEnabled = true;
            _scaling.GroundedScalingEnabled = true;
        }

        private void Update()
        {
            if (!IsOpen) return;
            // Mode transitions may restore these flags: setup retains pointer ownership.
            _navigation.NavigationEnabled = false;
            _scaling.InteractionsEnabled = false;
            _scaling.GroundedScalingEnabled = false;
            if (_keyboard != null)
            {
                _token = _keyboard.text;
                RefreshToken();
                if (!_keyboard.active) _keyboard = null;
            }
            Physics.SyncTransforms();
            var count = Physics.RaycastNonAlloc(_rig.RightController.position, _rig.RightController.forward,
                _hits, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            WorldSpaceButton nearest = null;
            var distance = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (!hit.transform.IsChildOf(_panel.transform) || hit.distance >= distance) continue;
                var button = hit.collider.GetComponent<WorldSpaceButton>();
                if (button == null) continue;
                nearest = button; distance = hit.distance;
            }
            if (nearest != _hovered) { _hovered?.SetPointed(false); _hovered = nearest; _hovered?.SetPointed(true); }
            var held = _input.RightTriggerHeld;
            if (held && !_triggerHeld) _hovered?.Invoke();
            _triggerHeld = held;
            if (_input.OpenMenuPressed) Recenter();
        }

        private Text TextAt(string value, float y, int size)
        {
            var item = new GameObject("Setup Text", typeof(RectTransform), typeof(Text));
            item.transform.SetParent(_panel.transform, false);
            var rect = item.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(860, 80); rect.anchoredPosition = new Vector2(0, y);
            var text = item.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value; text.fontSize = size; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false;
            return text;
        }

        private void ButtonAt(string label, float x, float y, UnityEngine.Events.UnityAction action, float width = 180)
        {
            var item = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(BoxCollider), typeof(WorldSpaceButton));
            item.transform.SetParent(_panel.transform, false);
            var rect = item.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(width, 48); rect.anchoredPosition = new Vector2(x, y);
            item.GetComponent<Image>().color = new Color(0.06f, 0.2f, 0.3f, 1f);
            var button = item.GetComponent<Button>(); button.targetGraphic = item.GetComponent<Image>(); button.transition = Selectable.Transition.None; button.onClick.AddListener(action);
            item.GetComponent<BoxCollider>().size = new Vector3(width, 48, 10);
            var text = TextAt(label, 0, 18); text.transform.SetParent(item.transform, false); text.rectTransform.sizeDelta = new Vector2(width - 8, 46);
            item.GetComponent<WorldSpaceButton>().Configure(button);
        }
    }
}
