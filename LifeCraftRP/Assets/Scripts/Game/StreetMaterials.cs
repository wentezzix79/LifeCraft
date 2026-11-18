using System.Collections.Generic;
using UnityEngine;

namespace LifeCraftRP
{
    internal static class StreetMaterials
    {
        private static readonly Dictionary<string,Texture2D> Textures = new Dictionary<string,Texture2D>();
        private static readonly int BaseMapTransform = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTextureTransform = Shader.PropertyToID("_MainTex_ST");
        private static readonly int NormalTransform = Shader.PropertyToID("_BumpMap_ST");

        internal static void Apply(Material material,string texture,float tileX=1,float tileY=1)
        {
            Texture2D albedo = Get(texture);
            if (albedo != null)
                material.mainTexture=albedo;
            var scale=new Vector2(tileX,tileY);
            material.mainTextureScale=scale;
            if (!material.HasProperty("_BumpMap"))
                return;
            Texture2D normal=Get(texture+"Normal");
            if (normal == null)
                return;
            material.SetTexture("_BumpMap",normal);
            material.SetTextureScale("_BumpMap",scale);
            material.SetFloat("_BumpScale",.58f);
            material.EnableKeyword("_NORMALMAP");
        }

        internal static void SetTiling(Renderer renderer,MaterialPropertyBlock block,float u,float v)
        {
            block.Clear();
            var transform=new Vector4(u,v,0,0);
            block.SetVector(BaseMapTransform,transform);
            block.SetVector(MainTextureTransform,transform);
            block.SetVector(NormalTransform,transform);
            renderer.SetPropertyBlock(block);
        }

        private static Texture2D Get(string name)
        {
            if (!Textures.TryGetValue(name,out Texture2D texture))
            {
                texture=Resources.Load<Texture2D>("Art/Streets/"+name);
                if (texture != null)
                    texture.anisoLevel=8;
                Textures[name]=texture;
            }
            return texture;
        }
    }
}
