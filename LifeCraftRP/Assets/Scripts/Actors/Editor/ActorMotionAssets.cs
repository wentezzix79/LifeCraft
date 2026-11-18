#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LifeCraftRP
{
    /// <summary>Creates the Animator graph and authored generic clips for the in-project rig.</summary>
    internal static class ActorMotionAssets
    {
        private const string ActorFolder = "Assets/Scripts/Actors/Resources/LifeCraftActors";
        private const string ControllerPath = ActorFolder + "/Avatar.controller";
        private const string RelaxedIdle = "Relaxed Idle";

        [InitializeOnLoadMethod]
        private static void ScheduleBuild() => EditorApplication.delayCall += Ensure;

        [MenuItem("Life Craft/Подготовить анимации персонажа")]
        internal static void Ensure()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (existing)
            {
                var stateMachine = existing.layers[0].stateMachine;
                if (Array.Exists(stateMachine.states, row => row.state.name == "Idle") &&
                    !Array.Exists(stateMachine.states, row => row.state.name == RelaxedIdle))
                {
                    Add(stateMachine,existing,RelaxedIdle,4.8f,true,0f);
                    AssetDatabase.SaveAssets();
                }
                return;
            }
            MakeFolder("Assets/Scripts/Actors","Resources");
            MakeFolder("Assets/Scripts/Actors/Resources","LifeCraftActors");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            try
            {
                var machine = controller.layers[0].stateMachine;
                Add(machine,controller,"Idle",1.2f,true,0f);
                Add(machine,controller,RelaxedIdle,4.8f,true,0f);
                Add(machine,controller,"Walk",.80f,true,25f);
                Add(machine,controller,"Run",.66f,true,38f);
                Add(machine,controller,"Sprint",.51f,true,52f);
                Add(machine,controller,"Jump",.36f,false,0f);
                Add(machine,controller,"Fall",.42f,true,0f);
                Add(machine,controller,"Land",.25f,false,0f);
                Add(machine,controller,"Crouch",.84f,true,13f);
                Add(machine,controller,"Punch",.43f,false,0f);
                Add(machine,controller,"Pick Up",.74f,false,0f);
                Add(machine,controller,"Carry",.80f,true,18f);
                Add(machine,controller,"Sit",.60f,false,0f);
                Add(machine,controller,"Drive",1f,true,0f);
                Add(machine,controller,"Enter Vehicle",.57f,false,0f);
                Add(machine,controller,"Exit Vehicle",.53f,false,0f);
                Add(machine,controller,"Open Door",.54f,false,0f);
                Add(machine,controller,"Work",.70f,false,0f);
                Add(machine,controller,"Phone",1.2f,true,0f);
                machine.defaultState = Array.Find(machine.states, row => row.state.name == RelaxedIdle).state;
                AssetDatabase.SaveAssets();
            }
            catch
            {
                AssetDatabase.DeleteAsset(ControllerPath);
                throw;
            }
        }

        private static void MakeFolder(string parent,string name)
        {
            if (!AssetDatabase.IsValidFolder(parent+"/"+name))
                AssetDatabase.CreateFolder(parent,name);
        }

        private static void Add(AnimatorStateMachine machine,AnimatorController controller,
            string label,float length,bool loops,float stride)
        {
            var state = machine.AddState(label);
            var clip = new AnimationClip { name=label, frameRate=30f, legacy=false };
            bool relaxed = label == RelaxedIdle;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loops;
            AnimationUtility.SetAnimationClipSettings(clip,settings);
            Height(clip,length,t =>
            {
                float sway = Mathf.Sin(t*Mathf.PI*2f);
                float action = Mathf.Sin(t*Mathf.PI);
                switch (label)
                {
                    case RelaxedIdle: return .949f+.0022f*sway;
                    case "Crouch": return .62f+.01f*sway;
                    case "Drive": return .40f+.005f*sway;
                    case "Sit": return Mathf.Lerp(.95f,.47f,t);
                    case "Land": return .95f-.14f*action;
                    case "Pick Up": return .95f-.42f*action;
                    case "Open Door": return .95f-.025f*action;
                    case "Enter Vehicle": return .94f-.47f*t;
                    case "Exit Vehicle": return .47f+.47f*t;
                    default: return .95f+.026f*Mathf.Abs(sway)*(stride/52f)+sway*.005f;
                }
            });
            Curve(clip,"Body",length,t =>
            {
                var a = Mathf.Sin(t*Mathf.PI);
                if (relaxed)
                    return Quaternion.Euler(2.4f+.62f*Mathf.Sin(t*2f*Mathf.PI),-1.6f,-.9f);
                float bend = label == "Crouch" ? 18f : label == "Pick Up" ? a*33f : label == "Drive" ? 8f : 3f+stride*.15f;
                float turn = label == "Punch" ? a*24f : label.Contains("Vehicle") ? a*24f : 0f;
                return Quaternion.Euler(bend,turn,0f);
            });
            Curve(clip,"Body/Head",length,t =>
            {
                if (relaxed)
                    return Quaternion.Euler(-2.9f+.7f*Mathf.Cos(t*2f*Mathf.PI),2.2f*Mathf.Sin(t*2f*Mathf.PI),1.1f);
                return Quaternion.Euler(label == "Phone" ? -4f : label == "Pick Up" ? 16f : -3f,
                    label == "Idle" || label == "Drive" ? Mathf.Sin(t*Mathf.PI*2f)*4f : 0f,0f);
            });

            for (var i=0;i<2;i++)
            {
                float side = i == 0 ? -1f : 1f;
                int limb = i;
                string hip = "Body/Hip "+i;
                string knee = hip+"/Knee "+i;
                string ankle = knee+"/Ankle "+i;
                string shoulder = "Body/Shoulder "+i;
                float Cycle(float t) => Mathf.Sin(t*Mathf.PI*2f)*side;
                float Act(float t) => Mathf.Sin(t*Mathf.PI);
                bool seated = label == "Drive" || label == "Sit";
                bool squat = label == "Crouch";
                bool airborne = label == "Fall" || label == "Jump";
                Curve(clip,hip,length,t =>
                {
                    float a = Cycle(t)*stride;
                    if (seated) a = -82f;
                    if (squat) a -= 36f;
                    if (airborne) a = -23f+side*12f;
                    if (label == "Land" || label == "Pick Up") a = -38f*Act(t);
                    return Quaternion.Euler(a,0f,side*2f);
                });
                Curve(clip,knee,length,t =>
                {
                    float a = Mathf.Max(0f,Cycle(t))*stride*.83f;
                    if (seated) a = 71f;
                    if (squat) a += 66f;
                    if (airborne) a = 31f;
                    if (label == "Land" || label == "Pick Up") a = 56f*Act(t);
                    return Quaternion.Euler(a,0f,0f);
                });
                Curve(clip,ankle,length,t => Quaternion.Euler(-Cycle(t)*stride*.21f,side*2f,0f));
                Curve(clip,shoulder,length,t =>
                {
                    if (relaxed)
                        return Quaternion.Euler(-2.8f,-side*5f,side*5f);
                    float angle = -Cycle(t)*stride*.77f;
                    if (seated) angle = -51f;
                    if (squat) angle -= 13f;
                    if (airborne) angle = -22f+side*21f;
                    if (label == "Carry") angle = -61f;
                    if (label == "Work") angle = -63f*Act(t);
                    if (label == "Pick Up") angle = -44f*Act(t);
                    if ((label == "Phone" || label == "Open Door") && limb == 1) angle = -37f;
                    if (label=="Punch" && limb==1) angle = -105f*Act(t);
                    return Quaternion.Euler(angle,-side*11f,side*8f);
                });
                Curve(clip,shoulder+"/Elbow "+i,length,t =>
                {
                    if (relaxed)
                        return Quaternion.Euler(-23f+.8f*Mathf.Sin(t*2f*Mathf.PI),side*4f,-side*21f);
                    float angle = -12f-Mathf.Max(0f,-Cycle(t))*stride*.59f;
                    if (seated) angle = -42f;
                    if (label == "Carry") angle = -49f;
                    if (label == "Work" || label == "Pick Up") angle -= 48f*Act(t);
                    if (label == "Phone" && limb == 1) angle = -121f;
                    return Quaternion.Euler(angle,0f,0f);
                });
            }

            clip.EnsureQuaternionContinuity();
            AssetDatabase.AddObjectToAsset(clip,controller);
            state.motion = clip;
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(controller);
        }

        private static void Height(AnimationClip clip,float length,Func<float,float> sample)
        {
            var keys = new Keyframe[13];
            for(int i=0;i<keys.Length;i++)
            {
                float t = i/(float)(keys.Length-1);
                keys[i] = new Keyframe(length*t,sample(t));
            }
            clip.SetCurve("Body",typeof(Transform),"m_LocalPosition.y",new AnimationCurve(keys));
        }

        private static void Curve(AnimationClip clip,string path,float length,Func<float,Quaternion> sample)
        {
            var keys = new Keyframe[4,13];
            string[] channels = {"x","y","z","w"};
            for(int i=0;i<keys.GetLength(1);i++)
            {
                float t = i/(float)(keys.GetLength(1)-1);
                Quaternion q = sample(t);
                for(int c=0;c<4;c++)
                    keys[c,i] = new Keyframe(length*t,q[c]);
            }
            for(int c=0;c<4;c++)
            {
                var component = new Keyframe[keys.GetLength(1)];
                for(int i=0;i<component.Length;i++)
                    component[i] = keys[c,i];
                clip.SetCurve(path,typeof(Transform),"m_LocalRotation."+channels[c],new AnimationCurve(component));
            }
        }
    }
}
#endif
