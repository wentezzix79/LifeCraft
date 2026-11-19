using System.Collections.Generic;
using LifeCraftRP;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraft.World
{
    internal static class OmskMallSigns
    {
        private static readonly Dictionary<(string, bool),Material> SignMaterials =
            new Dictionary<(string, bool),Material>();

        internal static void MegaRoof(Transform root,Vector3 localCenter,Vector3 outward,float width)
            => Build(root,"MegaEntrance",localCenter,outward,width,.302f,true);

        internal static void MegaPylon(Transform root,Vector3 localCenter,Vector3 outward,float width)
            => Build(root,"MegaPylon",localCenter,outward,width,.302f,false);

        internal static void ParkRoof(Transform root,Vector3 localCenter,Vector3 outward,float width)
            => Build(root,"ParkRoof",localCenter,outward,width,1f/3f,true);

        internal static void MegaWing(Transform root,Vector3 localCenter,Vector3 outward,float width,bool auchan)
            => Build(root,auchan?"MegaAuchan":"MegaVideo",localCenter,outward,width,.228f,true);

        private static void Build(Transform root,string name,Vector3 center,Vector3 outward,float width,
            float aspect,bool roofFrame)
        {
            Texture2D texture = Resources.Load<Texture2D>("Art/OmskSigns/"+name);
            if (!texture)
                return;

            Vector3 normal = outward.normalized;
            Vector3 right = Vector3.Cross(normal,Vector3.up).normalized;
            float height = width*aspect;
            Face(root,name,center,right,width,height,MaterialFor(name,texture,false),false);
            Face(root,name+" · тыльная сторона",center-normal*.14f,right,width,height,
                MaterialFor(name,texture,true),true);

            if (roofFrame)
                Mount(root,name,center-normal*.31f,right,normal,width,height);
        }

        private static void Face(Transform root,string label,Vector3 center,Vector3 right,float width,
            float height,Material material,bool back)
        {
            Vector3 span = right*(width*.5f);
            Vector3 rise = Vector3.up*(height*.5f);
            var vertices = new List<Vector3> { center-span-rise,center+span-rise,center+span+rise,center-span+rise };
            var indices = back ? new List<int> {0,1,2,0,2,3} : new List<int> {0,2,1,0,3,2};
            var uv = new List<Vector2> { Vector2.zero,Vector2.right,Vector2.one,Vector2.up };
            var sign = ActorArt.Custom("Крышная вывеска · "+label,root,vertices,indices,material,uv);
            sign.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Material MaterialFor(string name,Texture2D texture,bool back)
        {
            var key = (name,back);
            if (SignMaterials.TryGetValue(key,out var material) && material)
                return material;
            Color tint = back ? new Color(.245f,.249f,.248f) : Color.white;
            material = new Material(RuntimeShaders.Lit()) { name="Омск · "+name,color=tint,mainTexture=texture };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor",tint);
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip",1f);
            if (material.HasProperty("_Mode"))
                material.SetFloat("_Mode",1f);
            if (material.HasProperty("_Cutoff"))
                material.SetFloat("_Cutoff",.45f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness",.27f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness",.27f);
            material.SetOverrideTag("RenderType","TransparentCutout");
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            SignMaterials[key] = material;
            return material;
        }

        private static void Mount(Transform root,string name,Vector3 center,Vector3 right,Vector3 outward,
            float width,float height)
        {
            var points = new List<Vector3>();
            var faces = new List<int>();
            float lower = -height*.5f-.42f;
            float length = width*.44f;
            for (int i=-2;i<=2;i++)
            {
                Vector3 rail = center+right*(i*length*.5f);
                Vector3 foot = rail+Vector3.up*lower;
                Beam(foot,rail+Vector3.up*(height*.45f),.051f,points,faces);
                if (i<2)
                    Beam(foot,rail+right*(length*.5f)+Vector3.up*(height*.20f),.031f,points,faces);
                Beam(foot-outward*.37f,rail+Vector3.up*(height*.30f),.041f,points,faces);
            }
            for (int i=-1;i<=1;i++)
            {
                Vector3 rail = center+Vector3.up*(i*height*.37f);
                Beam(rail-right*length,rail+right*length,.049f,points,faces);
            }
            Material metal = ActorArt.Paint(new Color(.42f,.435f,.44f),.34f,.47f);
            ActorArt.Custom("Ферма вывески · "+name,root,points,faces,metal);
        }

        private static void Beam(Vector3 a,Vector3 b,float size,List<Vector3> points,List<int> faces)
        {
            Vector3 direction = (b-a).normalized;
            Vector3 right = Vector3.Cross(direction,Mathf.Abs(direction.y)>.95f ?
                Vector3.forward : Vector3.up).normalized*size;
            Vector3 up = Vector3.Cross(direction,right);
            int i = points.Count;
            points.Add(a-right-up); points.Add(a+right-up); points.Add(a+right+up); points.Add(a-right+up);
            points.Add(b-right-up); points.Add(b+right-up); points.Add(b+right+up); points.Add(b-right+up);
            for (int j=0;j<4;j++)
            {
                int p=i+j,q=i+(j+1)%4;
                faces.Add(p); faces.Add(q); faces.Add(q+4);
                faces.Add(p); faces.Add(q+4); faces.Add(p+4);
            }
        }
    }
}
