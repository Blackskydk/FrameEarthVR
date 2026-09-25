using UnityEngine;
using UnityEngine.InputSystem;

namespace EarthVR.Input
{
    /// <summary>
    /// Reads logical actions only. Navigation code never queries a controller layout or button.
    /// Rebind the Input Actions asset to add future Steam Frame controller paths.
    /// </summary>
    public sealed class OpenXRInputReader : MonoBehaviour, IEarthVRInput
    {
        private InputActionAsset _asset;
        private InputAction _fly;
        private InputAction _select;
        private InputAction _leftTrigger;
        private InputAction _rightTrigger;
        private InputAction _leftGrip;
        private InputAction _rightGrip;
        private InputAction _boost;
        private InputAction _openMenu;
        private InputAction _resetView;
        private InputAction _toggleMode;

        public Vector2 Fly => _fly?.ReadValue<Vector2>() ?? Vector2.zero;
        public bool SelectPressed => _select?.WasPressedThisFrame() ?? false;
        public bool LeftTriggerHeld => _leftTrigger?.IsPressed() ?? false;
        public bool RightTriggerHeld => _rightTrigger?.IsPressed() ?? false;
        public bool LeftGripHeld => _leftGrip?.IsPressed() ?? false;
        public bool RightGripHeld => _rightGrip?.IsPressed() ?? false;
        public bool BoostHeld => _boost?.IsPressed() ?? false;
        public bool OpenMenuPressed => _openMenu?.WasPressedThisFrame() ?? false;
        public bool ResetViewPressed => _resetView?.WasPressedThisFrame() ?? false;
        public bool ToggleMovementModePressed => _toggleMode?.WasPressedThisFrame() ?? false;
        public InputAction HeadPositionAction { get; private set; }
        public InputAction HeadRotationAction { get; private set; }
        public InputAction LeftPositionAction { get; private set; }
        public InputAction LeftRotationAction { get; private set; }
        public InputAction RightPositionAction { get; private set; }
        public InputAction RightRotationAction { get; private set; }

        public void Initialize(InputActionAsset source)
        {
            if (source == null)
            {
                Debug.LogError("EarthVRInputActions was not found in Resources.");
                enabled = false;
                return;
            }

            _asset = Instantiate(source);
            _asset.name = source.name + " (Runtime)";
            _fly = Find("Fly");
            _select = Find("Select");
            _leftTrigger = Find("LeftTrigger");
            _rightTrigger = Find("RightTrigger");
            _leftGrip = Find("LeftGrab");
            _rightGrip = Find("RightGrab");
            _boost = Find("Boost");
            _openMenu = Find("OpenMenu");
            _resetView = Find("ResetView");
            _toggleMode = Find("ToggleMovementMode");
            HeadPositionAction = Find("HeadPosition");
            HeadRotationAction = Find("HeadRotation");
            LeftPositionAction = Find("LeftPosition");
            LeftRotationAction = Find("LeftRotation");
            RightPositionAction = Find("RightPosition");
            RightRotationAction = Find("RightRotation");
            _asset.Enable();
        }

        private InputAction Find(string actionName)
        {
            var action = _asset.FindAction("EarthVR/" + actionName, true);
            return action;
        }

        private void OnDestroy()
        {
            if (_asset == null)
                return;
            _asset.Disable();
            Destroy(_asset);
        }
    }
}
