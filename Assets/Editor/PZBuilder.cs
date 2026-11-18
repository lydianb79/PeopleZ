using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// One-shot project setup + macOS (Metal) player build. Menu: PeopleZ/Build, or
/// Unity -batchmode -quit -projectPath . -executeMethod PZBuilder.BuildAll
public static class PZBuilder
{
    const string Settings = "Assets/Settings", ScenePath = "Assets/Main.unity", UrpPath = Settings + "/PZ_URP.asset", RendererPath = Settings + "/PZ_Renderer.asset";

    [MenuItem("PeopleZ/Setup Project")]
    public static void Setup()
    {
        Directory.CreateDirectory(Settings);
        AssetDatabase.Refresh();

        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
        if (urp == null)
        {
            var create = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset", BindingFlags.NonPublic | BindingFlags.Static);
            var pars = create.GetParameters();
            var rd = (ScriptableRendererData)create.Invoke(null, new object[] { RendererPath, Enum.Parse(pars[1].ParameterType, "UniversalRenderer"), false, "Renderer" });

            // screen-space ambient occlusion
            var ssaoType = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            var ssao = (ScriptableRendererFeature)ScriptableObject.CreateInstance(ssaoType);
            ssao.name = "SSAO";
            AssetDatabase.AddObjectToAsset(ssao, rd);
            rd.rendererFeatures.Add(ssao);
            var sso = new SerializedObject(ssao);
            SetProp(sso, "m_Settings.Source", 0);            // depth (no depth-normals pass needed)
            SetProp(sso, "m_Settings.Intensity", 1.6f);
            SetProp(sso, "m_Settings.Radius", 0.5f);
            SetProp(sso, "m_Settings.DirectLightingStrength", 0.3f);
            SetProp(sso, "m_Settings.Falloff", 250f);
            sso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rd);
            AssetDatabase.SaveAssets();
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(ssao, out string _, out long localId))
            {
                var rso = new SerializedObject(rd);
                var map = rso.FindProperty("m_RendererFeatureMap");
                map.arraySize = 1;
                map.GetArrayElementAtIndex(0).longValue = localId;
                rso.ApplyModifiedPropertiesWithoutUndo();
            }

            urp = UniversalRenderPipelineAsset.Create(rd);
            urp.supportsCameraDepthTexture = true;
            urp.supportsCameraOpaqueTexture = true;
            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 90f;
            urp.shadowCascadeCount = 4;
            AssetDatabase.CreateAsset(urp, UrpPath);
            var uso = new SerializedObject(urp);
            SetProp(uso, "m_MainLightShadowmapResolution", 4096);
            SetProp(uso, "m_SoftShadowsSupported", true);
            SetProp(uso, "m_SoftShadowQuality", 3);
            SetProp(uso, "m_OpaqueDownsampling", 0);
            uso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
        }

        GraphicsSettings.defaultRenderPipeline = urp;
        int ql = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(ql, false);

        var gs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        SetProp(gs, "m_InstancingStripping", 2);   // keep all: instanced shaders are only referenced from code
        gs.ApplyModifiedPropertiesWithoutUndo();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("PeopleZ").AddComponent<Game>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        PlayerSettings.productName = "PeopleZ";
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.macOS.buildNumber = "1";
        PlayerSettings.companyName = "PeopleZ";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.peoplez.game");
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, new[] { UnityEngine.Rendering.GraphicsDeviceType.Metal });
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.defaultScreenWidth = 1600;
        PlayerSettings.defaultScreenHeight = 1000;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.macRetinaSupport = true;
        const string iconAsset = "Assets/Icon.png";
        if (File.Exists(iconAsset))
        {
            AssetDatabase.ImportAsset(iconAsset, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(iconAsset);
            if (ti.npotScale != TextureImporterNPOTScale.None || ti.textureCompression != TextureImporterCompression.Uncompressed || !ti.alphaIsTransparency || ti.mipmapEnabled)
            {
                ti.npotScale = TextureImporterNPOTScale.None; ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.maxTextureSize = 1024;
                ti.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconAsset);
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);   // default icon for all targets
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[PZ] setup complete");
    }

    static void Sh(string exe, string args)
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
        p.WaitForExit();
        if (p.ExitCode != 0) Debug.LogWarning("[PZ] " + exe + " " + args + " failed: " + p.StandardError.ReadToEnd());
    }

    static void SetProp(SerializedObject so, string path, object value)
    {
        var p = so.FindProperty(path);
        if (p == null) { Debug.LogWarning("[PZ] missing property " + path); return; }
        if (value is bool b) p.boolValue = b;
        else if (value is int i) p.intValue = i;
        else if (value is float f) p.floatValue = f;
    }

    [MenuItem("PeopleZ/Build macOS App")]
    public static void BuildAll()
    {
        Setup();
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Build/PeopleZ.app",
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        // Fallback: if Unity did not emit an icon, build the .icns ourselves and re-sign the bundle.
        string icns = "Build/PeopleZ.app/Contents/Resources/PlayerIcon.icns";
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded && File.Exists("Assets/Icon.png") && !File.Exists(icns))
        {
            string set = "Temp/PeopleZ.iconset";
            Directory.CreateDirectory(set);
            foreach (int sz in new[] { 16, 32, 128, 256, 512 })
            {
                Sh("sips", "-z " + sz + " " + sz + " Assets/Icon.png --out " + set + "/icon_" + sz + "x" + sz + ".png");
                Sh("sips", "-z " + sz * 2 + " " + sz * 2 + " Assets/Icon.png --out " + set + "/icon_" + sz + "x" + sz + "@2x.png");
            }
            Sh("iconutil", "-c icns " + set + " -o " + icns);
            Sh("/usr/libexec/PlistBuddy", "-c \"Set :CFBundleIconFile PlayerIcon.icns\" Build/PeopleZ.app/Contents/Info.plist");
            Sh("codesign", "--force --deep -s - Build/PeopleZ.app");
            Debug.Log("[PZ] icon installed via iconutil: " + File.Exists(icns));
        }
        Debug.Log("[PZ] build result: " + report.summary.result + " errors=" + report.summary.totalErrors + " size=" + report.summary.totalSize);
        if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
