using UnityEngine;
using UnityEngine.InputSystem;

namespace EarthVR.Input
{
    public interface IEarthVRInput
    {
        Vector2 Fly { get; }
        bool SelectPressed { get; }
        bool LeftTriggerHeld { get; }
        bool RightTriggerHeld { get; }
        bool LeftGripHeld { get; }
        bool RightGripHeld { get; }
        bool BoostHeld { get; }
        bool ToggleGlobeOverviewPressed { get; }
        bool OpenMenuPressed { get; }
        bool ResetViewPressed { get; }
        bool ToggleMovementModePressed { get; }
        InputAction HeadPositionAction { get; }
        InputAction HeadRotationAction { get; }
        InputAction LeftPositionAction { get; }
        InputAction LeftRotationAction { get; }
        InputAction RightPositionAction { get; }
        InputAction RightRotationAction { get; }
    }
}
