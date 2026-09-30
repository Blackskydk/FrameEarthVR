using EarthVR.Core;
using EarthVR.Scaling;
using UnityEngine;
using UnityEngine.Rendering;

namespace EarthVR.Navigation
{
    /// <summary>Owns the car's lifecycle and a lightweight VR cockpit. Actual
    /// vehicle motion lives in NavigationController so geographic position,
    /// speed, rebasing, comfort effects, and saved viewpoints share one source.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class CarModeController : MonoBehaviour
    {
        private EarthVRRig _rig;
        private NavigationController _navigation;
        private WorldManipulationController _scaling;
        private MovementMode _modeBeforeCar = MovementMode.Grounded;
        private GameObject _vehicleRoot;

        public bool IsActive => _navigation != null &&
                                _navigation.State.Mode == MovementMode.Car;

        public void Initialize(
            EarthVRRig rig,
            NavigationController navigation,
            WorldManipulationController scaling)
        {
            _rig = rig;
            _navigation = navigation;
            _scaling = scaling;
            CreateVehicleVisual();
            _navigation.State.ModeChanged += OnModeChanged;
            OnModeChanged(_navigation.State.Mode);
        }

        public void Toggle()
        {
            if (_navigation == null || !_navigation.NavigationEnabled)
                return;
            if (IsActive)
            {
                _navigation.State.SetMode(_modeBeforeCar == MovementMode.Car
                    ? MovementMode.Grounded
                    : _modeBeforeCar);
                return;
            }

            _modeBeforeCar = _navigation.State.Mode;
            _navigation.State.SetMode(MovementMode.Car);
        }

        private void OnModeChanged(MovementMode mode)
        {
            var active = mode == MovementMode.Car;
            if (_vehicleRoot != null)
            {
                if (active)
                    AnchorVehicleAroundViewer();
                _vehicleRoot.SetActive(active);
            }
            if (!active && _navigation != null && _navigation.NavigationEnabled && _scaling != null)
                _scaling.InteractionsEnabled = true;
        }

        private void Update()
        {
            if (IsActive && _scaling != null)
                _scaling.InteractionsEnabled = false;
        }

        private void LateUpdate()
        {
            if (!IsActive || _vehicleRoot == null)
                return;
            var forward = _navigation.HorizontalTravelDirection;
            if (forward.sqrMagnitude < 0.0001f)
                return;
            _vehicleRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void AnchorVehicleAroundViewer()
        {
            var headLocal = _rig.TrackingOrigin.InverseTransformPoint(_rig.Camera.transform.position);
            _vehicleRoot.transform.localPosition = new Vector3(
                headLocal.x,
                Mathf.Max(0f, headLocal.y - 0.92f),
                headLocal.z - 0.08f);
            var forward = _navigation.HorizontalTravelDirection;
            _vehicleRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void CreateVehicleVisual()
        {
            _vehicleRoot = new GameObject("EarthVR Car Cockpit");
            _vehicleRoot.transform.SetParent(_rig.TrackingOrigin, false);

            var body = CreateMaterial("Car body", new Color(0.055f, 0.14f, 0.20f, 1f), 0.75f);
            var trim = CreateMaterial("Car trim", new Color(0.012f, 0.018f, 0.022f, 1f), 0.18f);
            var accent = CreateMaterial("Car accent", new Color(0.18f, 0.78f, 0.92f, 1f), 0.85f);

            CreatePart("Hood", PrimitiveType.Cube, new Vector3(0f, 0.24f, 1.18f),
                new Vector3(1.62f, 0.26f, 1.92f), Quaternion.identity, body);
            CreatePart("Dashboard", PrimitiveType.Cube, new Vector3(0f, 0.53f, 0.30f),
                new Vector3(1.52f, 0.22f, 0.26f), Quaternion.identity, trim);
            CreatePart("Left door rail", PrimitiveType.Cube, new Vector3(-0.79f, 0.48f, 0.02f),
                new Vector3(0.10f, 0.22f, 1.65f), Quaternion.identity, body);
            CreatePart("Right door rail", PrimitiveType.Cube, new Vector3(0.79f, 0.48f, 0.02f),
                new Vector3(0.10f, 0.22f, 1.65f), Quaternion.identity, body);
            CreatePart("Left windshield pillar", PrimitiveType.Cube, new Vector3(-0.70f, 0.92f, 0.48f),
                new Vector3(0.055f, 0.82f, 0.06f), Quaternion.Euler(0f, 0f, -17f), trim);
            CreatePart("Right windshield pillar", PrimitiveType.Cube, new Vector3(0.70f, 0.92f, 0.48f),
                new Vector3(0.055f, 0.82f, 0.06f), Quaternion.Euler(0f, 0f, 17f), trim);
            CreatePart("Steering wheel", PrimitiveType.Cylinder, new Vector3(-0.34f, 0.68f, 0.36f),
                new Vector3(0.31f, 0.035f, 0.31f), Quaternion.Euler(90f, 0f, 0f), trim);
            CreatePart("Instrument cluster", PrimitiveType.Cube, new Vector3(-0.34f, 0.65f, 0.435f),
                new Vector3(0.38f, 0.16f, 0.025f), Quaternion.Euler(-8f, 0f, 0f), accent);

            _vehicleRoot.SetActive(false);
        }

        private GameObject CreatePart(
            string name,
            PrimitiveType primitive,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(_vehicleRoot.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            Destroy(part.GetComponent<Collider>());
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return part;
        }

        private static Material CreateMaterial(string name, Color color, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Standard") ??
                         Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader)
            {
                name = $"EarthVR {name}",
                color = color,
                hideFlags = HideFlags.DontSave
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private void OnDestroy()
        {
            if (_navigation != null)
                _navigation.State.ModeChanged -= OnModeChanged;
        }
    }

    public static class CarDrivingMath
    {
        public static float MoveSpeedTowards(
            float currentSpeed,
            float targetSpeed,
            float acceleration,
            float braking,
            float deltaTime)
        {
            var changingDirection = currentSpeed * targetSpeed < 0f;
            var slowingDown = Mathf.Abs(targetSpeed) < Mathf.Abs(currentSpeed);
            var rate = changingDirection || slowingDown ? braking : acceleration;
            return Mathf.MoveTowards(currentSpeed, targetSpeed, Mathf.Max(0f, rate) * Mathf.Max(0f, deltaTime));
        }

        public static float CalculateSteeringDelta(
            float steeringInput,
            float speed,
            float maximumForwardSpeed,
            float steeringDegreesPerSecond,
            float deltaTime)
        {
            var speed01 = Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(0.1f, maximumForwardSpeed));
            var response = Mathf.Lerp(0.18f, 1f, Mathf.Sqrt(speed01));
            var reverseDirection = speed < -0.05f ? -1f : 1f;
            return Mathf.Clamp(steeringInput, -1f, 1f) *
                   Mathf.Max(0f, steeringDegreesPerSecond) * response * reverseDirection *
                   Mathf.Max(0f, deltaTime);
        }
    }
}
