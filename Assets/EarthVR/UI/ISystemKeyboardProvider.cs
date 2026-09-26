using System;

namespace EarthVR.UI
{
    /// <summary>
    /// Abstracts showing a native platform text-entry keyboard (for example the
    /// SteamVR/OpenVR system keyboard) instead of EarthVR's own built-in
    /// on-screen one. <see cref="NullSystemKeyboardProvider"/> always reports
    /// unavailable, so callers fall back to the built-in keyboard by default.
    ///
    /// Wiring up the real Steam keyboard means adding the SteamVR/OpenVR Unity
    /// plugin to the project, implementing this interface against
    /// Valve.VR.OpenVR.Overlay's ShowKeyboard/GetKeyboardText/HideKeyboard calls
    /// (or the equivalent SteamVR Input keyboard API), and passing that
    /// implementation into EarthVRWristMenu.Initialize instead of the null one.
    /// That plugin is not part of this project, so this is left as a clean
    /// extension point rather than an unverified guess at its API.
    /// </summary>
    public interface ISystemKeyboardProvider
    {
        bool IsAvailable { get; }

        /// <param name="initialText">Text to prefill the keyboard with.</param>
        /// <param name="onTextChanged">Invoked as the user types.</param>
        /// <param name="onDone">Invoked when the user submits/closes the keyboard.</param>
        void Show(string initialText, Action<string> onTextChanged, Action onDone);

        void Hide();
    }

    public sealed class NullSystemKeyboardProvider : ISystemKeyboardProvider
    {
        public bool IsAvailable => false;

        public void Show(string initialText, Action<string> onTextChanged, Action onDone)
        {
        }

        public void Hide()
        {
        }
    }
}
