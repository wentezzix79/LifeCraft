using System.Collections.Generic;
using UnityEngine;

namespace LifeCraftRP
{
    internal static class ActorArt
    {
        private static readonly Dictionary<(Color Color, float Gloss, float Metal), Material> Paints = new Dictionary<(Color, float, float), Material>();

        internal static Material Paint(Color color, float gloss = .22f, float metallic = 0f)
        {
            var key = (color,gloss,metallic);
            if (Paints.TryGetValue(key,out var cached) && cached)
                return cached;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader)
                shader = Shader.Find("Standard");
            var material = new Material(shader) { color = color, name = "LC · " + ColorUtility.ToHtmlStringRGB(color) };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor",color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness",gloss);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness",gloss);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic",metallic);
            Paints[key] = material;
            return material;
        }

        internal static Transform Joint(string label, Transform parent, Vector3 position)
        {
            var child = new GameObject(label).transform;
            child.SetParent(parent,false);
            child.localPosition = position;
            return child;
        }

        internal static GameObject Form(PrimitiveType type, string name, Transform parent,
            Vector3 center, Vector3 dimensions, Material material)
        {
            var shape = GameObject.CreatePrimitive(type);
            shape.name = name;
            shape.transform.SetParent(parent,false);
            shape.transform.localPosition = center;
            shape.transform.localScale = dimensions;
            shape.GetComponent<Renderer>().sharedMaterial = material;
            var collider = shape.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            return shape;
        }

        internal static GameObject Loft(string name, Transform parent, IReadOnlyList<Vector3> centers,
            IReadOnlyList<Vector2> radii, int sides, Material material)
        {
            var vertices = new List<Vector3>(centers.Count*sides+2);
            var triangles = new List<int>((centers.Count+1)*sides*6);
            for (var ring = 0; ring < centers.Count; ring++)
            for (var side = 0; side < sides; side++)
            {
                float phi = side * (Mathf.PI * 2f / sides);
                vertices.Add(centers[ring] + new Vector3(radii[ring].x * Mathf.Cos(phi),
                    0f, radii[ring].y * Mathf.Sin(phi)));
            }
            for (var ring = 0; ring < centers.Count - 1; ring++)
            for (var side = 0; side < sides; side++)
            {
                int p = ring*sides + side;
                int q = ring*sides + (side+1)%sides;
                triangles.Add(p); triangles.Add(p+sides); triangles.Add(q);
                triangles.Add(q); triangles.Add(p+sides); triangles.Add(q+sides);
            }
            int bottom = vertices.Count;
            vertices.Add(centers[0]);
            int top = vertices.Count;
            vertices.Add(centers[centers.Count-1]);
            for (var i = 0; i < sides; i++)
            {
                int p = (centers.Count-1)*sides+i;
                int q = (centers.Count-1)*sides+(i+1)%sides;
                triangles.Add(bottom); triangles.Add(i); triangles.Add((i+1)%sides);
                triangles.Add(top); triangles.Add(q); triangles.Add(p);
            }
            return Custom(name,parent,vertices,triangles,material);
        }

        internal static GameObject Custom(string name, Transform parent, List<Vector3> vertices,
            List<int> triangles, Material material,List<Vector2> uv=null)
        {
            var go = new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(parent,false);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals();
            if (uv!=null)
            {
                mesh.SetUVs(0,uv);
                mesh.RecalculateTangents();
            }
            mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
        internal static void Surface(string name, Transform parent, Vector3 a,
            Vector3 b,Vector3 c,Material material)
        {
            var vertices = new List<Vector3> { a,b,c };
            var normal = Vector3.Cross(b-a,c-a);
            var triangles = normal.z >= 0 ? new List<int> {0,1,2} : new List<int> {0,2,1};
            Custom(name,parent,vertices,triangles,material);
        }

        internal static GameObject Bar(string name,Transform parent,Vector3 a,Vector3 b,
            float width,float depth,Material material)
        {
            Vector3 axis = b-a;
            var obj = Form(PrimitiveType.Cube,name,parent,(a+b)*.5f,
                new Vector3(width,axis.magnitude+.002f,depth),material);
            float angle = -Mathf.Atan2(axis.x,axis.y)*Mathf.Rad2Deg;
            float tilt = Mathf.Atan2(axis.z,Mathf.Sqrt(axis.x*axis.x+axis.y*axis.y))*Mathf.Rad2Deg;
            obj.transform.localRotation = Quaternion.Euler(tilt,0,angle);
            return obj;
        }

        internal static GameObject RoundedPanel(string name,Transform parent,Vector3 center,
            float width,float height,float depth,float shape,Material material)
        {
            const int sides = 24;
            var verts = new List<Vector3>(sides*4+2);
            var indices = new List<int>(sides*24);
            for(int ring=0;ring<4;ring++)
            {
                float bevel = ring==0 || ring==3 ? .86f : 1f;
                float z = depth*(ring==0?-.5f:ring==1?-.28f:ring==2?.28f:.5f);
                for (int i=0;i<sides;i++)
                {
                    float angle = i*Mathf.PI*2f/sides;
                    float x=Mathf.Cos(angle), y=Mathf.Sin(angle);
                    verts.Add(new Vector3(Mathf.Sign(x)*Mathf.Pow(Mathf.Abs(x),2f/shape)*width*.5f*bevel,
                        Mathf.Sign(y)*Mathf.Pow(Mathf.Abs(y),2f/shape)*height*.5f*bevel,z));
                }
            }
            for(int ring=0;ring<3;ring++)
                for(int i=0;i<sides;i++)
                {
                    int a=ring*sides+i,b=ring*sides+(i+1)%sides;
                    indices.AddRange(new [] {a,b,b+sides,a,b+sides,a+sides});
                }
            int back=verts.Count,front=back+1;
            verts.Add(new Vector3(0,0,-depth*.5f));
            verts.Add(new Vector3(0,0,depth*.5f));
            for(int i=0;i<sides;i++)
            {
                int next=(i+1)%sides;
                indices.AddRange(new []{back,next,i,front,sides*3+i,sides*3+next});
            }
            var obj=Custom(name,parent,verts,indices,material);
            obj.transform.localPosition=center;
            return obj;
        }


    }
}
