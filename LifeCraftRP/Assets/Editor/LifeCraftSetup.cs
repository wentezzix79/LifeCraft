using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LifeCraftRP.Editor
{
    [InitializeOnLoad]
    public static class LifeCraftSetup
    {
        private const string ScenePath = "Assets/Scenes/OmskStarter.unity";
        private const string PipelinePath = "Assets/Settings/LifeCraftURP.asset";
        private const string RendererPath = "Assets/Settings/LifeCraftRenderer.asset";

        static LifeCraftSetup()
        {
            EditorApplication.delayCall += Prepare;
        }

        [MenuItem("LIFE CRAFT/Настроить проект")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var scopedScenes = new [] { new EditorBuildSettingsScene(ScenePath,true) };
            if (EditorBuildSettings.scenes.Length == 0)
                EditorBuildSettings.scenes = scopedScenes;

            PlayerSettings.productName = "LIFE CRAFT RP 2.0";
            PlayerSettings.companyName = "LifeCraft";
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            ConfigureLinearColorSpace();
            EnsurePipeline();
            EnsureStreetColorGrade();
            KeepRuntimeShaders();
            ActorMotionAssets.Ensure();
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureLinearColorSpace()
        {
            // Android linear rendering requires Vulkan or OpenGL ES 3.0 or newer.
            // Preserve the Editor's API selection except when a GLES2 override would block Linear.
            if (!PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android))
            {
                GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
                GraphicsDeviceType[] compatible = Array.FindAll(apis,x => x != GraphicsDeviceType.OpenGLES2);
                if (compatible.Length == 0)
                    PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,true);
                else if (compatible.Length != apis.Length)
                    PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,compatible);
            }
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                PlayerSettings.colorSpace = ColorSpace.Linear;
        }

        private static void EnsureStreetColorGrade()
        {
            string path = "Assets/Resources/"+StreetColorGrade.ResourcePath+".asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null)
                return;

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets","Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Art"))
                AssetDatabase.CreateFolder("Assets/Resources","Art");
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            StreetColorGrade.Configure(profile);
            AssetDatabase.CreateAsset(profile,path);
            foreach (VolumeComponent component in profile.components)
                AssetDatabase.AddObjectToAsset(component,profile);
            EditorUtility.SetDirty(profile);
        }

        private static void EnsurePipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            try
            {
                // Git archives omit the empty Settings directory.
                if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                    AssetDatabase.CreateFolder("Assets", "Settings");

                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, RendererPath);
                }

                bool rendererChanges = ResourceReloader.ReloadAllNullIn(
                    renderer,UniversalRenderPipelineAsset.packagePath);
                if (renderer.postProcessData == null)
                {
                    renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                        UniversalRenderPipelineAsset.packagePath+"/Runtime/Data/PostProcessData.asset");
                    rendererChanges |= renderer.postProcessData != null;
                }
                if (renderer.shaders == null || renderer.shaders.blitPS == null)
                    throw new InvalidOperationException("URP renderer shader resources are unavailable.");
                if (rendererChanges)
                    EditorUtility.SetDirty(renderer);

                if (pipeline == null)
                {
                    pipeline = UniversalRenderPipelineAsset.Create(renderer);
                    AssetDatabase.CreateAsset(pipeline, PipelinePath);
                }
                else
                {
                    // Repair assets generated by an earlier import.
                    var settings = new SerializedObject(pipeline);
                    var renderers = settings.FindProperty("m_RendererDataList");
                    var defaultRenderer = settings.FindProperty("m_DefaultRendererIndex");
                    if (renderers.arraySize == 0)
                        renderers.arraySize = 1;
                    int index = Mathf.Clamp(defaultRenderer.intValue, 0, renderers.arraySize - 1);
                    renderers.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
                    defaultRenderer.intValue = index;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    ResourceReloader.ReloadAllNullIn(pipeline, UniversalRenderPipelineAsset.packagePath);
                }

                var quality = new SerializedObject(pipeline);
                quality.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
                quality.FindProperty("m_SoftShadowsSupported").boolValue = true;
                quality.FindProperty("m_Cascade2Split").floatValue = .28f;
                quality.ApplyModifiedPropertiesWithoutUndo();

                if (GraphicsSettings.defaultRenderPipeline != pipeline)
                    GraphicsSettings.defaultRenderPipeline = pipeline;
                if (QualitySettings.renderPipeline != pipeline)
                    QualitySettings.renderPipeline = pipeline;
            }
            catch (Exception error)
            {
                if (pipeline != null)
                {
                    if (GraphicsSettings.defaultRenderPipeline == pipeline)
                        GraphicsSettings.defaultRenderPipeline = null;
                    if (QualitySettings.renderPipeline == pipeline)
                        QualitySettings.renderPipeline = null;
                }

                Debug.LogWarning("LIFE CRAFT: URP setup failed. " + error.Message);
            }
        }

        private static void KeepRuntimeShaders()
        {
            var assets=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            var settings=Array.Find(assets,asset=>asset != null && asset.GetType().Name=="GraphicsSettings");
            var serialized=settings!=null ? new SerializedObject(settings) : null;
            SerializedProperty list=serialized?.FindProperty("m_AlwaysIncludedShaders");
            const string materialFolder="Assets/Resources/Art/ShaderReferences";
            if(!AssetDatabase.IsValidFolder(materialFolder))
                AssetDatabase.CreateFolder("Assets/Resources/Art","ShaderReferences");
            foreach(string name in new [] {
                "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit",
                "Standard", "Diffuse", "Unlit/Color", "Particles/Standard Unlit", "Unlit/Transparent",
                "Skybox/Procedural"
            })
            {
                Shader shader=Shader.Find(name);
                if(shader==null)
                    continue;
                string materialPath=materialFolder+"/"+name.Replace('/','-')+".mat";
                if(AssetDatabase.LoadAssetAtPath<Material>(materialPath)==null)
                    AssetDatabase.CreateAsset(new Material(shader),materialPath);
                if(list==null)
                    continue;
                bool found=false;
                for(int i=0;i<list.arraySize;i++)
                    found |= list.GetArrayElementAtIndex(i).objectReferenceValue==shader;
                if(!found)
                    list.GetArrayElementAtIndex(list.arraySize++).objectReferenceValue=shader;
            }
            serialized?.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("LIFE CRAFT/Собрать Windows x64")]
        public static void BuildWindows()
        {
            Prepare();
            Directory.CreateDirectory("Builds/Windows");
            Build("Builds/Windows/LifeCraftRP.exe",BuildTarget.StandaloneWindows64);
        }

        [MenuItem("LIFE CRAFT/Собрать Android APK")]
        public static void BuildAndroid()
        {
            Prepare();
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,"com.lifecraft.rp");
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android,ScriptingImplementation.IL2CPP);
            EditorUserBuildSettings.buildAppBundle=false;
            Directory.CreateDirectory("Builds/Android");
            Build("Builds/Android/LifeCraftRP.apk",BuildTarget.Android);
        }

        private static void Build(string path,BuildTarget target)
        {
            BuildReport report=BuildPipeline.BuildPlayer(new [] { ScenePath },path,target,BuildOptions.None);
            if(report.summary.result!=BuildResult.Succeeded)
                throw new InvalidOperationException($"Сборка {target}: {report.summary.result}.");
            Debug.Log($"LIFE CRAFT готов: {path}");
        }
    }
}
