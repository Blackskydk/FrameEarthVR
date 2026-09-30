using System.Reflection;
using CesiumForUnity;
using EarthVR.Configuration;
using EarthVR.Core;
using EarthVR.Input;
using EarthVR.Navigation;
using EarthVR.Scaling;
using EarthVR.UI;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EarthVR.Tests
{
    public sealed class GroundedInteractionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Field(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        private static void Call(object target, string name, params object[] arguments) => target.GetType().GetMethod(name, Private).Invoke(target, arguments);
        private static void Property(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);

        private static EarthVRRig Rig(Transform root)
        {
            var rig = new EarthVRRig();
            var tracking = new GameObject("Test tracking").transform;
            tracking.SetParent(root, false);
            tracking.localPosition = new Vector3(0f, 50f, 0f);
            var camera = new GameObject("Test eyes").AddComponent<Camera>();
            camera.transform.SetParent(tracking, false);
            camera.transform.localPosition = new Vector3(0.1f, 1.7f, 0.2f);
            var hand = new GameObject("Test hand").transform;
            hand.SetParent(tracking, false);
            Property(rig, "NavigationSpace", root); Property(rig, "TrackingOrigin", tracking);
            Property(rig, "Camera", camera); Property(rig, "LeftController", hand); Property(rig, "RightController", hand);
            return rig;
        }

        [Test]
        public void GroundedResizeRetainsOneGeographicFootAnchorAcrossBothDirections()
        {
            var host = new GameObject("Grounded resize test");
            var earth = new GameObject("Test georeference");
            var settings = ScriptableObject.CreateInstance<EarthVRSettings>();
            try
            {
                var rig = Rig(host.transform);
                host.transform.position = new Vector3(10f, -40f, 20f);
                host.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
                var geo = earth.AddComponent<CesiumGeoreference>();
                geo.Initialize();
                var input = new IdleInput();
                var navigation = host.AddComponent<NavigationController>();
                navigation.Initialize(input, settings, rig, geo);
                navigation.State.SetMode(MovementMode.Grounded);
                var scaling = host.AddComponent<WorldManipulationController>();
                scaling.Initialize(input, settings, rig, geo, navigation, null);
                var originalFoot = geo.TransformUnityPositionToEarthCenteredEarthFixed((double3)(float3)
                    (rig.TrackingOrigin.position - Vector3.up * Mathf.Max(0.015f, settings.groundClearanceMeters / scaling.UserScale)));
                var originalEyeOffset = rig.Camera.transform.position - rig.TrackingOrigin.position;
                foreach (var scale in new[] { 0.05f, 2f, 1000f, 3f, 1f })
                {
                    // A competing correction must not become a new pivot.
                    if (scaling.IsScalingGrounded) host.transform.position += new Vector3(2f, -3f, 1f);
                    Call(scaling, "ApplyGroundedScale", scale);
                    var foot = geo.TransformUnityPositionToEarthCenteredEarthFixed((double3)(float3)
                        (rig.TrackingOrigin.position - Vector3.up * Mathf.Max(0.015f, settings.groundClearanceMeters / scaling.UserScale)));
                    Assert.That(math.distance(originalFoot, foot), Is.LessThan(0.02d), "Foot support drift at scale " + scale);
                    Assert.That(Vector3.Distance(originalEyeOffset, rig.Camera.transform.position - rig.TrackingOrigin.position), Is.LessThan(0.001f));
                }
                Call(scaling, "UpdateGroundedScale"); // A released stick ends the gesture.
                Assert.That(scaling.IsScalingGrounded, Is.False);
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(earth); Object.DestroyImmediate(settings); }
        }

        [Test]
        public void CarryPoseDoesNotInheritHandRollOrHeadRoll()
        {
            var host = new GameObject("Globe pose test");
            var settings = ScriptableObject.CreateInstance<EarthVRSettings>();
            try
            {
                var rig = Rig(host.transform);
                rig.Camera.transform.rotation = Quaternion.Euler(15f, 25f, 0f);
                var globe = host.AddComponent<MiniatureGlobePicker>();
                var root = new GameObject("Test globe"); root.transform.SetParent(rig.LeftController, false);
                var anchor = new GameObject("Test interface").transform; anchor.SetParent(rig.LeftController, false);
                Field(globe, "_rig", rig); Field(globe, "_settings", settings); Field(globe, "_root", root);
                Property(globe, "InterfaceAnchor", anchor);
                Call(globe, "StabilizeGlobePose");
                var position = root.transform.position; var orientation = anchor.rotation;
                rig.LeftController.rotation = Quaternion.Euler(40f, 55f, 75f);
                rig.Camera.transform.rotation *= Quaternion.AngleAxis(45f, Vector3.forward);
                Call(globe, "StabilizeGlobePose");
                Assert.That(Vector3.Distance(position, root.transform.position), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(orientation, anchor.rotation), Is.LessThan(0.01f));
            }
            finally { Object.DestroyImmediate(host); Object.DestroyImmediate(settings); }
        }

        private sealed class IdleInput : IEarthVRInput
        {
            public Vector2 Fly => Vector2.zero;
            public bool SelectPressed => false;
            public bool LeftTriggerHeld => false;
            public bool RightTriggerHeld => false;
            public bool LeftGripHeld => false;
            public bool RightGripHeld => false;
            public bool BoostHeld => false;
            public bool ToggleGlobeOverviewPressed => false;
            public bool OpenMenuPressed => false;
            public bool ResetViewPressed => false;
            public bool ToggleMovementModePressed => false;
            public InputAction HeadPositionAction => null;
            public InputAction HeadRotationAction => null;
            public InputAction LeftPositionAction => null;
            public InputAction LeftRotationAction => null;
            public InputAction RightPositionAction => null;
            public InputAction RightRotationAction => null;
        }
    }
}
