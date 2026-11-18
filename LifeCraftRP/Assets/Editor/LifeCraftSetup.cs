using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

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
            EnsurePipeline();
            KeepRuntimeShaders();
            ActorMotionAssets.Ensure();
            AssetDatabase.SaveAssets();
        }

        private static void EnsurePipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                Type pipelineType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
                Type rendererType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime");
                if (pipelineType == null || rendererType == null)
                {
                    Debug.LogError("Пакет URP 14 ещё не загружен. Проверьте Unity Package Manager.");
                    return;
                }

                var renderer = AssetDatabase.LoadAssetAtPath<ScriptableObject>(RendererPath);
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance(rendererType);
                    AssetDatabase.CreateAsset(renderer,RendererPath);
                }
                CallResourceReloader(renderer);

                foreach (MethodInfo method in pipelineType.GetMethods(BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.Static))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (method.Name == "Create" && parameters.Length == 1 &&
                        parameters[0].ParameterType.IsInstanceOfType(renderer))
                    {
                        pipeline = method.Invoke(null,new object[] { renderer }) as RenderPipelineAsset;
                        if (pipeline != null)
                            break;
                    }
                }
                if (pipeline == null)
                {
                    pipeline = (RenderPipelineAsset)ScriptableObject.CreateInstance(pipelineType);
                    var data = new SerializedObject(pipeline);
                    SerializedProperty list = data.FindProperty("m_RendererDataList");
                    if (list == null)
                        throw new InvalidOperationException("Не удалось настроить рендерер URP 14.");
                    list.arraySize = 1;
                    list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                CallResourceReloader(pipeline);
                AssetDatabase.CreateAsset(pipeline,PipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline=pipeline;
            QualitySettings.renderPipeline=pipeline;
        }

        private static void CallResourceReloader(ScriptableObject asset)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type utility = assembly.GetType("UnityEditor.Rendering.ResourceReloader");
                if (utility == null)
                    continue;
                MethodInfo reload=utility.GetMethod("TryReloadAllNullIn",BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.Static,null,new [] { typeof(object),typeof(string) },null);
                reload?.Invoke(null,new object[] { asset,"Packages/com.unity.render-pipelines.universal" });
                EditorUtility.SetDirty(asset);
                break;
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
            foreach(string name in new [] {"Universal Render Pipeline/Lit","Universal Render Pipeline/Particles/Unlit","Standard","Unlit/Color"})
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
