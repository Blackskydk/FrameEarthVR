using EarthVR.Configuration;
using EarthVR.Core;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.OpenXR;

namespace EarthVR.Editor
{
    public static class EarthVRSetupWizard
    {
        private const string SettingsDirectory = "Assets/EarthVR/Configuration/Resources";
        private const string SettingsPath = SettingsDirectory + "/EarthVRSettings.asset";
        private const string PipelinePath = "Assets/EarthVR/Configuration/EarthVR-PC-URP.asset";
        private const string RendererPath = "Assets/EarthVR/Configuration/EarthVR-PC-Renderer.asset";
        private const string ScenePath = "Assets/EarthVR/Scenes/EarthVR.unity";
        internal const string AndroidApplicationIdentifier = "com.frameearthvr.app";

        [MenuItem("EarthVR/Setup Project and Main Scene", priority = 1)]
        public static void SetupProjectAndScene()
        {
            EnsureFolder(SettingsDirectory);
            var settings = AssetDatabase.LoadAssetAtPath<EarthVRSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<EarthVRSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            ConfigureUrp();
            ConfigurePlayer();
            ConfigureOpenXR();
            CreateMainScene();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("EarthVR setup complete. Add the local Cesium ion token file, make SteamVR the OpenXR runtime, then enter Play mode.");
        }

        private static void ConfigureUrp()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                var renderer = pipeline.LoadBuiltinRendererData(RendererType.UniversalRenderer);
                if (renderer != null && !AssetDatabase.Contains(renderer))
                    AssetDatabase.CreateAsset(renderer, RendererPath);
                pipeline.renderScale = 1f;
                pipeline.msaaSampleCount = 2;
                pipeline.supportsHDR = false;
                pipeline.supportsCameraDepthTexture = false;
                pipeline.supportsCameraOpaqueTexture = false;
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorUtility.SetDirty(pipeline);
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            ConfigureAndroidPlayer();
            SetActiveInputHandling();
        }

        internal static void ConfigureAndroidPlayer()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidApplicationIdentifier);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.graphicsJobs = false;
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        }

        private static void SetActiveInputHandling()
        {
            var objects = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (objects == null || objects.Length == 0)
                return;
            var serialized = new SerializedObject(objects[0]);
            var property = serialized.FindProperty("activeInputHandler");
            if (property == null)
                return;
            property.intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureOpenXR()
        {
            ConfigureOpenXRForBuildTarget(BuildTargetGroup.Standalone);
            ConfigureOpenXRForBuildTarget(BuildTargetGroup.Android);
        }

        internal static bool ConfigureOpenXRForBuildTarget(BuildTargetGroup buildTargetGroup)
        {
            var settingsStoreType = typeof(UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget);
            var getOrCreateMethod = settingsStoreType.GetMethod(
                "GetOrCreate",
                BindingFlags.Static | BindingFlags.NonPublic);
            var settingsStore = getOrCreateMethod?.Invoke(null, null)
                as UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget;

            if (settingsStore != null &&
                !settingsStore.HasManagerSettingsForBuildTarget(buildTargetGroup))
            {
                settingsStore.CreateDefaultManagerSettingsForBuildTarget(buildTargetGroup);
            }

            var generalSettings = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget
                .XRGeneralSettingsForBuildTarget(buildTargetGroup);

            if (generalSettings == null)
            {
                Debug.LogWarning($"Could not create XR Management settings for {buildTargetGroup}. Open Project Settings > XR Plug-in Management once, then rerun EarthVR setup.");
                return false;
            }

            generalSettings.InitManagerOnStart = true;
            generalSettings.Manager.automaticLoading = true;
            generalSettings.Manager.automaticRunning = true;
            EditorUtility.SetDirty(generalSettings);
            EditorUtility.SetDirty(generalSettings.Manager);

            var assigned = XRPackageMetadataStore.AssignLoader(
                generalSettings.Manager,
                "UnityEngine.XR.OpenXR.OpenXRLoader",
                buildTargetGroup);
            if (!assigned)
                Debug.LogWarning($"OpenXR loader assignment was not accepted for {buildTargetGroup}. Enable OpenXR in Project Settings > XR Plug-in Management.");

            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(buildTargetGroup);
            if (openXr == null)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:OpenXRSettings"))
                {
                    var candidate = AssetDatabase.LoadAssetAtPath<OpenXRSettings>(AssetDatabase.GUIDToAssetPath(guid));
                    if (candidate != null && candidate.name == buildTargetGroup.ToString())
                    {
                        openXr = candidate;
                        break;
                    }
                }
            }
            if (openXr == null)
            {
                Debug.LogWarning($"OpenXR settings for {buildTargetGroup} were created but are not ready yet. Let Unity finish importing, then rerun EarthVR setup.");
                return false;
            }
            openXr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            openXr.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeRendering;
            openXr.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
#if UNITY_6000_2_OR_NEWER
            openXr.useOpenXRPredictedTime = true;
#endif
            // Builds re-apply these every time. With the build menu's
            // "Force Performance Features" unchecked, whatever is set in Project
            // Settings is kept so each can be switched off to isolate artifacts.
            var forceAndroidPerformance = buildTargetGroup != BuildTargetGroup.Android ||
                                          SteamFrameBuild.ForcePerformanceFeatures;
            if (buildTargetGroup == BuildTargetGroup.Android && !forceAndroidPerformance)
            {
                Debug.Log(
                    "EarthVR: keeping the Android OpenXR performance features (foveation, render regions, " +
                    "symmetric projection, buffer discards) exactly as set in Project Settings.");
            }
            if (buildTargetGroup == BuildTargetGroup.Android && forceAndroidPerformance)
            {
                openXr.symmetricProjection = true;
#if UNITY_6000_1_OR_NEWER
                openXr.multiviewRenderRegionsOptimizationMode =
                    OpenXRSettings.MultiviewRenderRegionsOptimizationMode.AllPasses;
#endif
#if UNITY_2023_2_OR_NEWER
                openXr.foveatedRenderingApi = OpenXRSettings.BackendFovationApi.SRPFoveation;
#endif
                openXr.optimizeBufferDiscards = true;
            }
            EditorUtility.SetDirty(openXr);
            foreach (var feature in openXr.GetFeatures())
            {
                var typeName = feature.GetType().Name;
                var isCommonController =
                    typeName == "OculusTouchControllerProfile" ||
                    typeName == "KhronosSimpleControllerProfile" ||
                    typeName == "SteamFrameControllerProfile";
                var isDesktopController = buildTargetGroup == BuildTargetGroup.Standalone &&
                    (typeName == "ValveIndexControllerProfile" || typeName == "HTCViveControllerProfile");
                var isAndroidPerformanceFeature = buildTargetGroup == BuildTargetGroup.Android &&
                    forceAndroidPerformance &&
                    (typeName == "FoveatedRenderingFeature" ||
                     typeName == "ValveOpenXRFoveatedRenderingFeature" ||
                     typeName == "ValveOpenXRRenderRegionsFeature" ||
                     typeName == "ValveOpenXRSupportFeature" ||
                     typeName == "ValveOpenXRLeptonValidationFeature");
                var isMetaRuntimeFeature = typeName == "MetaQuestFeature" || typeName == "OculusQuestFeature";

                if (isMetaRuntimeFeature && buildTargetGroup == BuildTargetGroup.Android)
                {
                    feature.enabled = false;
                    EditorUtility.SetDirty(feature);
                }
                else if (isCommonController || isDesktopController || isAndroidPerformanceFeature)
                {
                    feature.enabled = true;
                    if (buildTargetGroup == BuildTargetGroup.Android)
                        ConfigureValveFeature(feature, typeName);
                    EditorUtility.SetDirty(feature);
                }
            }
            return assigned;
        }

        private static void ConfigureValveFeature(Object feature, string typeName)
        {
            var serialized = new SerializedObject(feature);
            if (typeName == "ValveOpenXRFoveatedRenderingFeature")
            {
                SetBool(serialized, "applySettingsOnStartup", true);
                SetFloat(serialized, "initialFoveationLevel", 0.5f);
                SetBool(serialized, "initialUseEyeTracking", false);
            }
            else if (typeName == "ValveOpenXRRenderRegionsFeature")
            {
                SetBool(serialized, "symmetricProjection", true);
                SetEnum(serialized, "multiviewRenderRegionsOptimizationMode", 2);
            }
            else if (typeName == "ValveOpenXRSupportFeature")
            {
                SetBool(serialized, "optimizeBufferDiscards", true);
                SetBool(serialized, "lateLatchingMode", true);
                SetBool(serialized, "lateLatchingDebug", false);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(SerializedObject serialized, string propertyName, bool value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
                property.boolValue = value;
        }

        private static void SetFloat(SerializedObject serialized, string propertyName, float value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
                property.floatValue = value;
        }

        private static void SetEnum(SerializedObject serialized, string propertyName, int value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property != null)
                property.enumValueIndex = value;
        }

        private static void CreateMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("EarthVR Runtime Bootstrap").AddComponent<EarthVRBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
