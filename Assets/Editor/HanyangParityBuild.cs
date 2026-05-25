using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Editor;

namespace Hanyang.QuestBouncer.Editor
{
    public static class HanyangParityBuild
    {
        private const string ScenePath = "Assets/App/Scenes/Main.unity";
        private const string DefaultVisionOSBuildPath = ".omx/evidence/hanyang-parity/gate-10/visionos-xcode-project";
        private const string DefaultVisionOSSimulatorBuildPath = ".omx/evidence/hanyang-parity/gate-10/visionos-simulator-xcode-project";
        private const string DefaultHoloLensBuildPath = ".omx/evidence/hanyang-parity/gate-10/hololens-uwp-project";
        private const string DefaultBundleIdentifier = "com.hanyang.questbouncer.parity";
        private const string XRGeneralSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        private const string VisionOSLoaderPath = "Assets/XR/Loaders/VisionOSLoader.asset";
        private const string OpenXRLoaderPath = "Assets/XR/Loaders/OpenXRLoader.asset";
        private const string MRTKSettingsPath = "Assets/MRTK.Generated/MRTKSettings.asset";
        private const string MRTKProfilePath = "Assets/App/Settings/AppMRTKProfile.asset";
        private const string UniversalRenderPipelineGlobalSettingsPath = "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset";
        private const string MRTK3XRRigGuid = "acbf65a81ce2cf94f82a0809298acf70";
        private const string MRTK3InputSimulatorGuid = "ad5b753b73e311143a85055b15cea562";
        private const string MRTK3XRRigName = "MRTK XR Rig";
        private const string MRTK3InputSimulatorName = "MRTKInputSimulator";
        private const string VisionOSVolumeCameraName = "PolySpatial Hanyang Volume Camera";
        private static readonly Vector3 VisionOSVolumeCameraDimensions = new Vector3(4f, 4f, 4f);
        private const string HanyangBoundedVolumeCameraConfigGuid = "5e3111cbbe284945804909f2cd85d91a";
        private const string GraphicsToolsStandardShaderGuid = "c331f6c43a2ef0945864cb668f2653c9";
        private const string GraphicsToolsBackplateShaderGuid = "bf1548464ae044849a0ce555785ea4a2";
        private const string GraphicsToolsFrontplateShaderGuid = "3dee60a1b8e777e4f8b15a53c35077c0";
        private const string GraphicsToolsIridescentMapGuid = "a47616c60a914d2478946d4a5e0055ad";
        private const string GraphicsToolsBlobTextureGuid = "0500244013d182d43a4337685f8c618e";
        private const string MRTK3TraditionalBoundsVisualsPath = "Packages/org.mixedrealitytoolkit.spatialmanipulation/BoundsControl/Prefabs/BoundingBoxWithTraditionalHandles.prefab";
        private const int PlayModeCameraCaptureWidth = 1024;
        private const int PlayModeCameraCaptureHeight = 768;
        private const string PlayModeCapturePendingKey = "HanyangParity.PlayModeCapture.Pending";
        private const string PlayModeEnterPendingKey = "HanyangParity.PlayMode.EnterPending";
        private const string PlayModeCapturePathKey = "HanyangParity.PlayModeCapture.Path";
        private const string PlayModeCaptureStartTimeKey = "HanyangParity.PlayModeCapture.StartTime";
        private const float PlayModeCaptureDelaySeconds = 45f;
        private static readonly Vector3 LegacyObjectParentColliderCenter = Vector3.zero;
        private static readonly Vector3 LegacyObjectParentColliderSize = new(1.5593109f, 2.757052f, 1.1214576f);
        private static readonly string[] HolographicBackplateMaterialPaths =
        {
            "Assets/Materials/HolographicBackPlate.mat",
            "Assets/Materials/HolographicBackPlateToggleState.mat",
            "Assets/Materials/HolographicBackPlateBorderOnly.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/HolographicBackPlate.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/HolographicBackPlateToggleState.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/HolographicBackPlateBorderOnly.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/HolographicBackPlateGrabbable.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/HolographicBackPlateGrabbableProximity.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/HolographicBackPlateGrabbed.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/HolographicBackPlateHover.mat"
        };
        private static readonly string[] HolographicFrontplateMaterialPaths =
        {
            "Assets/Materials/HolographicButtonContentCageProximity.mat",
            "Assets/Materials/MRTK_PressableInteractablesButtonBox.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/HolographicButtonContent.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/HolographicButtonContentCage.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/HolographicButtonContentCageProximity.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/StandardAssets/Materials/MRTK_PressableInteractablesButtonBox.mat"
        };
        private static readonly string[] HolographicStandardMaterialPaths =
        {
            "Assets/Materials/MRTK_GrabbableDots2RowsV1.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/MRTK_GrabbableDots.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/MRTK_GrabbableDots2RowsH.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/MRTK_GrabbableDots2RowsV1.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Interactable/Materials/MRTK_GrabbableDots2RowsV2.mat"
        };
        private static readonly string[] HolographicButtonIconMaterialSearchFolders =
        {
            "Assets",
            "Packages"
        };
        private static readonly string[] LegacyBoundingBoxShellMaterialPaths =
        {
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Materials/BoundingBox.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Materials/BoundingBoxGrabbed.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Materials/BoundingBoxSlate.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Materials/BoundingBoxSlateGrabbed.mat"
        };
        private static readonly string[] LegacyBoundingBoxHandleMaterialPaths =
        {
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Materials/BoundingBoxHandleBlue.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Materials/BoundingBoxHandleBlueGrabbed.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Materials/BoundingBoxHandleWhite.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Materials/BoundingBoxLines.mat",
            "Packages/com.microsoft.mixedreality.toolkit.foundation/SDK/Features/UX/Materials/BoundsControlHandleDefault.mat"
        };
        private static readonly string[] MRTK3BoundsHandleMaterialPaths =
        {
            "Packages/org.mixedrealitytoolkit.standardassets/Materials/BoundsControl/BoundsHandle.mat"
        };
        private static readonly string[] VesselMaterialPaths =
        {
            "Assets/Prefabs/New Material.mat",
            "Assets/Resources/New Material.mat",
            "Assets/Resources/New Material 1.mat",
            "Assets/obj/New Material.mat"
        };
        private static readonly string[] LegacyMRTK2CameraInputRoots =
        {
            "MixedRealityToolkit",
            "MixedRealityPlayspace"
        };

        [InitializeOnLoadMethod]
        private static void InstallPlayModeAutomationHooks()
        {
            EditorApplication.update -= HandlePlayModeCaptureAutomation;
            EditorApplication.update += HandlePlayModeCaptureAutomation;
        }

        [MenuItem("Hanyang Parity/Report Prerequisites")]
        public static void ReportPrerequisites()
        {
            Debug.Log($"Hanyang parity prerequisite report: Unity {Application.unityVersion}");
            Debug.Log($"Project path: {Path.GetFullPath(Path.Combine(Application.dataPath, ".."))}");
            Debug.Log($"Parity scene exists: {File.Exists(ScenePath)} ({ScenePath})");
            Debug.Log($"Manifest has com.meta.xr.sdk.core 71.0.0: {ManifestContains("\"com.meta.xr.sdk.core\": \"71.0.0\"")}");
            Debug.Log($"Manifest has com.unity.xr.visionos: {ManifestContains("\"com.unity.xr.visionos\"")}");
            Debug.Log($"Manifest has com.unity.polyspatial.visionos: {ManifestContains("\"com.unity.polyspatial.visionos\"")}");
            ReportBuildTarget("VisionOS", "VisionOS");
            ReportBuildTarget("WSA", "WSAPlayer");
            ReportXRLoaders("VisionOS");
            ReportXRLoaders("WSA");
        }

        [MenuItem("Hanyang Parity/Report Scene Runtime Surface")]
        public static void ReportSceneRuntimeSurface()
        {
            EnsureSceneExists();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var report = new StringBuilder();
            report.AppendLine("Hanyang parity scene runtime surface report");
            report.AppendLine($"Scene: {scene.path}");
            report.AppendLine($"Root count: {scene.rootCount}");
            report.AppendLine($"Default PolySpatial window config: {DescribeAssetByGuid("780e9fdf3d43042578153145466820cf")}");
            report.AppendLine();

            report.AppendLine("Roots:");
            foreach (var root in scene.GetRootGameObjects().OrderBy(root => root.name))
            {
                report.AppendLine($"- {root.name} active={root.activeInHierarchy} children={root.transform.childCount} pos={FormatVector(root.transform.position)} scale={FormatVector(root.transform.lossyScale)}");
            }

            report.AppendLine();
            report.AppendLine("Cameras:");
            foreach (var camera in Resources.FindObjectsOfTypeAll<Camera>().Where(IsSceneObject).OrderBy(camera => camera.name))
            {
                report.AppendLine($"- {GetPath(camera.transform)} tag={camera.tag} enabled={camera.enabled} active={camera.gameObject.activeInHierarchy} pos={FormatVector(camera.transform.position)} rot={FormatVector(camera.transform.eulerAngles)} clear={camera.clearFlags} bg={FormatColor(camera.backgroundColor)} near={camera.nearClipPlane} far={camera.farClipPlane} fov={camera.fieldOfView} cullingMask=0x{camera.cullingMask:X8}");
            }

            var volumeCameras = Resources.FindObjectsOfTypeAll<Component>()
                .Where(IsSceneObject)
                .Where(component => component != null && component.GetType().FullName == "Unity.PolySpatial.VolumeCamera")
                .OrderBy(component => component.name)
                .ToArray();

            report.AppendLine();
            report.AppendLine($"VolumeCameras: {volumeCameras.Length}");
            foreach (var volumeCamera in volumeCameras)
            {
                report.AppendLine($"- {GetPath(volumeCamera.transform)} active={volumeCamera.gameObject.activeInHierarchy} {DescribeSerializedFields(volumeCamera, "m_Dimensions", "m_OutputConfiguration", "m_CullingMask", "OpenWindowOnLoad")}");
            }

            var renderers = Resources.FindObjectsOfTypeAll<Renderer>()
                .Where(IsSceneObject)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
                .ToArray();
            report.AppendLine();
            report.AppendLine($"Enabled active renderers: {renderers.Length}");
            report.AppendLine($"Aggregate renderer bounds: {FormatBounds(CalculateBounds(renderers))}");

            report.AppendLine();
            report.AppendLine("Largest renderer bounds:");
            foreach (var renderer in renderers.OrderByDescending(renderer => renderer.bounds.size.sqrMagnitude).Take(20))
            {
                report.AppendLine($"- {GetPath(renderer.transform)} type={renderer.GetType().Name} bounds={FormatBounds(renderer.bounds)} material={GetMaterialName(renderer)}");
            }

            report.AppendLine();
            report.AppendLine("Root renderer bounds:");
            foreach (var group in renderers.GroupBy(renderer => renderer.transform.root.name).OrderBy(group => group.Key))
            {
                report.AppendLine($"- {group.Key}: count={group.Count()} bounds={FormatBounds(CalculateBounds(group))}");
            }

            Debug.Log(report.ToString());
        }

        [MenuItem("Hanyang Parity/visionOS/Configure Build Settings")]
        public static void ConfigureVisionOS()
        {
            ConfigureVisionOS(useSimulatorSdk: false);
        }

        private static void ConfigureVisionOS(bool useSimulatorSdk)
        {
            var group = ParseBuildTargetGroup("VisionOS");
            var target = ParseBuildTarget("VisionOS");
            ConfigureBuildScenes(ensureVisionOSVolumeCamera: true);
            ConfigurePlayerSettings(group, DefaultBundleIdentifier);
            ConfigureVisionOSMixedRealitySettings();
            ConfigureVisionOSShaderSettings();
            ConfigureVisionOSSdk(useSimulatorSdk);
            ConfigureXRLoader(group, "UnityEngine.XR.VisionOS.VisionOSLoader", VisionOSLoaderPath);
            ConfigureMRTKProfile(group);
            AssetDatabase.SaveAssets();
            Debug.Log("Hanyang parity visionOS build settings configured.");
            ReportBuildTarget("VisionOS", "VisionOS");
            ReportXRLoaders("VisionOS");
            ReportMRTKProfile("VisionOS");
        }

        [MenuItem("Hanyang Parity/Report Visual Shader Support")]
        public static void ReportVisualShaderSupport()
        {
            ReportShaderSupport("GraphicsToolsStandard", GraphicsToolsStandardShaderGuid);
            ReportShaderSupport("GraphicsToolsBackplate", GraphicsToolsBackplateShaderGuid);
            ReportShaderSupport("GraphicsToolsFrontplate", GraphicsToolsFrontplateShaderGuid);
            foreach (var materialPath in HolographicBackplateMaterialPaths
                         .Concat(HolographicFrontplateMaterialPaths)
                         .Concat(HolographicStandardMaterialPaths))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    Debug.LogWarning($"Visual material missing: {materialPath}");
                    continue;
                }

                Debug.Log($"Visual material support: {materialPath} shader={material.shader?.name ?? "(none)"} supported={material.shader != null && material.shader.isSupported} renderQueue={material.renderQueue}");
            }
        }

        [MenuItem("Hanyang Parity/visionOS/Build Xcode Project")]
        public static void BuildVisionOS()
        {
            BuildVisionOSInternal(useSimulatorSdk: false, GetArgument("-visionOSBuildPath", DefaultVisionOSBuildPath), "visionOS Xcode project");
        }

        [MenuItem("Hanyang Parity/visionOS/Build Simulator Xcode Project")]
        public static void BuildVisionOSSimulator()
        {
            BuildVisionOSInternal(useSimulatorSdk: true, GetArgument("-visionOSSimulatorBuildPath", DefaultVisionOSSimulatorBuildPath), "visionOS simulator Xcode project");
        }

        private static void BuildVisionOSInternal(bool useSimulatorSdk, string buildPath, string description)
        {
            ConfigureVisionOS(useSimulatorSdk);
            var group = ParseBuildTargetGroup("VisionOS");
            var target = ParseBuildTarget("VisionOS");

            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                throw new InvalidOperationException("visionOS Build Support is not installed for this Unity editor.");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                throw new InvalidOperationException("Failed to switch active build target to visionOS.");

            BuildPlayer(group, target, buildPath, description, useVisionOSSimulatorSdk: useSimulatorSdk);
        }

        [MenuItem("Hanyang Parity/HoloLens/Configure Build Settings")]
        public static void ConfigureHoloLens()
        {
            var group = ParseBuildTargetGroup("WSA");
            ConfigureBuildScenes(ensureVisionOSVolumeCamera: false);
            ConfigurePlayerSettings(group, DefaultBundleIdentifier);
            ConfigureXRLoader(group, "UnityEngine.XR.OpenXR.OpenXRLoader", OpenXRLoaderPath);
            ConfigureMRTKProfile(group);
            AssetDatabase.SaveAssets();
            Debug.Log("Hanyang parity HoloLens build settings configured.");
            ReportBuildTarget("WSA", "WSAPlayer");
            ReportXRLoaders("WSA");
            ReportMRTKProfile("WSA");
        }

        [MenuItem("Hanyang Parity/HoloLens/Build UWP Project")]
        public static void BuildHoloLens()
        {
            ConfigureHoloLens();
            var group = ParseBuildTargetGroup("WSA");
            var target = ParseBuildTarget("WSAPlayer");

            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                throw new InvalidOperationException("UWP Build Support is not installed for this Unity editor.");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                throw new InvalidOperationException("Failed to switch active build target to UWP/HoloLens.");

            BuildPlayer(group, target, GetArgument("-holoLensBuildPath", DefaultHoloLensBuildPath), "HoloLens UWP project");
        }

        private static void ConfigureBuildScenes(bool ensureVisionOSVolumeCamera)
        {
            EnsureSceneExists();
            ApplyMRTK3CameraAndInput(saveScene: !ensureVisionOSVolumeCamera);
            ApplyUnity6VisualParity(saveScene: !ensureVisionOSVolumeCamera);
            if (ensureVisionOSVolumeCamera)
            {
                ApplyVisionOSVolumeCamera(saveScene: true);
            }
            else
            {
                SetVisionOSVolumeCameraActive(SceneManager.GetActiveScene(), false);
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }

        [MenuItem("Hanyang Parity/Scene/Apply MRTK3 Camera And Input")]
        public static void ApplyMRTK3CameraAndInput()
        {
            ApplyMRTK3CameraAndInput(saveScene: true);
        }

        [MenuItem("Hanyang Parity/Scene/Open Scene And Enter Play Mode")]
        public static void OpenSceneAndEnterPlayMode()
        {
            OpenSceneAndEnterPlayMode(captureScreenshot: false);
        }

        [MenuItem("Hanyang Parity/Scene/Open Scene Enter Play And Capture")]
        public static void OpenSceneEnterPlayAndCapture()
        {
            OpenSceneAndEnterPlayMode(captureScreenshot: true);
        }

        private static void OpenSceneAndEnterPlayMode(bool captureScreenshot)
        {
            EnsureSceneExists();
            ConfigureEditorPlayModeBuildTarget();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ApplyMRTK3CameraAndInput(saveScene: true);
            ApplyUnity6VisualParity(saveScene: true);
            SetVisionOSVolumeCameraActive(SceneManager.GetActiveScene(), false);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

            if (captureScreenshot)
            {
                var capturePath = Path.GetFullPath(GetArgument(
                    "-hanyangPlayCapturePath",
                    ".omx/evidence/hanyang-parity/gate-14/current-playmode.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(capturePath) ?? ".");
                SessionState.SetString(PlayModeCapturePathKey, capturePath);
                SessionState.SetFloat(PlayModeCaptureStartTimeKey, 0f);
                SessionState.SetBool(PlayModeCapturePendingKey, true);
                Debug.Log($"Hanyang parity Play Mode capture armed: {capturePath}");
            }

            SessionState.SetBool(PlayModeEnterPendingKey, true);
        }

        private static void ConfigureEditorPlayModeBuildTarget()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneOSX)
                return;

            Debug.Log($"Hanyang parity switching editor Play Mode target from {EditorUserBuildSettings.activeBuildTarget} to StandaloneOSX for source-project comparison.");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
            {
                Debug.LogWarning("Hanyang parity could not switch editor Play Mode target to StandaloneOSX; Play Mode may still include target-specific XR simulation.");
            }
        }

        private static void ApplyMRTK3CameraAndInput(bool saveScene)
        {
            EnsureSceneExists();

            var activeScene = SceneManager.GetActiveScene();
            var scene = activeScene.IsValid() && activeScene.path == ScenePath
                ? activeScene
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var removedRoots = RemoveLegacyMRTK2Roots(scene);
            var rig = EnsurePrefabRoot(scene, MRTK3XRRigGuid, MRTK3XRRigName);
            var simulator = EnsurePrefabRoot(scene, MRTK3InputSimulatorGuid, MRTK3InputSimulatorName);

            ResetRootTransform(rig.transform);
            ResetRootTransform(simulator.transform);
            EnsureRigCameraSetup(rig);
            EnsureMRTK3SpatialManipulation(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene && !EditorSceneManager.SaveScene(scene))
                throw new IOException($"Failed to save MRTK3/XRI camera/input scene changes: {ScenePath}");

            AssetDatabase.SaveAssets();

            var removedSummary = removedRoots.Count == 0 ? "(none)" : string.Join(", ", removedRoots);
            Debug.Log($"Hanyang parity scene camera/input normalized to MRTK3/XRI. Removed legacy MRTK2 roots: {removedSummary}. Roots present: {MRTK3XRRigName}, {MRTK3InputSimulatorName}.");
        }

        [MenuItem("Hanyang Parity/Scene/Apply visionOS Volume Camera")]
        public static void ApplyVisionOSVolumeCamera()
        {
            ApplyVisionOSVolumeCamera(saveScene: true);
        }

        private static void ApplyVisionOSVolumeCamera(bool saveScene)
        {
            EnsureSceneExists();

            var activeScene = SceneManager.GetActiveScene();
            var scene = activeScene.IsValid() && activeScene.path == ScenePath
                ? activeScene
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var volumeCamera = EnsureVisionOSVolumeCamera(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene && !EditorSceneManager.SaveScene(scene))
                throw new IOException($"Failed to save visionOS VolumeCamera scene changes: {ScenePath}");

            AssetDatabase.SaveAssets();
            Debug.Log($"Hanyang parity visionOS VolumeCamera normalized: {GetPath(volumeCamera.transform)} {DescribeSerializedFields(volumeCamera, "m_Dimensions", "m_OutputConfiguration", "m_CullingMask", "OpenWindowOnLoad")}.");
        }

        [MenuItem("Hanyang Parity/Scene/Apply Unity6 Visual Parity")]
        public static void ApplyUnity6VisualParity()
        {
            ApplyUnity6VisualParity(saveScene: true);
        }

        private static void ApplyUnity6VisualParity(bool saveScene)
        {
            EnsureSceneExists();

            var activeScene = SceneManager.GetActiveScene();
            var scene = activeScene.IsValid() && activeScene.path == ScenePath
                ? activeScene
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var cameras = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>(includeInactive: true))
                .ToArray();
            foreach (var camera in cameras)
                EnsureCameraVisualParity(camera);

            var materialCount = EnsureUnity6HolographicMaterials();

            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene && !EditorSceneManager.SaveScene(scene))
                throw new IOException($"Failed to save Unity 6 visual parity scene changes: {ScenePath}");

            AssetDatabase.SaveAssets();
            Debug.Log($"Hanyang parity Unity 6 visual surface normalized. Cameras={cameras.Length}, holographic materials={materialCount}.");
        }

        private static Component EnsureVisionOSVolumeCamera(Scene scene)
        {
            var volumeCameraType = FindType("Unity.PolySpatial.VolumeCamera");
            if (volumeCameraType == null)
                throw new InvalidOperationException("Unity.PolySpatial.VolumeCamera type is not available. Verify com.unity.polyspatial is installed.");

            var volumeCamera = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren(volumeCameraType, includeInactive: true).Cast<Component>())
                .FirstOrDefault(component => component != null);

            if (volumeCamera == null)
            {
                var go = new GameObject(VisionOSVolumeCameraName);
                SceneManager.MoveGameObjectToScene(go, scene);
                volumeCamera = go.AddComponent(volumeCameraType);
            }

            var volumeCameraObject = volumeCamera.gameObject;
            volumeCameraObject.name = VisionOSVolumeCameraName;
            volumeCameraObject.SetActive(true);
            ResetRootTransform(volumeCameraObject.transform);

            var boundedConfigPath = AssetDatabase.GUIDToAssetPath(HanyangBoundedVolumeCameraConfigGuid);
            if (string.IsNullOrWhiteSpace(boundedConfigPath))
                throw new FileNotFoundException($"Could not resolve Hanyang bounded VolumeCamera config GUID {HanyangBoundedVolumeCameraConfigGuid}.");

            var boundedConfig = AssetDatabase.LoadMainAssetAtPath(boundedConfigPath);
            if (boundedConfig == null)
                throw new FileNotFoundException("Could not load Hanyang bounded VolumeCamera config.", boundedConfigPath);

            var serializedObject = new SerializedObject(volumeCamera);
            SetVector3Property(serializedObject, "m_Dimensions", VisionOSVolumeCameraDimensions);
            SetBoolProperty(serializedObject, "m_ScaleWithWindow", true);
            SetBoolProperty(serializedObject, "m_IsUniformScale", false);
            SetObjectReferenceProperty(serializedObject, "m_OutputConfiguration", boundedConfig);
            SetIntProperty(serializedObject, "m_CullingMask", -1);
            SetBoolProperty(serializedObject, "OpenWindowOnLoad", true);
            SetIntProperty(serializedObject, "m_TargetDisplay", 0);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(volumeCameraObject);
            EditorUtility.SetDirty(volumeCamera);
            return volumeCamera;
        }

        private static void SetVisionOSVolumeCameraActive(Scene scene, bool active)
        {
            var volumeCameraType = FindType("Unity.PolySpatial.VolumeCamera");
            if (volumeCameraType == null || !scene.IsValid())
                return;

            var volumeCameras = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren(volumeCameraType, includeInactive: true).Cast<Component>())
                .ToArray();
            foreach (var volumeCamera in volumeCameras)
            {
                if (volumeCamera == null)
                    continue;

                volumeCamera.gameObject.SetActive(active);
                EditorUtility.SetDirty(volumeCamera.gameObject);
                EditorUtility.SetDirty(volumeCamera);
                Debug.Log($"Hanyang parity visionOS VolumeCamera active={active}: {GetPath(volumeCamera.transform)}");
            }
        }

        private static List<string> RemoveLegacyMRTK2Roots(Scene scene)
        {
            var removedRoots = new List<string>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!LegacyMRTK2CameraInputRoots.Contains(root.name))
                    continue;

                removedRoots.Add(root.name);
                UnityEngine.Object.DestroyImmediate(root);
            }

            return removedRoots;
        }

        private static GameObject EnsurePrefabRoot(Scene scene, string prefabGuid, string rootName)
        {
            var existing = scene.GetRootGameObjects().FirstOrDefault(root => root.name == rootName);
            if (existing != null)
                return existing;

            var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
            if (string.IsNullOrWhiteSpace(prefabPath))
                throw new FileNotFoundException($"Could not resolve MRTK3 prefab GUID {prefabGuid} for {rootName}.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new FileNotFoundException($"Could not load MRTK3 prefab for {rootName}.", prefabPath);

            var instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
                throw new InvalidOperationException($"Failed to instantiate MRTK3 prefab {rootName} from {prefabPath}.");

            instance.name = rootName;
            return instance;
        }

        private static void ResetRootTransform(Transform transform)
        {
            transform.SetParent(null, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        private static void EnsureRigCameraSetup(GameObject rig)
        {
            NormalizeRigCameraHeight(rig);

            var cameras = rig.GetComponentsInChildren<Camera>(includeInactive: true);
            if (cameras.Length == 0)
                throw new InvalidOperationException($"{MRTK3XRRigName} does not contain a camera.");

            foreach (var camera in cameras)
            {
                camera.tag = "MainCamera";
                EnsureCameraVisualParity(camera);
                EnsureUniversalAdditionalCameraData(camera);
                EditorUtility.SetDirty(camera);
            }
        }

        private static void EnsureMRTK3SpatialManipulation(Scene scene)
        {
            var objectParent = FindSceneTransform(scene, "Object Parent")?.gameObject;
            if (objectParent == null)
            {
                Debug.LogWarning("Hanyang parity could not find Object Parent for MRTK3 spatial manipulation setup.");
                return;
            }

            var objectManipulator = objectParent.GetComponent<MixedReality.Toolkit.SpatialManipulation.ObjectManipulator>();
            if (objectManipulator == null)
                objectManipulator = objectParent.AddComponent<MixedReality.Toolkit.SpatialManipulation.ObjectManipulator>();

            RemovePersistedMRTK3BoundsVisuals(objectParent.transform);

            objectManipulator.HostTransform = objectParent.transform;
            objectManipulator.AllowedManipulations =
                MixedReality.Toolkit.TransformFlags.Move |
                MixedReality.Toolkit.TransformFlags.Rotate |
                MixedReality.Toolkit.TransformFlags.Scale;
            objectManipulator.AllowedInteractionTypes =
                MixedReality.Toolkit.SpatialManipulation.InteractionFlags.Near |
                MixedReality.Toolkit.SpatialManipulation.InteractionFlags.Ray |
                MixedReality.Toolkit.SpatialManipulation.InteractionFlags.Gaze |
                MixedReality.Toolkit.SpatialManipulation.InteractionFlags.Generic;
            ConfigureObjectManipulatorColliders(objectManipulator, objectParent);
            objectManipulator.enabled = true;
            EditorUtility.SetDirty(objectManipulator);

            var boundsControl = objectParent.GetComponent<MixedReality.Toolkit.SpatialManipulation.BoundsControl>();
            if (boundsControl == null)
                boundsControl = objectParent.AddComponent<MixedReality.Toolkit.SpatialManipulation.BoundsControl>();

            ConfigureBoundsControlSerialized(boundsControl, objectParent.transform, objectManipulator);
            boundsControl.enabled = true;
            ApplyMRTK3BoundsVisualParity(boundsControl);
            EditorUtility.SetDirty(boundsControl);
            EditorUtility.SetDirty(objectParent);
        }

        private static void RemovePersistedMRTK3BoundsVisuals(Transform objectParent)
        {
            var visualRoots = objectParent
                .GetComponentsInChildren<Transform>(includeInactive: true)
                .Where(child => child != objectParent &&
                                child.parent == objectParent &&
                                IsBoundsVisualPath(child))
                .ToArray();

            foreach (var visualRoot in visualRoots)
            {
                UnityEngine.Object.DestroyImmediate(visualRoot.gameObject);
            }
        }

        private static void ConfigureBoundsControlSerialized(
            MixedReality.Toolkit.SpatialManipulation.BoundsControl boundsControl,
            Transform target,
            MixedReality.Toolkit.SpatialManipulation.ObjectManipulator objectManipulator)
        {
            var boundsVisualsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MRTK3TraditionalBoundsVisualsPath);
            if (boundsVisualsPrefab == null)
                Debug.LogWarning($"Hanyang parity MRTK3 bounds visuals prefab not found: {MRTK3TraditionalBoundsVisualsPath}");

            var serializedObject = new SerializedObject(boundsControl);
            serializedObject.Update();

            SetSerializedObjectReference(serializedObject, "boundsVisualsPrefab", boundsVisualsPrefab);
            SetSerializedObjectReference(serializedObject, "target", target);
            SetSerializedObjectReference(serializedObject, "interactable", objectManipulator);
            SetSerializedEnum(serializedObject, "boundsCalculationMethod", (int)MixedReality.Toolkit.SpatialManipulation.BoundsCalculator.BoundsCalculationMethod.RendererOverCollider);
            SetSerializedEnum(serializedObject, "flattenMode", (int)MixedReality.Toolkit.SpatialManipulation.FlattenMode.Never);
            SetSerializedFloat(serializedObject, "boundsPadding", 0f);
            SetSerializedBool(serializedObject, "handlesActive", true);
            SetSerializedEnum(serializedObject, "enabledHandles",
                (int)(MixedReality.Toolkit.SpatialManipulation.HandleType.Rotation |
                      MixedReality.Toolkit.SpatialManipulation.HandleType.Scale));

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedObjectReference(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.objectReferenceValue = value;
        }

        private static void SetSerializedEnum(SerializedObject serializedObject, string propertyName, int value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.intValue = value;
        }

        private static void SetSerializedFloat(SerializedObject serializedObject, string propertyName, float value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.floatValue = value;
        }

        private static void SetSerializedBool(SerializedObject serializedObject, string propertyName, bool value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                property.boolValue = value;
        }

        private static void ConfigureObjectManipulatorColliders(
            MixedReality.Toolkit.SpatialManipulation.ObjectManipulator objectManipulator,
            GameObject objectParent)
        {
            var collider = EnsureObjectParentCollider(objectParent);
            objectManipulator.colliders.Clear();
            if (collider != null)
                objectManipulator.colliders.Add(collider);
        }

        private static Collider EnsureObjectParentCollider(GameObject objectParent)
        {
            var collider = objectParent.GetComponent<BoxCollider>();
            if (collider == null)
                collider = objectParent.AddComponent<BoxCollider>();

            // Keep the legacy HoloLens BoundsControl override from unity.unity.
            // Recomputing from renderers makes MRTK3 bounds smaller and shifts them down.
            collider.center = LegacyObjectParentColliderCenter;
            collider.size = LegacyObjectParentColliderSize;
            collider.isTrigger = false;
            EditorUtility.SetDirty(collider);
            return collider;
        }

        private static void ApplyMRTK3BoundsVisualParity(MixedReality.Toolkit.SpatialManipulation.BoundsControl boundsControl)
        {
            foreach (var renderer in boundsControl.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                var path = GetPath(renderer.transform);
                if (!IsBoundsVisualPath(renderer.transform))
                    continue;

                if (path.IndexOf("/Box", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ApplyLegacyBoundsShell(renderer);
                    continue;
                }

                if (path.IndexOf("ScaleHandle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("RotateHandle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("Manipulator", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ApplyBlueBoundsHandle(renderer);
                }
            }
        }

        private static bool IsBoundsVisualPath(Transform transform)
        {
            var path = GetPath(transform);
            return path.IndexOf("BoundingBoxWith", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("/rigRoot/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("/rigRoot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("ScaleHandle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("RotateHandle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("midpoint_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("corner_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ApplyLegacyBoundsShell(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            var shellColor = new Color(0.31f, 0.31f, 0.31f, 1f);
            var innerColor = new Color(0.4f, 0.4f, 0.4f, 0.24f);
            SetPropertyBlockColor(block, "_Color_", shellColor);
            SetPropertyBlockColor(block, "_Inner_Color_", innerColor);
            SetPropertyBlockColor(block, "_Color_At_0_", new Color(0f, 0f, 0f, 0f));
            SetPropertyBlockColor(block, "_Color_At_1_", new Color(0.52f, 0.52f, 0.52f, 1f));
            SetPropertyBlockColor(block, "_Selection_Color_", new Color(0.108f, 0.565f, 1f, 1f));
            SetPropertyBlockFloat(block, "_Show_Internal_", 1f);
            SetPropertyBlockFloat(block, "_Show_Internal_Back_", 1f);
            SetPropertyBlockFloat(block, "_Show_Internal_Front_", 1f);
            SetPropertyBlockFloat(block, "_Always_Show_Internal_", 1f);
            SetPropertyBlockFloat(block, "_Gaze_Focus_", 1f);
            SetPropertyBlockFloat(block, "_Focus_Max_Intensity_", 0.55f);
            SetPropertyBlockFloat(block, "_Proximity_Max_Intensity_", 0.55f);
            SetPropertyBlockFloat(block, "_Xray_Alpha_Multiplier_", 0.58f);
            SetPropertyBlockFloat(block, "_Xray_Intensity_", 0.12f);
            renderer.SetPropertyBlock(block);
        }

        private static void ApplyBlueBoundsHandle(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            ApplyBlueBoundsHandle(block);
            renderer.SetPropertyBlock(block);
        }

        private static void EnsureCameraVisualParity(Camera camera)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 10f;
            camera.fieldOfView = 60f;
            EditorUtility.SetDirty(camera);
        }

        private static void NormalizeRigCameraHeight(GameObject rig)
        {
            var cameraOffset = rig.transform.Find("Camera Offset");
            if (cameraOffset != null)
            {
                cameraOffset.localPosition = Vector3.zero;
                cameraOffset.localRotation = Quaternion.identity;
                cameraOffset.localScale = Vector3.one;
                EditorUtility.SetDirty(cameraOffset);
            }

            var xrOriginType = FindType("Unity.XR.CoreUtils.XROrigin");
            if (xrOriginType == null)
                return;

            foreach (var component in rig.GetComponentsInChildren(xrOriginType, includeInactive: true))
            {
                if (component == null)
                    continue;

                var serializedObject = new SerializedObject(component);
                SetFloatProperty(serializedObject, "m_CameraYOffset", 0f);
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(component);
            }

            Debug.Log($"{MRTK3XRRigName} camera height normalized to the original HoloLens scene baseline.");
        }

        private static void EnsureUniversalAdditionalCameraData(Camera camera)
        {
            var cameraDataType = FindType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if (cameraDataType == null)
            {
                Debug.LogWarning($"Could not find UniversalAdditionalCameraData while configuring {camera.name}.");
                return;
            }

            if (camera.GetComponent(cameraDataType) != null)
                return;

            camera.gameObject.AddComponent(cameraDataType);
            EditorUtility.SetDirty(camera.gameObject);
        }

        private static int EnsureUnity6HolographicMaterials()
        {
            var materialCount = 0;
            materialCount += AssignShaderToMaterialsByName(
                HolographicBackplateMaterialPaths,
                "Universal Render Pipeline/Unlit",
                material => ConfigureBackplateMaterial(material, material.name.IndexOf("BorderOnly", StringComparison.OrdinalIgnoreCase) >= 0));
            materialCount += AssignShaderToMaterials(
                HolographicFrontplateMaterialPaths,
                GraphicsToolsFrontplateShaderGuid,
                ConfigureFrontplateMaterial);
            materialCount += AssignShaderToMaterials(
                HolographicStandardMaterialPaths,
                GraphicsToolsStandardShaderGuid,
                ConfigureStandardMaterial);
            materialCount += AssignShaderToHolographicButtonIconMaterials();
            materialCount += AssignShaderToMaterialsByName(
                VesselMaterialPaths,
                "Universal Render Pipeline/Lit",
                ConfigureVesselMaterial);
            materialCount += AssignShaderToMaterials(
                LegacyBoundingBoxShellMaterialPaths,
                GraphicsToolsStandardShaderGuid,
                ConfigureInvisibleBoundingBoxShellMaterial);
            materialCount += AssignShaderToMaterialsByName(
                LegacyBoundingBoxHandleMaterialPaths,
                "Universal Render Pipeline/Lit",
                ConfigureBoundingBoxHandleMaterial);
            materialCount += AssignShaderToMaterials(
                MRTK3BoundsHandleMaterialPaths,
                GraphicsToolsStandardShaderGuid,
                ConfigureMRTK3BoundsHandleMaterial);

            return materialCount;
        }

        private static int AssignShaderToHolographicButtonIconMaterials()
        {
            var shader = LoadShaderByGuid(GraphicsToolsStandardShaderGuid);
            var materialPaths = AssetDatabase
                .FindAssets("HolographicButtonIcon t:Material", HolographicButtonIconMaterialSearchFolders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith("HolographicButtonIcon", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToArray();

            var materialCount = 0;
            foreach (var materialPath in materialPaths)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    Debug.LogWarning($"Hanyang parity button icon material not found: {materialPath}");
                    continue;
                }

                material.shader = shader;
                ConfigureButtonIconMaterial(material);
                EditorUtility.SetDirty(material);
                materialCount++;
            }

            return materialCount;
        }

        private static int AssignShaderToMaterials(IEnumerable<string> materialPaths, string shaderGuid, Action<Material> configureMaterial)
        {
            var shader = LoadShaderByGuid(shaderGuid);
            var materialCount = 0;
            foreach (var materialPath in materialPaths)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    Debug.LogWarning($"Hanyang parity material not found: {materialPath}");
                    continue;
                }

                material.shader = shader;
                configureMaterial(material);
                EditorUtility.SetDirty(material);
                materialCount++;
            }

            return materialCount;
        }

        private static int AssignShaderToMaterialsByName(IEnumerable<string> materialPaths, string shaderName, Action<Material> configureMaterial)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Hanyang parity shader not found by name: {shaderName}");
                return 0;
            }

            var materialCount = 0;
            foreach (var materialPath in materialPaths)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    Debug.LogWarning($"Hanyang parity material not found: {materialPath}");
                    continue;
                }

                material.shader = shader;
                configureMaterial(material);
                EditorUtility.SetDirty(material);
                materialCount++;
            }

            return materialCount;
        }

        private static Shader LoadShaderByGuid(string shaderGuid)
        {
            var shaderPath = AssetDatabase.GUIDToAssetPath(shaderGuid);
            if (string.IsNullOrWhiteSpace(shaderPath))
                throw new FileNotFoundException($"Could not resolve shader GUID {shaderGuid}.");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
                throw new FileNotFoundException($"Could not load shader for GUID {shaderGuid}.", shaderPath);

            return shader;
        }

        private static void ConfigureBackplateMaterial(Material material, bool borderOnly)
        {
            if (material.shader != null && material.shader.name.IndexOf("Universal Render Pipeline/Unlit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ConfigureUnlitBackplateMaterial(material, borderOnly);
                return;
            }

            material.enableInstancing = true;
            material.renderQueue = -1;
            material.EnableKeyword("_GRADIENT_ENABLE_");
            material.EnableKeyword("_IRIDESCENCE_ENABLE_");
            material.EnableKeyword("_LINE_ENABLE_");
            material.EnableKeyword("_SMOOTH_EDGES_");

            SetMaterialFloat(material, "_Absolute_Sizes_", 1f);
            SetMaterialFloat(material, "_Edge_Only_", borderOnly ? 1f : 0f);
            SetMaterialFloat(material, "_Edge_Width_", 0.5f);
            SetMaterialFloat(material, "_Gradient_Enable_", borderOnly ? 0f : 1f);
            SetMaterialFloat(material, "_Iridescence_Enable_", 1f);
            SetMaterialFloat(material, "_Iridescence_Intensity_", 0.371f);
            SetMaterialFloat(material, "_Line_Enable_", 1f);
            SetMaterialFloat(material, "_Line_Width_", 0.001f);
            SetMaterialFloat(material, "_Radius_", 0.01f);
            SetMaterialFloat(material, "_Smooth_Edges_", 1f);
            SetMaterialTexture(material, "_Iridescent_Map_", LoadTextureByGuid(GraphicsToolsIridescentMapGuid));
            var baseColor = borderOnly ? new Color(0f, 0f, 0f, 0f) : new Color(0.012f, 0.06f, 0.24f, 1f);
            SetMaterialColor(material, "_Base_Color_", baseColor);
            SetMaterialColor(material, "_Color", baseColor);
            SetMaterialColor(material, "_Color_", baseColor);
            SetMaterialColor(material, "_Inner_Color_", new Color(0.012f, 0.18f, 0.55f, 1f));
            SetMaterialColor(material, "_Top_Left_", new Color(0f, 0.251f, 0.502f, 1f));
            SetMaterialColor(material, "_Top_Right_", new Color(0.26030594f, 0f, 0.851f, 1f));
            SetMaterialColor(material, "_Bottom_Left_", new Color(0.098223545f, 0.22895679f, 0.9921568f, 1f));
            SetMaterialColor(material, "_Bottom_Right_", new Color(0.124633946f, 0.124633946f, 0.617f, 1f));
            SetMaterialColor(material, "_Line_Color_", new Color(0.30860388f, 0.40065098f, 0.6039216f, 1f));
            SetMaterialColor(material, "_Iridescence_Tint_", Color.white);
        }

        private static void ConfigureUnlitBackplateMaterial(Material material, bool borderOnly)
        {
            material.enableInstancing = true;
            var backplateColor = borderOnly ? new Color(0f, 0f, 0f, 0f) : new Color(0.006f, 0.04f, 0.30f, 1f);

            if (borderOnly)
            {
                material.renderQueue = 3000;
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
                SetMaterialFloat(material, "_Surface", 1f);
                SetMaterialFloat(material, "_Blend", 1f);
                SetMaterialFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                SetMaterialFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                SetMaterialFloat(material, "_ZWrite", 0f);
            }
            else
            {
                material.renderQueue = 2000;
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_SURFACE_TYPE_OPAQUE");
                SetMaterialFloat(material, "_Surface", 0f);
                SetMaterialFloat(material, "_Blend", 0f);
                SetMaterialFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                SetMaterialFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                SetMaterialFloat(material, "_ZWrite", 1f);
            }

            SetMaterialFloat(material, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            SetMaterialFloat(material, "_AlphaClip", 0f);
            SetMaterialColor(material, "_BaseColor", backplateColor);
            SetMaterialColor(material, "_Color", backplateColor);
        }

        private static void ConfigureFrontplateMaterial(Material material)
        {
            material.enableInstancing = true;
            material.renderQueue = -1;
            material.EnableKeyword("_BLOB_ENABLE__ON");
            material.EnableKeyword("_BLOB_ENABLE_2__ON");
            material.EnableKeyword("_SMOOTH_EDGES__ON");

            SetMaterialFloat(material, "_Blob_Enable_", 1f);
            SetMaterialFloat(material, "_Blob_Enable_2_", 1f);
            SetMaterialFloat(material, "_Blob_Fade_", 1f);
            SetMaterialFloat(material, "_Blob_Fade_2_", 1f);
            SetMaterialFloat(material, "_Blob_Intensity_", 0.5f);
            SetMaterialFloat(material, "_Line_Width_", 0.001f);
            SetMaterialFloat(material, "_Proximity_Max_Intensity_", 0.45f);
            SetMaterialFloat(material, "_Radius_", 0.01f);
            SetMaterialFloat(material, "_Selection_Fade_", 0f);
            SetMaterialFloat(material, "_Smooth_Edges_", 1f);
            SetMaterialFloat(material, "_Use_Global_Left_Index_", 1f);
            SetMaterialFloat(material, "_Use_Global_Right_Index_", 1f);
            SetMaterialTexture(material, "_Blob_Texture_", LoadTextureByGuid(GraphicsToolsBlobTextureGuid));
            SetMaterialColor(material, "_Edge_Color_", new Color(0.53f, 0.53f, 0.53f, 1f));
        }

        private static void ConfigureStandardMaterial(Material material)
        {
            material.enableInstancing = false;
            material.renderQueue = 2000;
            SetMaterialFloat(material, "_Mode", 0f);
            SetMaterialFloat(material, "_SrcBlend", 1f);
            SetMaterialFloat(material, "_DstBlend", 0f);
            SetMaterialFloat(material, "_ZWrite", 1f);
            SetMaterialFloat(material, "_UseWorldScale", 1f);
            SetMaterialColor(material, "_Color", Color.white);
            SetMaterialColor(material, "_EmissionColor", new Color(0f, 0f, 0f, 0f));
        }

        private static void ConfigureButtonIconMaterial(Material material)
        {
            material.enableInstancing = false;
            material.renderQueue = 2450;
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.EnableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_USECOLOR_ON");
            material.EnableKeyword("_USEMAINTEX_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SURFACE_TYPE_OPAQUE");

            SetMaterialFloat(material, "_Mode", 1f);
            SetMaterialFloat(material, "_Surface", 0f);
            SetMaterialFloat(material, "_Blend", 0f);
            SetMaterialFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SetMaterialFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            SetMaterialFloat(material, "_ZWrite", 1f);
            SetMaterialFloat(material, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            SetMaterialFloat(material, "_AlphaClip", 1f);
            SetMaterialFloat(material, "_Cutoff", 0.5f);
            SetMaterialFloat(material, "_UseColor", 1f);
            SetMaterialFloat(material, "_UseMainTex", 1f);
            SetMaterialFloat(material, "_UseWorldScale", 1f);
            SetMaterialColor(material, "_BaseColor", Color.white);
            SetMaterialColor(material, "_Color", Color.white);
            SetMaterialColor(material, "_EmissionColor", new Color(0f, 0f, 0f, 0f));
        }

        private static void ConfigureVesselMaterial(Material material)
        {
            material.enableInstancing = true;
            material.renderQueue = 3000;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            SetMaterialFloat(material, "_Surface", 1f);
            SetMaterialFloat(material, "_Blend", 1f);
            SetMaterialFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SetMaterialFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetMaterialFloat(material, "_SrcBlendAlpha", 1f);
            SetMaterialFloat(material, "_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetMaterialFloat(material, "_ZWrite", 0f);
            SetMaterialFloat(material, "_Metallic", 0.568f);
            SetMaterialFloat(material, "_Smoothness", 1f);
            SetMaterialFloat(material, "_Glossiness", 0f);
            var vesselColor = new Color(0.509434f, 0.509434f, 0.509434f, 0.48235294f);
            SetMaterialColor(material, "_BaseColor", vesselColor);
            SetMaterialColor(material, "_Color", vesselColor);
        }

        private static void ConfigureInvisibleBoundingBoxShellMaterial(Material material)
        {
            material.enableInstancing = true;
            material.renderQueue = 3000;
            material.SetOverrideTag("RenderType", "Fade");

            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHABLEND_TRANS_ON");
            material.DisableKeyword("_ADDITIVE_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_BORDER_LIGHT");
            material.EnableKeyword("_DISABLE_ALBEDO_MAP");
            material.EnableKeyword("_HOVER_LIGHT");
            material.EnableKeyword("_NEAR_LIGHT_FADE");
            material.EnableKeyword("_NEAR_PLANE_FADE");
            material.EnableKeyword("_NEAR_PLANE_FADE_REVERSE");
            material.EnableKeyword("_PROXIMITY_LIGHT");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_SURFACE_TYPE_OPAQUE");

            SetMaterialFloat(material, "_Mode", 5f);
            SetMaterialFloat(material, "_CustomMode", 2f);
            SetMaterialFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SetMaterialFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SetMaterialFloat(material, "_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            SetMaterialFloat(material, "_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            SetMaterialFloat(material, "_BlendOp", (float)UnityEngine.Rendering.BlendOp.Add);
            SetMaterialFloat(material, "_ZWrite", 0f);
            SetMaterialFloat(material, "_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            SetMaterialFloat(material, "_CullMode", (float)UnityEngine.Rendering.CullMode.Off);
            SetMaterialFloat(material, "_ColorWriteMask", (float)UnityEngine.Rendering.ColorWriteMask.All);
            SetMaterialFloat(material, "_Fade", 1f);
            SetMaterialFloat(material, "_DirectionalLight", 0f);
            SetMaterialFloat(material, "_Metallic", 0f);
            SetMaterialFloat(material, "_Smoothness", 0.5f);
            SetMaterialFloat(material, "_BorderLight", 1f);
            SetMaterialFloat(material, "_BorderLightOpaque", 0f);
            SetMaterialFloat(material, "_BorderLightOpaqueAlpha", 1f);
            SetMaterialFloat(material, "_BorderLightReplacesAlbedo", 0f);
            SetMaterialFloat(material, "_BorderLightUsesHoverColor", 1f);
            SetMaterialFloat(material, "_BorderMinValue", 1f);
            SetMaterialFloat(material, "_BorderWidth", 0.016f);
            SetMaterialFloat(material, "_HoverLight", 1f);
            SetMaterialFloat(material, "_NearLightFade", 1f);
            SetMaterialFloat(material, "_NearPlaneFade", 1f);
            SetMaterialFloat(material, "_NearPlaneFadeReverse", 1f);
            SetMaterialFloat(material, "_ProximityLight", 1f);
            SetMaterialFloat(material, "_SpecularHighlights", 0f);
            var transparentShell = new Color(0.1254902f, 0.1254902f, 0.1254902f, 0f);
            SetMaterialColor(material, "_Color", transparentShell);
            SetMaterialColor(material, "_BaseColor", transparentShell);
        }

        private static void ConfigureBoundingBoxHandleMaterial(Material material)
        {
            material.enableInstancing = true;
            material.renderQueue = 2000;
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_SURFACE_TYPE_OPAQUE");
            SetMaterialFloat(material, "_Surface", 0f);
            SetMaterialFloat(material, "_Blend", 0f);
            SetMaterialFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SetMaterialFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            SetMaterialFloat(material, "_ZWrite", 1f);
            SetMaterialFloat(material, "_Metallic", 0f);
            SetMaterialFloat(material, "_Smoothness", 0.35f);
            var handleColor = new Color(0.10784314f, 0.5647059f, 1f, 1f);
            SetMaterialColor(material, "_BaseColor", handleColor);
            SetMaterialColor(material, "_Color", handleColor);
        }

        private static void ConfigureMRTK3BoundsHandleMaterial(Material material)
        {
            material.enableInstancing = true;
            material.renderQueue = 3999;
            SetMaterialFloat(material, "_RenderQueueOverride", 3999f);
            SetMaterialFloat(material, "_Mode", 5f);
            SetMaterialFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            SetMaterialFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            SetMaterialFloat(material, "_ZWrite", 1f);
            SetMaterialFloat(material, "_Smoothness", 0.35f);
            SetMaterialFloat(material, "_Metallic", 0f);
            SetMaterialFloat(material, "_BorderLight", 0f);
            SetMaterialFloat(material, "_HoverLight", 0f);
            SetMaterialFloat(material, "_ProximityLight", 0f);

            var handleColor = new Color(0.10784314f, 0.5647059f, 1f, 1f);
            SetMaterialColor(material, "_Color", handleColor);
            SetMaterialColor(material, "_BaseColor", handleColor);
            SetMaterialColor(material, "_Base_Color_", handleColor);
            SetMaterialColor(material, "_EmissiveColor", handleColor);
            SetMaterialColor(material, "_EnvironmentColorX", handleColor);
            SetMaterialColor(material, "_EnvironmentColorY", handleColor);
            SetMaterialColor(material, "_EnvironmentColorZ", new Color(0.65f, 0.87f, 1f, 1f));
            SetMaterialColor(material, "_GradientColor2", handleColor);
            SetMaterialColor(material, "_GradientColor3", handleColor);
            SetMaterialColor(material, "_BorderColor", new Color(0.78f, 0.9f, 1f, 0.8f));
            SetMaterialColor(material, "_HoverColorOpaqueOverride", handleColor);
        }

        private static void SetMaterialFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        private static void SetMaterialColor(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
                material.SetColor(propertyName, value);
        }

        private static void SetMaterialTexture(Material material, string propertyName, Texture value)
        {
            if (value != null && material.HasProperty(propertyName))
                material.SetTexture(propertyName, value);
        }

        private static void SetPropertyBlockFloat(MaterialPropertyBlock block, string propertyName, float value)
        {
            block.SetFloat(Shader.PropertyToID(propertyName), value);
        }

        private static void SetPropertyBlockColor(MaterialPropertyBlock block, string propertyName, Color value)
        {
            block.SetColor(Shader.PropertyToID(propertyName), value);
        }

        private static void ApplyBlueBoundsHandle(MaterialPropertyBlock block)
        {
            var handleColor = new Color(0.10784314f, 0.5647059f, 1f, 1f);
            SetPropertyBlockColor(block, "_Color", handleColor);
            SetPropertyBlockColor(block, "_BaseColor", handleColor);
            SetPropertyBlockColor(block, "_Base_Color_", handleColor);
            SetPropertyBlockColor(block, "_EmissiveColor", handleColor);
            SetPropertyBlockColor(block, "_EnvironmentColorX", handleColor);
            SetPropertyBlockColor(block, "_EnvironmentColorY", handleColor);
            SetPropertyBlockColor(block, "_EnvironmentColorZ", new Color(0.65f, 0.87f, 1f, 1f));
            SetPropertyBlockColor(block, "_GradientColor2", handleColor);
            SetPropertyBlockColor(block, "_GradientColor3", handleColor);
            SetPropertyBlockColor(block, "_BorderColor", new Color(0.78f, 0.9f, 1f, 0.8f));
            SetPropertyBlockFloat(block, "_BorderLight", 0f);
            SetPropertyBlockFloat(block, "_HoverLight", 0f);
            SetPropertyBlockFloat(block, "_ProximityLight", 0f);
        }

        private static Texture LoadTextureByGuid(string textureGuid)
        {
            var texturePath = AssetDatabase.GUIDToAssetPath(textureGuid);
            return string.IsNullOrWhiteSpace(texturePath) ? null : AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
        }

        private static void HandlePlayModeCaptureAutomation()
        {
            if (SessionState.GetBool(PlayModeEnterPendingKey, false) &&
                !EditorApplication.isPlayingOrWillChangePlaymode &&
                !EditorApplication.isCompiling &&
                !EditorApplication.isUpdating)
            {
                SessionState.SetBool(PlayModeEnterPendingKey, false);
                Debug.Log("Hanyang parity entering Play Mode for editor comparison.");
                EditorApplication.isPlaying = true;
                return;
            }

            if (!SessionState.GetBool(PlayModeCapturePendingKey, false))
                return;

            if (!EditorApplication.isPlaying)
                return;

            var startTime = SessionState.GetFloat(PlayModeCaptureStartTimeKey, 0f);
            if (startTime <= 0f)
            {
                SessionState.SetFloat(PlayModeCaptureStartTimeKey, (float)EditorApplication.timeSinceStartup);
                return;
            }

            if (EditorApplication.timeSinceStartup - startTime < PlayModeCaptureDelaySeconds)
                return;

            var capturePath = SessionState.GetString(
                PlayModeCapturePathKey,
                Path.GetFullPath(".omx/evidence/hanyang-parity/gate-14/current-playmode.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(capturePath) ?? ".");
            ApplyPlayModeBoundsVisualParity();
            LogPlayModeVisualSurface();
            CaptureMainCamera(capturePath);
            ScreenCapture.CaptureScreenshot(capturePath);
            SessionState.SetBool(PlayModeCapturePendingKey, false);
            Debug.Log($"Hanyang parity Play Mode screenshot requested: {capturePath}");
        }

        private static void ApplyPlayModeBoundsVisualParity()
        {
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                var path = GetPath(renderer.transform);
                if (path.IndexOf("BoundingBoxWithTraditionalHandles", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                renderer.enabled = false;
            }
        }

        private static void ReportShaderSupport(string label, string shaderGuid)
        {
            var shaderPath = AssetDatabase.GUIDToAssetPath(shaderGuid);
            var shader = string.IsNullOrWhiteSpace(shaderPath) ? null : AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Debug.Log($"Visual shader support: {label} guid={shaderGuid} path={(string.IsNullOrWhiteSpace(shaderPath) ? "(unresolved)" : shaderPath)} name={shader?.name ?? "(none)"} supported={shader != null && shader.isSupported}");
        }

        private static void LogPlayModeVisualSurface()
        {
            var activePipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            Debug.Log($"Hanyang parity Play Mode render pipeline: {(activePipeline == null ? "(built-in)" : activePipeline.name)}");

            var cameras = Resources.FindObjectsOfTypeAll<Camera>()
                .Where(IsSceneObject)
                .OrderBy(camera => GetPath(camera.transform))
                .ToArray();
            if (Camera.main == null)
            {
                Debug.LogWarning("Hanyang parity Play Mode camera report: Camera.main is null.");
            }

            foreach (var camera in cameras)
            {
                Debug.Log($"Hanyang parity Play Mode camera report: path={GetPath(camera.transform)} tag={camera.tag} enabled={camera.enabled} active={camera.gameObject.activeInHierarchy} depth={camera.depth} clear={camera.clearFlags} bg={FormatColor(camera.backgroundColor)} near={camera.nearClipPlane} far={camera.farClipPlane} fov={camera.fieldOfView} cullingMask=0x{camera.cullingMask:X8} target={(camera.targetTexture == null ? "(screen)" : camera.targetTexture.name)} main={camera == Camera.main}");
            }

            var materialNames = new HashSet<string>(HolographicBackplateMaterialPaths
                .Concat(HolographicFrontplateMaterialPaths)
                .Concat(HolographicStandardMaterialPaths)
                .Select(Path.GetFileNameWithoutExtension));
            var materials = Resources.FindObjectsOfTypeAll<Material>()
                .Where(material => material != null && materialNames.Contains(material.name))
                .OrderBy(material => material.name)
                .ToArray();
            foreach (var material in materials)
            {
                Debug.Log($"Hanyang parity Play Mode material report: {material.name} shader={material.shader?.name ?? "(none)"} supported={material.shader != null && material.shader.isSupported} renderQueue={material.renderQueue}");
            }

            var renderers = Resources.FindObjectsOfTypeAll<Renderer>()
                .Where(IsSceneObject)
                .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy)
                .OrderBy(renderer => GetPath(renderer.transform))
                .ToArray();
            Debug.Log($"Hanyang parity Play Mode active renderer count: {renderers.Length} bounds={FormatBounds(CalculateBounds(renderers))}");
            foreach (var renderer in renderers)
            {
                var path = GetPath(renderer.transform);
                var shouldReport = renderers.Length <= 80 ||
                                   path.IndexOf("Main Menu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   path.IndexOf("Object Parent", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   path.IndexOf("Blood Vessel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   renderer.sharedMaterials.Any(material => material != null && materialNames.Contains(material.name));
                if (!shouldReport)
                    continue;

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                Debug.Log($"Hanyang parity Play Mode renderer report: path={path} type={renderer.GetType().Name} layer={LayerMask.LayerToName(renderer.gameObject.layer)} enabled={renderer.enabled} bounds={FormatBounds(renderer.bounds)} materials={DescribeMaterials(renderer)} block={DescribeMaterialPropertyBlock(block)}");
            }
        }

        private static void CaptureMainCamera(string screenCapturePath)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("Hanyang parity Main Camera direct capture skipped: Camera.main is null.");
                return;
            }

            var cameraCapturePath = GetSiblingCapturePath(screenCapturePath, "-camera-main");
            var previousTargetTexture = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var renderTexture = new RenderTexture(PlayModeCameraCaptureWidth, PlayModeCameraCaptureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "HanyangParityMainCameraCapture"
            };
            var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);

            try
            {
                camera.targetTexture = renderTexture;
                RenderTexture.active = renderTexture;
                camera.Render();
                texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(cameraCapturePath, texture.EncodeToPNG());
                Debug.Log($"Hanyang parity Main Camera direct screenshot written: {cameraCapturePath}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Hanyang parity Main Camera direct screenshot failed: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                camera.targetTexture = previousTargetTexture;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
        }

        private static string GetSiblingCapturePath(string originalPath, string suffix)
        {
            var directory = Path.GetDirectoryName(originalPath);
            var fileName = Path.GetFileNameWithoutExtension(originalPath);
            var extension = Path.GetExtension(originalPath);
            return Path.Combine(string.IsNullOrWhiteSpace(directory) ? "." : directory, $"{fileName}{suffix}{extension}");
        }

        private static void SetBoolProperty(SerializedObject serializedObject, string propertyPath, bool value)
        {
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
                return;

            property.boolValue = value;
        }

        private static int SetBoolPropertiesByName(SerializedObject serializedObject, string propertyName, bool value)
        {
            var changedCount = 0;
            var property = serializedObject.GetIterator();
            var enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = true;
                if (property.name != propertyName || property.propertyType != SerializedPropertyType.Boolean)
                    continue;

                if (property.boolValue == value)
                    continue;

                property.boolValue = value;
                changedCount++;
            }

            return changedCount;
        }

        private static void SetIntProperty(SerializedObject serializedObject, string propertyPath, int value)
        {
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
                return;

            property.intValue = value;
        }

        private static void SetFloatProperty(SerializedObject serializedObject, string propertyPath, float value)
        {
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
                return;

            property.floatValue = value;
        }

        private static void SetVector3Property(SerializedObject serializedObject, string propertyPath, Vector3 value)
        {
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
                return;

            property.vector3Value = value;
        }

        private static void SetObjectReferenceProperty(SerializedObject serializedObject, string propertyPath, UnityEngine.Object value)
        {
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
                return;

            property.objectReferenceValue = value;
        }

        private static void ConfigurePlayerSettings(BuildTargetGroup group, string bundleIdentifier)
        {
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
            PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.IL2CPP);
            ConfigureInputHandling();
            if (!string.IsNullOrWhiteSpace(bundleIdentifier))
                PlayerSettings.SetApplicationIdentifier(namedTarget, GetArgument("-bundleId", bundleIdentifier));
        }

        private static void ConfigureInputHandling()
        {
            var settings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton(nameof(PlayerSettings)));
            var activeInputHandler = settings.FindProperty("activeInputHandler");
            if (activeInputHandler == null)
            {
                Debug.LogWarning("Could not find PlayerSettings.activeInputHandler while configuring parity input handling.");
                return;
            }

            if (activeInputHandler.intValue == 1)
                return;

            activeInputHandler.intValue = 1;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("PlayerSettings.activeInputHandler configured to Input System Package (New) for MRTK3/XRI parity.");
        }

        private static void ConfigureXRLoader(BuildTargetGroup group, string loaderTypeFullName, string fallbackAssetPath)
        {
            var perBuildTargetSettings = FindOrCreateXRGeneralSettings();

            if (!perBuildTargetSettings.HasSettingsForBuildTarget(group))
                perBuildTargetSettings.CreateDefaultSettingsForBuildTarget(group);

            if (!perBuildTargetSettings.HasManagerSettingsForBuildTarget(group))
                perBuildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(group);

            var manager = perBuildTargetSettings.ManagerSettingsForBuildTarget(group);
            if (manager == null)
            {
                Debug.LogWarning($"XR manager settings for {group} could not be created.");
                return;
            }

            manager.automaticLoading = true;
            manager.automaticRunning = true;

            var loaderType = FindType(loaderTypeFullName);
            if (loaderType == null)
            {
                Debug.LogWarning($"XR loader type is not available: {loaderTypeFullName}. Required package may be missing.");
                EditorUtility.SetDirty(manager);
                EditorUtility.SetDirty(perBuildTargetSettings);
                return;
            }

            if (manager.activeLoaders.Any(loader => loader != null && loader.GetType() == loaderType))
            {
                EditorUtility.SetDirty(manager);
                EditorUtility.SetDirty(perBuildTargetSettings);
                return;
            }

            var assigned = XRPackageMetadataStore.AssignLoader(manager, loaderType.FullName, group);
            if (!assigned)
            {
                var loader = FindOrCreateLoaderAsset(loaderType, fallbackAssetPath);
                if (loader == null || !manager.TryAddLoader(loader))
                    Debug.LogWarning($"Failed to assign XR loader {loaderType.FullName} for {group}.");
            }

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(perBuildTargetSettings);
        }

        private static void ConfigureVisionOSSdk(bool useSimulatorSdk)
        {
            var sdkType = FindTypeByName("VisionOSSdkVersion");
            if (sdkType == null || !sdkType.IsEnum)
            {
                Debug.LogWarning("VisionOSSdkVersion enum is not available in this Unity editor.");
                return;
            }

            var sdkNames = Enum.GetNames(sdkType);
            Debug.Log($"VisionOSSdkVersion values: {string.Join(", ", sdkNames)}");

            var sdkName = useSimulatorSdk
                ? sdkNames.FirstOrDefault(name => name.IndexOf("sim", StringComparison.OrdinalIgnoreCase) >= 0)
                : sdkNames.FirstOrDefault(name => name.IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0) ?? sdkNames.FirstOrDefault();

            if (string.IsNullOrEmpty(sdkName))
                throw new InvalidOperationException($"No VisionOSSdkVersion value is available. Values: {string.Join(", ", sdkNames)}");

            var sdkValue = Enum.Parse(sdkType, sdkName);
            var property = typeof(PlayerSettings).GetProperty("VisionOSSdkVersion", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var setters = FindStaticMethodsByName("SetVisionOSSdkVersion");
            if (property != null)
            {
                property.SetValue(null, sdkValue);
                Debug.Log($"Set visionOS SDK through UnityEditor.PlayerSettings.VisionOSSdkVersion.");
            }
            else if (TryInvokeSdkSetter(setters, sdkType, sdkValue))
            {
                Debug.Log("Set visionOS SDK through SetVisionOSSdkVersion().");
            }
            else if (TrySetVisionOSSdkByReflection(sdkType, sdkValue))
            {
                Debug.Log($"visionOS SDK configured via reflected PlayerSettings surface: {sdkName}");
            }
            else
            {
                throw new MissingMethodException("PlayerSettings", "VisionOSSdkVersion/SetVisionOSSdkVersion");
            }

            Debug.Log($"visionOS SDK configured: {sdkName}");
        }

        private static void ConfigureVisionOSMixedRealitySettings()
        {
            var settingsType = FindType("UnityEditor.XR.VisionOS.VisionOSSettings");
            if (settingsType == null)
            {
                Debug.LogWarning("VisionOSSettings type is not available in this Unity editor.");
                return;
            }

            var settings = GetOrCreateVisionOSSettings(settingsType);
            if (settings == null)
            {
                Debug.LogWarning("VisionOSSettings asset could not be created or loaded.");
                return;
            }

            SetEnumProperty(settings, "appMode", "RealityKit");
            SetStringProperty(settings, "handsTrackingUsageDescription", "This app uses hand tracking for spatial interaction.");
            SetStringProperty(settings, "worldSensingUsageDescription", "This app uses world sensing to align spatial content.");
            InvokeInstanceMethod(settings, "GetOrCreateRuntimeSettings");
            UpdateVisionOSCapabilityProfiles(settingsType, settings);

            EditorUtility.SetDirty((UnityEngine.Object)settings);
            AssetDatabase.SaveAssets();
            Debug.Log("VisionOSSettings configured for RealityKit with PolySpatial hand/world usage descriptions.");
        }

        private static void ConfigureVisionOSShaderSettings()
        {
            var globalSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(UniversalRenderPipelineGlobalSettingsPath);
            if (globalSettings == null)
            {
                Debug.LogWarning($"URP global settings asset not found: {UniversalRenderPipelineGlobalSettingsPath}");
                return;
            }

            var serializedObject = new SerializedObject(globalSettings);
            var changedCount = SetBoolPropertiesByName(serializedObject, "m_IncludeTerrainShaders", false);
            if (changedCount > 0)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(globalSettings);
            }

            Debug.Log($"visionOS shader settings configured. Disabled URP terrain shader inclusion fields: {changedCount}.");
        }

        private static object GetOrCreateVisionOSSettings(Type settingsType)
        {
            var currentSettingsProperty = settingsType.GetProperty("currentSettings", BindingFlags.Public | BindingFlags.Static);
            var settings = currentSettingsProperty?.GetValue(null);
            if (settings != null)
                return settings;

            var getOrCreateSettings = settingsType.GetMethod("GetOrCreateSettings", BindingFlags.Public | BindingFlags.Static);
            settings = getOrCreateSettings?.Invoke(null, null);
            if (settings == null)
                return null;

            if (currentSettingsProperty != null && currentSettingsProperty.CanWrite)
                currentSettingsProperty.SetValue(null, settings);

            if (settings is UnityEngine.Object unityObject)
                EditorUtility.SetDirty(unityObject);

            return settings;
        }

        private static void SetEnumProperty(object target, string propertyName, string enumName)
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite || !property.PropertyType.IsEnum)
                throw new MissingMemberException(target.GetType().FullName, propertyName);

            var value = Enum.Parse(property.PropertyType, enumName);
            property.SetValue(target, value);
        }

        private static void SetStringProperty(object target, string propertyName, string value)
        {
            var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
                throw new MissingMemberException(target.GetType().FullName, propertyName);

            property.SetValue(target, value);
        }

        private static object InvokeInstanceMethod(object target, string methodName)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return method?.Invoke(target, null);
        }

        private static void UpdateVisionOSCapabilityProfiles(Type settingsType, object settings)
        {
            var appModeProperty = settingsType.GetProperty("appMode", BindingFlags.Public | BindingFlags.Instance);
            var appMode = appModeProperty?.GetValue(settings);
            if (appMode == null)
                return;

            var editorUtilsType = FindType("UnityEditor.XR.VisionOS.VisionOSEditorUtils");
            var updateProfiles = editorUtilsType?
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(method =>
                {
                    if (method.Name != "UpdateSelectedCapabilityProfiles")
                        return false;

                    var parameters = method.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType == appMode.GetType();
                });
            if (updateProfiles == null)
                return;

            updateProfiles.Invoke(null, new[] { appMode });
        }

        private static bool TryInvokeSdkSetter(MethodInfo[] setters, Type sdkType, object sdkValue)
        {
            foreach (var setter in setters)
            {
                if (!TryBuildSdkSetterArguments(setter, sdkType, sdkValue, out var arguments))
                {
                    Debug.Log($"Skipped visionOS SDK setter candidate: {DescribeMethod(setter)}");
                    continue;
                }

                setter.Invoke(null, arguments);
                Debug.Log($"Invoked visionOS SDK setter: {DescribeMethod(setter)}");
                return true;
            }

            return false;
        }

        private static bool TryBuildSdkSetterArguments(MethodInfo setter, Type sdkType, object sdkValue, out object[] arguments)
        {
            var parameters = setter.GetParameters();
            arguments = new object[parameters.Length];
            var hasSdkParameter = false;

            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                if (TryConvertSdkArgument(sdkType, sdkValue, parameter.ParameterType, out var sdkArgument))
                {
                    arguments[index] = sdkArgument;
                    hasSdkParameter = true;
                    continue;
                }

                if (TryConvertVisionOSBuildTargetArgument(parameter.ParameterType, out var targetArgument))
                {
                    arguments[index] = targetArgument;
                    continue;
                }

                return false;
            }

            return hasSdkParameter;
        }

        private static bool TryConvertVisionOSBuildTargetArgument(Type targetType, out object argument)
        {
            argument = null;

            if (targetType == typeof(BuildTargetGroup))
            {
                argument = ParseBuildTargetGroup("VisionOS");
                return true;
            }

            if (targetType == typeof(BuildTarget))
            {
                argument = ParseBuildTarget("VisionOS");
                return true;
            }

            if (targetType == typeof(NamedBuildTarget))
            {
                argument = NamedBuildTarget.FromBuildTargetGroup(ParseBuildTargetGroup("VisionOS"));
                return true;
            }

            return false;
        }

        private static bool TrySetVisionOSSdkByReflection(Type sdkType, object sdkValue)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in GetTypesSafely(assembly))
                {
                    if (!IsVisionOSSettingsSurface(type))
                        continue;

                    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (!property.CanWrite || property.Name.IndexOf("sdk", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        if (!TryConvertSdkArgument(sdkType, sdkValue, property.PropertyType, out var argument))
                            continue;

                        property.SetValue(null, argument);
                        Debug.Log($"Set visionOS SDK through {type.FullName}.{property.Name}.");
                        return true;
                    }

                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (method.Name.IndexOf("sdk", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        var parameters = method.GetParameters();
                        if (parameters.Length != 1 || !TryConvertSdkArgument(sdkType, sdkValue, parameters[0].ParameterType, out var argument))
                            continue;

                        method.Invoke(null, new[] { argument });
                        Debug.Log($"Set visionOS SDK through {type.FullName}.{method.Name}().");
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsVisionOSSettingsSurface(Type type)
        {
            var fullName = type.FullName ?? string.Empty;
            return fullName.IndexOf("VisionOS", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryConvertSdkArgument(Type sdkType, object sdkValue, Type targetType, out object argument)
        {
            argument = null;

            if (targetType == sdkType)
            {
                argument = sdkValue;
                return true;
            }

            if (targetType.IsEnum && Enum.GetNames(targetType).SequenceEqual(Enum.GetNames(sdkType)))
            {
                argument = Enum.Parse(targetType, sdkValue.ToString());
                return true;
            }

            if (targetType == typeof(int))
            {
                argument = Convert.ToInt32(sdkValue);
                return true;
            }

            return false;
        }

        private static void ConfigureMRTKProfile(BuildTargetGroup group)
        {
            var settings = AssetDatabase.LoadAssetAtPath<MRTKSettings>(MRTKSettingsPath);
            var profile = AssetDatabase.LoadAssetAtPath<MRTKProfile>(MRTKProfilePath);

            if (settings == null)
                throw new FileNotFoundException("MRTK3 settings asset not found.", MRTKSettingsPath);

            if (profile == null)
                throw new FileNotFoundException("MRTK3 profile asset not found.", MRTKProfilePath);

            settings.SetProfileForBuildTarget(group, profile);
            EditorUtility.SetDirty(settings);
            Debug.Log($"MRTK3 profile '{profile.name}' configured for {group}.");
        }

        private static XRGeneralSettingsPerBuildTarget FindOrCreateXRGeneralSettings()
        {
            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget settings) && settings != null)
                return settings;

            var guid = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget").FirstOrDefault();
            if (!string.IsNullOrEmpty(guid))
            {
                var existingPath = AssetDatabase.GUIDToAssetPath(guid);
                settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(existingPath);
                if (settings != null)
                {
                    EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
                    return settings;
                }
            }

            EnsureAssetFolder(Path.GetDirectoryName(XRGeneralSettingsPath)?.Replace('\\', '/'));
            settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(settings, XRGeneralSettingsPath);
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            return settings;
        }

        private static XRLoader FindOrCreateLoaderAsset(Type loaderType, string assetPath)
        {
            var guid = AssetDatabase.FindAssets($"t:{loaderType.Name}").FirstOrDefault();
            if (!string.IsNullOrEmpty(guid))
                return AssetDatabase.LoadAssetAtPath<XRLoader>(AssetDatabase.GUIDToAssetPath(guid));

            if (!typeof(ScriptableObject).IsAssignableFrom(loaderType))
                return null;

            EnsureAssetFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            var asset = ScriptableObject.CreateInstance(loaderType) as XRLoader;
            if (asset == null)
                return null;

            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static void BuildPlayer(
            BuildTargetGroup group,
            BuildTarget target,
            string buildPath,
            string description,
            bool useVisionOSSimulatorSdk = false)
        {
            var fullBuildPath = Path.GetFullPath(buildPath);
            Directory.CreateDirectory(fullBuildPath);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = fullBuildPath,
                target = target,
                targetGroup = group,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"{description} build failed: {report.summary.result}");

            PatchVisionOSExport(group, fullBuildPath, report, useVisionOSSimulatorSdk);
            Debug.Log($"{description} exported to {fullBuildPath}");
        }

        private static void PatchVisionOSExport(
            BuildTargetGroup group,
            string fullBuildPath,
            BuildReport report,
            bool useVisionOSSimulatorSdk)
        {
            if (!string.Equals(group.ToString(), "VisionOS", StringComparison.OrdinalIgnoreCase))
                return;

            EnsurePolySpatialVisionOSPostprocess(fullBuildPath, report);
            PatchVisionOSSwiftSettings(fullBuildPath);
            PatchVisionOSNativeLibrary(fullBuildPath, useVisionOSSimulatorSdk);
            PatchVisionOSXcodeProject(fullBuildPath);
        }

        private static void EnsurePolySpatialVisionOSPostprocess(string fullBuildPath, BuildReport report)
        {
            var polySpatialAppPath = Path.Combine(fullBuildPath, "MainApp/UnityPolySpatialApp.swift");
            var pluginRoot = Path.Combine(fullBuildPath, "Libraries/com.unity.polyspatial.visionos/Plugins");
            var simulatorLibraryPath = Path.Combine(pluginRoot, "libPolySpatial_xrsimulator.a");
            var deviceLibraryPath = Path.Combine(pluginRoot, "libPolySpatial_xros.a");
            if (File.Exists(polySpatialAppPath) && (File.Exists(simulatorLibraryPath) || File.Exists(deviceLibraryPath)))
                return;

            var processorType = FindType("Unity.PolySpatial.Internals.Editor.VisionOSBuildProcessor");
            if (processorType == null)
                throw new InvalidOperationException("Unity PolySpatial visionOS build processor is unavailable.");

            var processor = Activator.CreateInstance(processorType, nonPublic: true);
            InvokeVisionOSBuildProcessor(processorType, processor, "DoPreprocessBuild", report);
            InvokeVisionOSBuildProcessor(processorType, processor, "DoPostprocessBuild", report);

            if (!File.Exists(polySpatialAppPath) || (!File.Exists(simulatorLibraryPath) && !File.Exists(deviceLibraryPath)))
            {
                throw new InvalidOperationException(
                    "Unity PolySpatial visionOS postprocess did not produce the RealityKit Swift app shell and native library.");
            }

            Debug.Log($"Applied PolySpatial visionOS RealityKit postprocess: {fullBuildPath}");
        }

        private static void InvokeVisionOSBuildProcessor(Type processorType, object processor, string methodName, BuildReport report)
        {
            var method = processorType.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
                throw new MissingMethodException(processorType.FullName, methodName);

            try
            {
                method.Invoke(processor, new object[] { report });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                throw new InvalidOperationException(
                    $"Unity PolySpatial visionOS build processor failed in {methodName}.",
                    exception.InnerException);
            }
        }

        private static void PatchVisionOSSwiftSettings(string fullBuildPath)
        {
            var compositorPath = Path.Combine(
                fullBuildPath,
                "Libraries/com.unity.xr.visionos/Runtime/Plugins/visionos/UnityCompositorSpace.swift");
            if (!File.Exists(compositorPath))
            {
                Debug.LogWarning($"visionOS Swift compositor file missing: {compositorPath}");
                return;
            }

            var source = File.ReadAllText(compositorPath);
            var patched = source
                .Replace(
                    "\n        .persistentSystemOverlays(VisionOSPersistentSystemOverlays)",
                    string.Empty)
                .Replace(LegacyCompositorVisionOSSettingsBlock, "import SwiftUI\n");

            var generatedSettingsPath = Path.Combine(fullBuildPath, "MainApp/UnityVisionOSSettings.swift");
            if (File.Exists(generatedSettingsPath))
            {
                if (!string.Equals(source, patched, StringComparison.Ordinal))
                {
                    File.WriteAllText(compositorPath, patched);
                    Debug.Log($"Patched visionOS Swift compositor settings: {compositorPath}");
                }

                PatchVisionOSGeneratedSettingsSwift(fullBuildPath);
                return;
            }

            if (patched.Contains("HanyangVisionOSSwiftSettingsApplied"))
            {
                if (!string.Equals(source, patched, StringComparison.Ordinal))
                    File.WriteAllText(compositorPath, patched);
                return;
            }

            const string importMarker = "import SwiftUI\n";
            if (!patched.Contains(importMarker))
            {
                Debug.LogWarning($"visionOS Swift compositor import marker missing: {compositorPath}");
                return;
            }

            File.WriteAllText(compositorPath, patched.Replace(importMarker, LegacyCompositorVisionOSSettingsBlock));
            Debug.Log($"Patched visionOS Swift compositor settings: {compositorPath}");
        }

        private const string LegacyCompositorVisionOSSettingsBlock = @"import SwiftUI

private let HanyangVisionOSSwiftSettingsApplied = true
var VisionOSEnableHighQualityRecordingMode = false
var VisionOSEnableFoveation = false
var VisionOSUpperLimbVisibility: Visibility = .automatic
var VisionOSPersistentSystemOverlays: Visibility = .automatic
var VisionOSImmersionStyle: ImmersionStyle = .automatic
var VisionOSSkipPresent = false
var VisionOSEDRHeadroom = 1.2
";

        private static void PatchVisionOSGeneratedSettingsSwift(string fullBuildPath)
        {
            var settingsPath = Path.Combine(fullBuildPath, "MainApp/UnityVisionOSSettings.swift");
            if (!File.Exists(settingsPath))
                return;

            var source = File.ReadAllText(settingsPath);
            var volumeDimensions = FormattableString.Invariant(
                $"{VisionOSVolumeCameraDimensions.x:0.000}, {VisionOSVolumeCameraDimensions.y:0.000}, {VisionOSVolumeCameraDimensions.z:0.000}");
            var patched = source
                .Replace("let unityStartInBatchMode = true", "let unityStartInBatchMode = false")
                .Replace(" .persistentSystemOverlays(.automatic)", string.Empty)
                .Replace(" .persistentSystemOverlays(.visible)", string.Empty)
                .Replace(" .persistentSystemOverlays(.hidden)", string.Empty)
                .Replace(".init(1.000, 1.000, 1.000)", $".init({volumeDimensions})");
            if (string.Equals(source, patched, StringComparison.Ordinal))
                return;

            File.WriteAllText(settingsPath, patched);
            Debug.Log($"Patched visionOS PolySpatial Swift settings: {settingsPath}");
        }

        private static void PatchVisionOSNativeLibrary(string fullBuildPath, bool useSimulatorSdk)
        {
            var librariesPath = Path.Combine(fullBuildPath, "Libraries");
            var destinationPath = Path.Combine(librariesPath, "libUnityVisionOS.a");

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageCachePath = Path.Combine(projectRoot, "Library/PackageCache");
            if (!Directory.Exists(packageCachePath))
            {
                Debug.LogWarning($"Unity package cache missing: {packageCachePath}");
                return;
            }

            var sdkFolder = useSimulatorSdk ? "Simulator" : "Device";
            var sourcePath = Directory
                .GetDirectories(packageCachePath, "com.unity.xr.visionos@*", SearchOption.TopDirectoryOnly)
                .Select(packagePath => Path.Combine(
                    packagePath,
                    $"Runtime/Plugins/visionos/{sdkFolder}/arm64/libUnityVisionOS.a"))
                .FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning($"Unity visionOS native library missing for {sdkFolder} SDK.");
                return;
            }

            Directory.CreateDirectory(librariesPath);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            Debug.Log($"Copied visionOS {sdkFolder} native library: {destinationPath}");
        }

        private static void PatchVisionOSXcodeProject(string fullBuildPath)
        {
            var pbxProjectPath = Directory
                .GetDirectories(fullBuildPath, "*.xcodeproj", SearchOption.TopDirectoryOnly)
                .Select(projectPath => Path.Combine(projectPath, "project.pbxproj"))
                .FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(pbxProjectPath))
            {
                Debug.LogWarning($"visionOS Xcode project missing under: {fullBuildPath}");
                return;
            }

            var source = File.ReadAllText(pbxProjectPath);
            var patched = source.Replace("XROS_DEPLOYMENT_TARGET = 1.0;", "XROS_DEPLOYMENT_TARGET = 2.0;");
            if (!patched.Contains("libUnityVisionOS.a"))
            {
                const string il2CppLibraryFlag = "\"\\\"$CONFIGURATION_BUILD_DIR/il2cpp.a\\\"\",";
                const string visionOSLibraryFlag = "\"\\\"$(PROJECT_DIR)/Libraries/libUnityVisionOS.a\\\"\",";
                patched = patched.Replace(
                    il2CppLibraryFlag,
                    il2CppLibraryFlag + "\n\t\t\t\t\t" + visionOSLibraryFlag);
            }

            if (!string.Equals(source, patched, StringComparison.Ordinal))
            {
                File.WriteAllText(pbxProjectPath, patched);
                Debug.Log($"Patched visionOS Xcode project settings: {pbxProjectPath}");
            }
        }

        private static void ReportBuildTarget(string groupName, string targetName)
        {
            if (!TryParseBuildTargetGroup(groupName, out var group) || !TryParseBuildTarget(targetName, out var target))
            {
                Debug.LogWarning($"Build target enum missing: group={groupName}, target={targetName}");
                return;
            }

            Debug.Log($"Build target support {groupName}/{targetName}: {BuildPipeline.IsBuildTargetSupported(group, target)}");
        }

        private static void ReportXRLoaders(string groupName)
        {
            if (!TryParseBuildTargetGroup(groupName, out var group))
                return;

            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget settings) || settings == null)
            {
                Debug.LogWarning($"XRGeneralSettingsPerBuildTarget missing while reporting {groupName}.");
                return;
            }

            if (!settings.HasManagerSettingsForBuildTarget(group))
            {
                Debug.LogWarning($"XR manager settings missing for {groupName}.");
                return;
            }

            var manager = settings.ManagerSettingsForBuildTarget(group);
            var loaders = manager?.activeLoaders == null
                ? "(none)"
                : string.Join(", ", manager.activeLoaders.Where(loader => loader != null).Select(loader => loader.GetType().FullName));
            Debug.Log($"XR loaders for {groupName}: {loaders}");
        }

        private static void ReportMRTKProfile(string groupName)
        {
            if (!TryParseBuildTargetGroup(groupName, out var group))
                return;

            var settings = AssetDatabase.LoadAssetAtPath<MRTKSettings>(MRTKSettingsPath);
            var profile = settings == null ? null : settings.GetProfileForBuildTarget(group);
            Debug.Log($"MRTK3 profile for {groupName}: {(profile == null ? "(none)" : profile.name)}");
        }

        private static BuildTargetGroup ParseBuildTargetGroup(string value)
        {
            if (!TryParseBuildTargetGroup(value, out var result))
                throw new InvalidOperationException($"BuildTargetGroup is not available in this Unity editor: {value}");
            return result;
        }

        private static BuildTarget ParseBuildTarget(string value)
        {
            if (!TryParseBuildTarget(value, out var result))
                throw new InvalidOperationException($"BuildTarget is not available in this Unity editor: {value}");
            return result;
        }

        private static bool TryParseBuildTargetGroup(string value, out BuildTargetGroup result)
        {
            return Enum.TryParse(value, out result);
        }

        private static bool TryParseBuildTarget(string value, out BuildTarget result)
        {
            return Enum.TryParse(value, out result);
        }

        private static Type FindType(string fullName)
        {
            var type = Type.GetType(fullName);
            if (type != null)
                return type;

            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(fullName))
                .FirstOrDefault(candidate => candidate != null);
        }

        private static Type FindTypeByName(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in GetTypesSafely(assembly))
                {
                    if (type.Name == name)
                        return type;
                }
            }

            return null;
        }

        private static Type[] GetTypesSafely(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null).ToArray();
            }
        }

        private static MethodInfo[] FindStaticMethodsByName(string name)
        {
            var methods = new System.Collections.Generic.List<MethodInfo>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in GetTypesSafely(assembly))
                {
                    methods.AddRange(type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        .Where(method => method.Name == name));
                }
            }

            return methods.ToArray();
        }

        private static string DescribeMethod(MethodInfo method)
        {
            var parameters = method.GetParameters()
                .Select(parameter => $"{parameter.ParameterType.FullName} {parameter.Name}");
            return $"{method.DeclaringType?.FullName}.{method.Name}({string.Join(", ", parameters)})";
        }

        private static bool ManifestContains(string value)
        {
            var manifestPath = "Packages/manifest.json";
            return File.Exists(manifestPath) && File.ReadAllText(manifestPath).Contains(value, StringComparison.Ordinal);
        }

        private static bool IsSceneObject(UnityEngine.Object obj)
        {
            if (obj == null || EditorUtility.IsPersistent(obj))
                return false;

            return obj switch
            {
                Component component => component.gameObject.scene.IsValid(),
                GameObject gameObject => gameObject.scene.IsValid(),
                _ => false
            };
        }

        private static string DescribeAssetByGuid(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return $"{guid} -> (unresolved)";

            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            return $"{guid} -> {path} ({(asset == null ? "not loaded" : asset.GetType().FullName)})";
        }

        private static string DescribeSerializedFields(UnityEngine.Object obj, params string[] fieldNames)
        {
            var serializedObject = new SerializedObject(obj);
            return string.Join(" ", fieldNames.Select(fieldName =>
            {
                var property = serializedObject.FindProperty(fieldName);
                return property == null ? $"{fieldName}=(missing)" : $"{fieldName}={FormatSerializedProperty(property)}";
            }));
        }

        private static string FormatSerializedProperty(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => property.boolValue.ToString(),
                SerializedPropertyType.Integer => property.intValue.ToString(),
                SerializedPropertyType.Float => property.floatValue.ToString("0.###"),
                SerializedPropertyType.Enum => property.enumDisplayNames.ElementAtOrDefault(property.enumValueIndex) ?? property.enumValueIndex.ToString(),
                SerializedPropertyType.Vector3 => FormatVector(property.vector3Value),
                SerializedPropertyType.ObjectReference => property.objectReferenceValue == null ? "(null)" : $"{property.objectReferenceValue.name}:{property.objectReferenceValue.GetType().Name}",
                SerializedPropertyType.LayerMask => $"0x{property.intValue:X8}",
                _ => property.ToString()
            };
        }

        private static Bounds CalculateBounds(IEnumerable<Renderer> renderers)
        {
            var rendererArray = renderers.ToArray();
            if (rendererArray.Length == 0)
                return new Bounds(Vector3.zero, Vector3.zero);

            var bounds = rendererArray[0].bounds;
            foreach (var renderer in rendererArray.Skip(1))
            {
                bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }

        private static string FormatBounds(Bounds bounds)
        {
            return $"center={FormatVector(bounds.center)} size={FormatVector(bounds.size)} min={FormatVector(bounds.min)} max={FormatVector(bounds.max)}";
        }

        private static string FormatVector(Vector3 vector)
        {
            return $"({vector.x:0.###}, {vector.y:0.###}, {vector.z:0.###})";
        }

        private static string FormatColor(Color color)
        {
            return $"({color.r:0.###}, {color.g:0.###}, {color.b:0.###}, {color.a:0.###})";
        }

        private static string GetPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = $"{transform.name}/{path}";
            }

            return path;
        }

        private static Transform FindSceneTransform(Scene scene, string objectName)
        {
            if (!scene.IsValid())
                return null;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive: true))
                {
                    if (transform.name == objectName)
                        return transform;
                }
            }

            return null;
        }

        private static string GetMaterialName(Renderer renderer)
        {
            var material = renderer.sharedMaterial;
            return material == null ? "(none)" : material.name;
        }

        private static string DescribeMaterials(Renderer renderer)
        {
            return string.Join("; ", renderer.sharedMaterials.Select(DescribeMaterial));
        }

        private static string DescribeMaterial(Material material)
        {
            if (material == null)
                return "(none)";

            var shader = material.shader;
            return $"{material.name}|shader={shader?.name ?? "(none)"}|supported={(shader != null && shader.isSupported)}|queue={material.renderQueue}|instancing={material.enableInstancing}{DescribeMaterialColor(material)}{DescribeMaterialFloat(material, "_Smoothness")}{DescribeMaterialFloat(material, "_Glossiness")}";
        }

        private static string DescribeMaterialColor(Material material)
        {
            if (material.HasProperty("_BaseColor"))
                return $"|baseColor={FormatColor(material.GetColor("_BaseColor"))}";

            if (material.HasProperty("_Color"))
                return $"|color={FormatColor(material.GetColor("_Color"))}";

            return string.Empty;
        }

        private static string DescribeMaterialFloat(Material material, string propertyName)
        {
            return material.HasProperty(propertyName)
                ? $"|{propertyName}={material.GetFloat(propertyName):0.###}"
                : string.Empty;
        }

        private static string DescribeMaterialPropertyBlock(MaterialPropertyBlock block)
        {
            if (block == null || block.isEmpty)
                return "empty";

            return string.Join(" ", new[]
            {
                DescribeBlockColor(block, "_Color"),
                DescribeBlockColor(block, "_Color_"),
                DescribeBlockColor(block, "_Base_Color_"),
                DescribeBlockColor(block, "_Inner_Color_"),
                DescribeBlockColor(block, "_Color_At_0_"),
                DescribeBlockColor(block, "_Color_At_1_"),
                DescribeBlockColor(block, "_Top_Left_"),
                DescribeBlockColor(block, "_Top_Right_"),
                DescribeBlockColor(block, "_Bottom_Left_"),
                DescribeBlockColor(block, "_Bottom_Right_"),
                DescribeBlockColor(block, "_Edge_Color_"),
                DescribeBlockFloat(block, "_Gaze_Focus_"),
                DescribeBlockFloat(block, "_Show_Internal_"),
                DescribeBlockFloat(block, "_Show_Internal_Back_"),
                DescribeBlockFloat(block, "_Show_Internal_Front_"),
                DescribeBlockFloat(block, "_Always_Show_Internal_"),
                DescribeBlockFloat(block, "_Selection_Fade_"),
                DescribeBlockFloat(block, "_Blob_Enable_"),
                DescribeBlockFloat(block, "_Blob_Enable_2_")
            });
        }

        private static string DescribeBlockColor(MaterialPropertyBlock block, string propertyName)
        {
            return $"{propertyName}={FormatColor(block.GetColor(Shader.PropertyToID(propertyName)))}";
        }

        private static string DescribeBlockFloat(MaterialPropertyBlock block, string propertyName)
        {
            return $"{propertyName}={block.GetFloat(Shader.PropertyToID(propertyName)):0.###}";
        }

        private static void EnsureSceneExists()
        {
            if (!File.Exists(ScenePath))
                throw new FileNotFoundException("Parity scene not found.", ScenePath);
        }

        private static void EnsureAssetFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
                return;

            var parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            var name = Path.GetFileName(assetFolder);
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static string GetArgument(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }

            return fallback;
        }
    }
}
