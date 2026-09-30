#if EARTHVR_STEAMVR
using System;
using System.Text;
using UnityEngine;
using Valve.VR;

namespace EarthVR.UI
{
    /// <summary>
    /// Shows SteamVR's own system keyboard overlay via the legacy OpenVR API
    /// (IVROverlay), which is separate from the OpenXR runtime this app
    /// otherwise uses for rendering/tracking. Opens a lightweight,
    /// overlay-only OpenVR connection alongside the running OpenXR session —
    /// SteamVR supports both at once, so this does not touch tracking or
    /// rendering. Requires the SteamVR Unity Plugin (Assets/SteamVR) and the
    /// EARTHVR_STEAMVR scripting define symbol (Project Settings > Player >
    /// Other Settings > Scripting Define Symbols); see docs/KNOWN_LIMITATIONS.md.
    /// </summary>
    public sealed class SteamVrSystemKeyboardProvider : ISystemKeyboardProvider, IDisposable
    {
        private const uint MaximumCharacters = 256;
        private bool _initAttempted;
        private bool _initSucceeded;
        private bool _showing;
        private Action<string> _onTextChanged;
        private Action _onDone;
        private string _lastText = string.Empty;
        private readonly StringBuilder _textBuffer = new((int)MaximumCharacters);
        private KeyboardEventPump _pump;

        public bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                return _initSucceeded && OpenVR.Overlay != null && OpenVR.System != null;
            }
        }

        private void EnsureInitialized()
        {
            if (_initAttempted)
                return;
            _initAttempted = true;

            var error = EVRInitError.None;
            // VRApplication_Overlay: a companion connection into the already-
            // running SteamVR instance for overlay-only calls, without trying
            // to take over the scene/HMD that OpenXR already owns.
            OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
            if (error != EVRInitError.None)
            {
                Debug.LogWarning($"EarthVR: SteamVR overlay keyboard unavailable ({error}); using the built-in keyboard instead.");
                return;
            }
            _initSucceeded = true;
        }

        public void Show(string initialText, Action<string> onTextChanged, Action onDone)
        {
            if (!IsAvailable)
                return;

            _onTextChanged = onTextChanged;
            _onDone = onDone;
            _lastText = initialText ?? string.Empty;

            var result = OpenVR.Overlay.ShowKeyboard(
                (int)EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                (int)EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine,
                0,
                "Search for a location",
                MaximumCharacters,
                _lastText,
                0);
            if (result != EVROverlayError.None)
            {
                Debug.LogWarning($"EarthVR: SteamVR ShowKeyboard failed ({result}); using the built-in keyboard instead.");
                return;
            }

            _showing = true;
            EnsurePump();
        }

        public void Hide()
        {
            if (!_showing)
                return;
            _showing = false;
            OpenVR.Overlay?.HideKeyboard();
        }

        private void EnsurePump()
        {
            if (_pump != null)
                return;
            var pumpObject = new GameObject("SteamVR Keyboard Event Pump") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(pumpObject);
            _pump = pumpObject.AddComponent<KeyboardEventPump>();
            _pump.Configure(this);
        }

        private void PollOnce()
        {
            if (!_showing || OpenVR.Overlay == null)
                return;

            var length = OpenVR.Overlay.GetKeyboardText(_textBuffer, MaximumCharacters);
            var text = _textBuffer.ToString(0, Mathf.Min((int)length, _textBuffer.Length));
            if (text != _lastText)
            {
                _lastText = text;
                _onTextChanged?.Invoke(text);
            }

            var evt = default(VREvent_t);
            var eventSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VREvent_t));
            while (OpenVR.System != null && OpenVR.System.PollNextEvent(ref evt, eventSize))
            {
                if (evt.eventType == (uint)EVREventType.VREvent_KeyboardClosed ||
                    evt.eventType == (uint)EVREventType.VREvent_KeyboardDone)
                {
                    _showing = false;
                    _onDone?.Invoke();
                }
            }
        }

        public void Dispose()
        {
            if (_pump != null)
                UnityEngine.Object.Destroy(_pump.gameObject);
            if (_initSucceeded)
                OpenVR.Shutdown();
        }

        private sealed class KeyboardEventPump : MonoBehaviour
        {
            private SteamVrSystemKeyboardProvider _owner;
            public void Configure(SteamVrSystemKeyboardProvider owner) => _owner = owner;
            private void Update() => _owner?.PollOnce();
            private void OnApplicationQuit() => _owner?.Dispose();
        }
    }
}
#endif
