using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Meta.XR.MRUtilityKit;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;

namespace Wonderland.Editor
{
    /// <summary>
    /// Reproducible project setup and APK build (adapted from the EasyPiano pipeline).
    ///   scripts/unity.sh configure   regenerates Assets/Scenes/Wonderland.unity and the Quest settings
    ///   scripts/unity.sh build       configure + ARM64 IL2CPP APK -> Builds/Android/Wonderland.apk
    /// </summary>
    public static class WonderlandBuild
    {
        public const string ScenePath = "Assets/Scenes/Wonderland.unity";
        private const string ProfilePath = "Assets/Settings/Build Profiles/Meta Quest.asset";
        public const string ApkPath = "Builds/Android/Wonderland.apk";

        [MenuItem("Wonderland/Configure scene and Quest settings")]
        public static void Configure()
        {
            var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
            if (profile == null) throw new BuildFailedException("Meta Quest build profile is missing.");
            BuildProfile.SetActiveBuildProfile(profile);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.splink.wonderland");
            PlayerSettings.productName = "Wonderland";
            PlayerSettings.companyName = "Splink";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.enableFrameTimingStats = true;
            EditorUserBuildSettings.buildAppBundle = false;

            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (xr == null || xr.Manager.activeLoaders.Count != 1 || xr.Manager.activeLoaders[0].GetType().Name != "OpenXRLoader")
                throw new BuildFailedException("Android must have exactly one OpenXR loader.");
            xr.InitManagerOnStart = true;
            EditorUtility.SetDirty(xr);
            var openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (!openXR.GetFeatures().Any(f => f.GetType().Name == "MetaXRFeature" && f.enabled))
                throw new BuildFailedException("Meta XR OpenXR feature must be enabled.");
            foreach (var feature in openXR.GetFeatures())
                if (feature.GetType().Name.Contains("SpaceWarp") || feature.GetType().Name.Contains("Subsampled")) feature.enabled = false;
            EditorUtility.SetDirty(openXR);

            var materials = new WorldMaterials
            {
                Lit = WonderlandMaterials.Lit, Terrain = WonderlandMaterials.Terrain, UI = WonderlandMaterials.UI, Smoke = WonderlandMaterials.Smoke,
                Water = WonderlandMaterials.Water,
            };
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab");
            if (prefab == null) throw new BuildFailedException("Meta XR OVRCameraRig prefab not found.");
            var rigObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var rig = rigObject.GetComponent<OVRCameraRig>();
            var manager = rigObject.GetComponent<OVRManager>();
            var managerSettings = new SerializedObject(manager);
            managerSettings.FindProperty("isInsightPassthroughEnabled").boolValue = true;
            managerSettings.FindProperty("shouldBoundaryVisibilityBeSuppressed").boolValue = true;
            managerSettings.FindProperty("_trackingOriginType").intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
            managerSettings.ApplyModifiedProperties();
            foreach (var camera in rigObject.GetComponentsInChildren<Camera>(true))
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;   // alpha 0: passthrough shows wherever nothing is drawn
                camera.nearClipPlane = 0.03f;
                camera.farClipPlane = 30f;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            }
            new GameObject("Passthrough").AddComponent<OVRPassthroughLayer>();

            var mrukObject = new GameObject("MR Utility Kit");
            var mruk = mrukObject.AddComponent<MRUK>();
            var mrukSettings = new SerializedObject(mruk);
            mrukSettings.FindProperty("SceneSettings.DataSource").intValue = (int)MRUK.SceneDataSource.Device;
            mrukSettings.FindProperty("SceneSettings.LoadSceneOnStartup").boolValue = false; // WonderlandApp loads after the permission prompt
            mrukSettings.ApplyModifiedProperties();

            WonderlandLighting.CreateKeyLight();
            var app = new GameObject("Wonderland App").AddComponent<WonderlandApp>();
            app.Materials = materials;
            app.Rig = rig;
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            profile.overrideGlobalScenes = false;
            profile.scenes = EditorBuildSettings.scenes;
            EditorUtility.SetDirty(profile);

            var config = OVRProjectConfig.CachedProjectConfig;
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersOnly;
            config.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;   // MRUK floor + free space; app works without it
            config.anchorSupport = OVRProjectConfig.AnchorSupport.Disabled;
            config.boundaryVisibilitySupport = OVRProjectConfig.FeatureSupport.Supported;
            var configSettings = new SerializedObject(config);
            configSettings.FindProperty("_insightPassthroughSupport").intValue = (int)OVRProjectConfig.FeatureSupport.Required;
            configSettings.FindProperty("_systemLoadingScreenBackground").intValue = (int)OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
            var targets = configSettings.FindProperty("targetDeviceTypes");
            targets.arraySize = 1;
            targets.GetArrayElementAtIndex(0).intValue = (int)OVRProjectConfig.DeviceType.Quest3;
            configSettings.ApplyModifiedProperties();
            OVRProjectConfig.CommitProjectConfig(config);
            FixRequiredSetupTasks();
            EditorSceneManager.SaveScene(scene);
            OVRManifestPreprocessor.GenerateOrUpdateAndroidManifest(true);
            AssetDatabase.SaveAssets();
            Validate();
        }

        private static void FixRequiredSetupTasks()
        {
            // SDK task types are internal: reflect only to read and invoke their registered public delegates.
            var getTasks = typeof(OVRProjectSetup).GetMethod("GetTasks", BindingFlags.NonPublic | BindingFlags.Static);
            for (int pass = 0; pass < 3; pass++)
            foreach (var task in (IEnumerable)getTasks.Invoke(null, new object[] { BuildTargetGroup.Android }))
            {
                var type = task.GetType();
                var isDone = (Func<BuildTargetGroup, bool>)type.GetProperty("IsDone").GetValue(task);
                if (isDone(BuildTargetGroup.Android)) continue;
                string Read(string name)
                {
                    var value = type.GetProperty(name).GetValue(task);
                    return value?.GetType().GetMethod("GetValue").Invoke(value, new object[] { BuildTargetGroup.Android })?.ToString();
                }
                var level = Read("Level"); var message = Read("Message") ?? "";
                Debug.Log($"Wonderland setup [{level}]: {message}");
                if (level == "Required")
                {
                    var fix = type.GetProperty("FixAction").GetValue(task) as Action<BuildTargetGroup>;
                    fix?.Invoke(BuildTargetGroup.Android);
                }
            }
        }

        [MenuItem("Wonderland/Validate configuration")]
        public static void Validate()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>();
            if (rig == null) throw new BuildFailedException("OVRCameraRig missing.");
            var manager = rig.GetComponent<OVRManager>();
            if (!manager.isInsightPassthroughEnabled || rig.centerEyeAnchor.GetComponent<Camera>().backgroundColor.a != 0f)
                throw new BuildFailedException("Passthrough must be enabled with a transparent camera background.");
            if (UnityEngine.Object.FindObjectsByType<OVRPassthroughLayer>().Length != 1) throw new BuildFailedException("Exactly one passthrough layer expected.");
            var app = UnityEngine.Object.FindAnyObjectByType<WonderlandApp>();
            if (app == null || app.Materials?.Lit == null || app.Materials.Terrain == null || app.Materials.UI == null || app.Materials.Smoke == null || app.Materials.Water == null)
                throw new BuildFailedException("WonderlandApp with all materials expected.");
            if (UnityEngine.Object.FindAnyObjectByType<MRUK>() == null) throw new BuildFailedException("MRUK missing.");
            string manifest = File.ReadAllText("Assets/Plugins/Android/AndroidManifest.xml");
            if (!manifest.Contains("com.oculus.feature.PASSTHROUGH")) throw new BuildFailedException("Manifest lacks passthrough.");
            if (!manifest.Contains("com.oculus.permission.USE_SCENE")) throw new BuildFailedException("Manifest lacks USE_SCENE.");
            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != "com.splink.wonderland") throw new BuildFailedException("Wrong application id.");
            Debug.Log("Wonderland: configuration validated.");
        }

        public static void Build()
        {
            Configure();
            bool development = Environment.GetEnvironmentVariable("WONDERLAND_DEVELOPMENT") == "1";
            var path = Environment.GetEnvironmentVariable("WONDERLAND_APK") ?? ApkPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            EditorUserBuildSettings.development = development;
            string profile = File.ReadAllText(ProfilePath);
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = path, target = BuildTarget.Android,
                    options = development ? BuildOptions.Development : BuildOptions.None,
                });
            }
            finally
            {
                // Build processors add preloaded assets to the profile; a build must not change the project.
                if (File.ReadAllText(ProfilePath) != profile) { File.WriteAllText(ProfilePath, profile); AssetDatabase.ImportAsset(ProfilePath, ImportAssetOptions.ForceUpdate); }
            }
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"APK build failed: {report.summary.result} ({report.summary.totalErrors} errors).");
            Debug.Log($"Wonderland: APK built at {Path.GetFullPath(path)} ({report.summary.totalSize} bytes, {(development ? "development" : "non-development")}).");
        }
    }
}
