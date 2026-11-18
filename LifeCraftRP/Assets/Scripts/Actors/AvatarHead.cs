using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraftRP
{
    /// <summary>Loads a fitted anatomical head and neck derived from the CC0 MakeHuman base mesh.</summary>
    internal static class AvatarHead
    {
        private const string AssetDirectory = "Art/Avatar/";
        private static Mesh headMesh;
        private static Texture2D complexion;

        internal static bool Build(Transform head, Material skin)
        {
            if (!headMesh)
            {
                headMesh = LoadMesh();
                if (!headMesh)
                    return false;
                complexion = Resources.Load<Texture2D>(AssetDirectory + "HeadComplexion");
            }

            var model = new GameObject("Голова и шея · анатомическая модель",
                typeof(MeshFilter), typeof(MeshRenderer));
            model.transform.SetParent(head, false);
            model.GetComponent<MeshFilter>().sharedMesh = headMesh;
            var renderer = model.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = skin;
            if (complexion)
            {
                var appearance = new MaterialPropertyBlock();
                if (skin.HasProperty("_MainTex"))
                    appearance.SetTexture("_MainTex", complexion);
                if (skin.HasProperty("_BaseMap"))
                    appearance.SetTexture("_BaseMap", complexion);
                renderer.SetPropertyBlock(appearance);
            }

            var sclera = ActorArt.Paint(new Color(.88f,.84f,.77f),.56f);
            var iris = ActorArt.Paint(new Color(.195f,.121f,.075f),.61f);
            var pupil = ActorArt.Paint(new Color(.032f,.025f,.021f),.70f);
            for (int side=-1; side<=1; side+=2)
            {
                float x = side * .0332f;
                ActorArt.Form(PrimitiveType.Sphere,"Глаз",head,
                    new Vector3(x,.1184f,.0665f),new Vector3(.0284f,.0280f,.0284f),sclera);
                ActorArt.Form(PrimitiveType.Sphere,"Радужка",head,
                    new Vector3(x,.1183f,.0795f),new Vector3(.0116f,.0116f,.0035f),iris);
                ActorArt.Form(PrimitiveType.Sphere,"Зрачок",head,
                    new Vector3(x,.1183f,.081f),new Vector3(.0048f,.0048f,.0012f),pupil);
            }
            return true;
        }

        private static Mesh LoadMesh()
        {
            var asset = Resources.Load<TextAsset>(AssetDirectory + "AnatomicalHead");
            if (!asset)
                return null;
            byte[] bytes = asset.bytes;
            using var input = new BinaryReader(new MemoryStream(bytes, false));
            if (bytes.Length < 12 || input.ReadUInt32() != 0x3148434C) // LCH1
                return null;

            int vertices = input.ReadInt32();
            int indexCount = input.ReadInt32();
            if (vertices <= 0 || vertices > ushort.MaxValue || indexCount <= 0
                || indexCount % 3 != 0 || bytes.Length != 12L + 32L * vertices + 2L * indexCount)
                return null;

            var positions = new Vector3[vertices];
            var normals = new Vector3[vertices];
            var coordinates = new Vector2[vertices];
            for (int i=0; i<vertices; i++)
            {
                float x = input.ReadSingle(), y = input.ReadSingle(), z = input.ReadSingle();
                // Extend the neck under the collar so head turns don't expose its cut edge.
                float tuck = Mathf.InverseLerp(-.038f,-.070f,y);
                positions[i] = new Vector3(x,y-.028f*tuck*tuck,z);
                normals[i] = new Vector3(input.ReadSingle(),input.ReadSingle(),input.ReadSingle());
                coordinates[i] = new Vector2(input.ReadSingle(),input.ReadSingle());
            }
            var triangles = new int[indexCount];
            for (int i=0; i<indexCount; i++)
            {
                int index = input.ReadUInt16();
                if (index >= vertices)
                    return null;
                triangles[i] = index;
            }

            var mesh = new Mesh
            {
                name = "LC · MakeHuman hm08 fitted head",
                indexFormat = IndexFormat.UInt16,
                vertices = positions,
                normals = normals,
                uv = coordinates,
                triangles = triangles,
            };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
