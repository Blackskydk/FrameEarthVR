using UnityEngine;
using UnityEngine.InputSystem;

namespace EarthVR.Core
{
    public sealed class TrackedActionPose : MonoBehaviour
    {
        private InputAction _position;
        private InputAction _rotation;

        public void Configure(InputAction position, InputAction rotation)
        {
            _position = position;
            _rotation = rotation;
            ApplyPose();
        }

        private void OnEnable()
        {
            // XR devices receive a second Input System update immediately before
            // rendering. Applying both updates keeps gameplay current and gives the
            // compositor the freshest predicted head/controller pose.
            InputSystem.onAfterUpdate += ApplyPose;
        }

        private void OnDisable()
        {
            InputSystem.onAfterUpdate -= ApplyPose;
        }

        private void ApplyPose()
        {
            if (_position != null)
                transform.localPosition = _position.ReadValue<Vector3>();
            if (_rotation == null)
                return;
            var value = _rotation.ReadValue<Quaternion>();
            var squaredMagnitude = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            if (squaredMagnitude > 0.5f)
                transform.localRotation = value;
        }
    }
}
