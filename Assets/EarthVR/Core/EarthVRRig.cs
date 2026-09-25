using EarthVR.Configuration;
using EarthVR.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace EarthVR.Core
{
    public sealed class EarthVRRig
    {
        public Transform NavigationSpace { get; private set; }
        public Transform TrackingOrigin { get; private set; }
        public Camera Camera { get; private set; }
        public Transform LeftController { get; private set; }
        public Transform RightController { get; private set; }

        public static EarthVRRig Create(OpenXRInputReader input, EarthVRSettings settings)
        {
            var rig = new EarthVRRig();
            rig.NavigationSpace = new GameObject("Navigation Space").transform;
            rig.TrackingOrigin = new GameObject("XR Origin (Floor)").transform;
            rig.TrackingOrigin.SetParent(rig.NavigationSpace, false);
            rig.TrackingOrigin.localPosition = new Vector3(0f, settings.startHeightMeters, 0f);

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(rig.TrackingOrigin, false);
            rig.Camera = cameraObject.GetComponent<Camera>();
            rig.Camera.nearClipPlane = settings.humanNearClipMeters;
            rig.Camera.farClipPlane = settings.geographicFarClipMeters;
            rig.Camera.clearFlags = CameraClearFlags.SolidColor;
            rig.Camera.backgroundColor = new Color(0.005f, 0.008f, 0.02f, 1f);
            rig.Camera.allowHDR = false;
            rig.Camera.allowMSAA = true;
            rig.Camera.useOcclusionCulling = false;
            cameraObject.AddComponent<TrackedActionPose>().Configure(input.HeadPositionAction, input.HeadRotationAction);

            rig.LeftController = CreateController(
                "Left Controller", rig.TrackingOrigin, input.LeftPositionAction, input.LeftRotationAction,
                new Color(0.1f, 0.55f, 1f), settings);
            rig.RightController = CreateController(
                "Right Controller (Dominant)", rig.TrackingOrigin, input.RightPositionAction, input.RightRotationAction,
                new Color(1f, 0.35f, 0.08f), settings);

            return rig;
        }

        private static Transform CreateController(
            string name,
            Transform parent,
            UnityEngine.InputSystem.InputAction position,
            UnityEngine.InputSystem.InputAction rotation,
            Color color,
            EarthVRSettings settings)
        {
            var controller = new GameObject(name).transform;
            controller.SetParent(parent, false);
            controller.gameObject.AddComponent<TrackedActionPose>().Configure(position, rotation);

            var ray = new GameObject("Direction Indicator", typeof(LineRenderer));
            ray.transform.SetParent(controller, false);
            var line = ray.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.forward * settings.pointerDefaultLengthMeters);
            line.startWidth = 0.012f;
            line.endWidth = 0.05f;
            line.numCapVertices = 6;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                line.sharedMaterial = new Material(shader) { color = Color.white };
            }

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0.26f, 0f),
                    new GradientAlphaKey(0.18f, 0.3f),
                    new GradientAlphaKey(0.07f, 0.78f),
                    new GradientAlphaKey(0.025f, 1f)
                });
            line.colorGradient = gradient;

            var hitMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hitMarker.name = "Pointer Surface Hit";
            Object.Destroy(hitMarker.GetComponent<Collider>());
            hitMarker.transform.SetParent(controller, false);
            var markerRenderer = hitMarker.GetComponent<MeshRenderer>();
            markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;
            if (shader != null)
                markerRenderer.sharedMaterial = new Material(shader)
                {
                    color = new Color(1f, 1f, 1f, 0.48f)
                };
            hitMarker.SetActive(false);

            controller.gameObject.AddComponent<ControllerPointerBeam>().Initialize(
                line,
                hitMarker.transform,
                settings.pointerDefaultLengthMeters,
                settings.pointerMaximumLengthMeters);
            return controller;
        }

    }

    internal sealed class ControllerPointerBeam : MonoBehaviour
    {
        private LineRenderer _beam;
        private Transform _hitMarker;
        private float _defaultLength;
        private float _maximumLength;

        public void Initialize(LineRenderer beam, Transform hitMarker, float defaultLength, float maximumLength)
        {
            _beam = beam;
            _hitMarker = hitMarker;
            _defaultLength = Mathf.Max(1f, defaultLength);
            _maximumLength = Mathf.Max(_defaultLength, maximumLength);
        }

        private void LateUpdate()
        {
            if (_beam == null)
                return;

            var length = _defaultLength;
            var hasHit = Physics.Raycast(
                    transform.position,
                    transform.forward,
                    out var hit,
                    _maximumLength,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);
            if (hasHit)
                length = Mathf.Max(0.05f, hit.distance);

            _beam.SetPosition(1, Vector3.forward * length);
            // Preserve a visible angular width for long celestial/terrain rays.
            _beam.endWidth = Mathf.Clamp(length * 0.002f, 0.05f, 1f);
            if (_hitMarker == null)
                return;

            _hitMarker.gameObject.SetActive(hasHit);
            if (!hasHit)
                return;

            _hitMarker.localPosition = Vector3.forward * length;
            var markerSize = Mathf.Clamp(length * 0.003f, 0.035f, 1.25f);
            _hitMarker.localScale = Vector3.one * markerSize;
        }
    }
}
