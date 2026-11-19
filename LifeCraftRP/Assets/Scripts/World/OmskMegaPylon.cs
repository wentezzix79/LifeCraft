using System.Collections.Generic;
using LifeCraftRP;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraft.World
{
    internal static class OmskMegaPylon
    {
        // Centre of the mapped west-side circulation island, outside its ring road.
        private static readonly Vector3 Island = new Vector3(-226f,0f,75.7f);
        private static readonly Vector3 Outward = new Vector3(-.755f,0f,.656f).normalized;
        private static readonly Vector3 Right = Vector3.Cross(Vector3.up,Outward);

        private static readonly Dictionary<Color,Material> Materials = new Dictionary<Color,Material>();

        private static readonly Color Steel = new Color(.887f,.893f,.860f);
        private static readonly Color Seam = new Color(.677f,.702f,.700f);
        private static readonly Color Grey = new Color(.478f,.475f,.455f);
        private static readonly Color Grass = new Color(.478f,.573f,.322f);
        private static readonly Color White = new Color(.960f,.957f,.916f);
        private static readonly Color Black = new Color(.094f,.108f,.115f);
        private static readonly Color Blue = new Color(.082f,.245f,.530f);
        private static readonly Color Red = new Color(.753f,.081f,.130f);
        private static readonly Color Yellow = new Color(.989f,.782f,.211f);

        private static readonly SignFace[] Signs =
        {
            new SignFace("IKEA",Blue,Yellow,.90f,FontStyle.Bold),
            new SignFace("Ашан",White,new Color(.792f,.082f,.091f),.93f,FontStyle.Bold),
            new SignFace("ЛЕРУА МЕРЛЕН",White,new Color(.196f,.386f,.210f),.46f,FontStyle.Bold),
            new SignFace("М.Видео",Red,White,.70f,FontStyle.BoldAndItalic),
            new SignFace("СПОРТМАСТЕР",Blue,White,.48f,FontStyle.BoldAndItalic),
            new SignFace("MEGACARD",Black,White,.70f,FontStyle.BoldAndItalic),
            new SignFace("КИНОТЕАТР",Blue,new Color(.965f,.907f,.744f),.56f,FontStyle.Bold)
        };

        private readonly struct SignFace
        {
            internal readonly string Text;
            internal readonly Color Panel;
            internal readonly Color Ink;
            internal readonly float Size;
            internal readonly FontStyle Style;

            internal SignFace(string text,Color panel,Color ink,float size,FontStyle style)
            {
                Text=text; Panel=panel; Ink=ink; Size=size; Style=style;
            }
        }

        internal static void Build(Transform megaRoot)
        {
            var batches = new Dictionary<Color,Batch>();
            IslandGeometry(batches);
            Tower(batches);
            SignPanels(megaRoot,batches);
            Publish(megaRoot,batches);
            OmskMallSigns.MegaPylon(megaRoot,At(0f,21.84f,.81f),Outward,11.8f);
        }

        private static Vector3 At(float x,float height,float depth) =>
            Island+Right*x+Outward*depth+Vector3.up*height;

        private static void IslandGeometry(Dictionary<Color,Batch> batches)
        {
            Batch edging=Group(batches,Grey);
            Batch lawn=Group(batches,Grass);
            const int points=40;
            for (int i=0;i<points;i++)
            {
                float a=i*(Mathf.PI*2f/points);
                float b=(i+1)*(Mathf.PI*2f/points);
                float ax=Mathf.Cos(a), az=Mathf.Sin(a);
                float bx=Mathf.Cos(b), bz=Mathf.Sin(b);
                Vector3 outerA=At(8.36f*ax,.126f,7.54f*az);
                Vector3 outerB=At(8.36f*bx,.126f,7.54f*bz);
                Vector3 innerA=At(8.13f*ax,.126f,7.31f*az);
                Vector3 innerB=At(8.13f*bx,.126f,7.31f*bz);
                edging.Quad(outerB,outerA,innerA,innerB);
                edging.Quad(outerA-Vector3.up*.125f,outerB-Vector3.up*.125f,outerB,outerA);
                lawn.Triangle(At(0,.116f,0), innerB-Vector3.up*.010f, innerA-Vector3.up*.010f);
            }
            edging.Box(0,.12f,0,3.68f,.29f,3.02f);
            edging.Box(0,.30f,0,3.12f,.13f,2.80f);
        }

        private static void Tower(Dictionary<Color,Batch> batches)
        {
            Batch frame=Group(batches,Steel);
            Batch seams=Group(batches,Seam);
            var lower = new Vector3[4];
            var upper = new Vector3[4];
            const int levels=8;
            for (int i=0;i<=levels;i++)
            {
                float fraction=i/(float)levels;
                float y=.35f+fraction*19.85f;
                float x=Mathf.Lerp(1.48f,1.13f,fraction);
                float z=Mathf.Lerp(1.21f,.56f,fraction);
                upper[0]=At(-x,y,-z);
                upper[1]=At(x,y,-z);
                upper[2]=At(x,y,z);
                upper[3]=At(-x,y,z);
                if (i>0)
                {
                    for (int side=0;side<4;side++)
                    {
                        int next=(side+1)%4;
                        frame.Beam(lower[side],upper[side],.083f);
                        frame.Beam(upper[side],upper[next],.059f);
                        seams.Beam(lower[side],upper[next],.039f);
                        seams.Beam(lower[next],upper[side],.039f);
                    }
                }
                if (i==1 || i==4 || i==7)
                {
                    frame.Beam(upper[0],upper[2],.052f);
                    frame.Beam(upper[1],upper[3],.052f);
                }
                for (int side=0;side<4;side++)
                    lower[side]=upper[side];
            }

            for (int side=-1;side<=1;side+=2)
            {
                float depth=side*.60f;
                frame.Box(0,20.09f,depth,11.65f,.15f,.14f);
                frame.Box(0,23.48f,depth,11.65f,.10f,.13f);
                for (int i=0;i<5;i++)
                {
                    float x=-5.73f+i*2.865f;
                    frame.Box(x,21.80f,depth,.092f,3.43f,.092f);
                    if (i<4)
                        seams.Beam(At(x,20.09f,depth),At(x+2.865f,23.48f,depth),.040f);
                }
                frame.Beam(At(0,17.6f,depth),At(-5.73f,20.09f,depth),.051f);
                frame.Beam(At(0,17.6f,depth),At(5.73f,20.09f,depth),.051f);
            }
        }

        private static void SignPanels(Transform parent,Dictionary<Color,Batch> batches)
        {
            Font font=Resources.Load<Font>("Art/DejaVuSans");
            if (!font)
                font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Batch frame=Group(batches,Steel);
            Batch trim=Group(batches,Seam);
            const float width=4.72f, pitch=1.43f;
            for (int side=-1;side<=1;side+=2)
            {
                float depth=side*.74f;
                frame.Box(width*.5f+.08f,14.91f,depth,.12f,10.08f,.17f);
                frame.Box(-width*.5f-.08f,14.91f,depth,.12f,10.08f,.17f);
                for (int index=0;index<Signs.Length;index++)
                {
                    SignFace sign=Signs[index];
                    float y=19.25f-index*pitch;
                    Group(batches,sign.Panel).Box(0,y,depth,width,1.36f,.12f);
                    trim.Box(0,y-.70f,depth+side*.071f,width+.23f,.057f,.08f);
                    if (font)
                        Label(parent,font,sign,At(0,y,depth+side*.063f),side*Outward);
                }
            }
        }

        private static void Label(Transform parent,Font font,SignFace sign,Vector3 position,Vector3 normal)
        {
            var obj=new GameObject("Указатель МЕГА · "+sign.Text,typeof(TextMesh));
            obj.transform.SetParent(parent,false);
            obj.transform.localPosition=position;
            obj.transform.localRotation=Quaternion.LookRotation(-normal,Vector3.up);
            TextMesh text=obj.GetComponent<TextMesh>();
            text.font=font;
            text.fontSize=88;
            text.characterSize=sign.Size;
            text.anchor=TextAnchor.MiddleCenter;
            text.alignment=TextAlignment.Center;
            text.fontStyle=sign.Style;
            text.color=sign.Ink;
            text.text=sign.Text;
            obj.GetComponent<MeshRenderer>().sharedMaterial=font.material;
        }

        private static Batch Group(Dictionary<Color,Batch> batches,Color color)
        {
            if (!batches.TryGetValue(color,out Batch batch))
                batches.Add(color,batch=new Batch());
            return batch;
        }

        private static void Publish(Transform parent,Dictionary<Color,Batch> batches)
        {
            foreach (var pair in batches)
            {
                Batch batch=pair.Value;
                var obj=new GameObject("Стела МЕГА · "+(pair.Key==Grass?"газон":"сборка"),typeof(MeshFilter),typeof(MeshRenderer));
                obj.transform.SetParent(parent,false);
                var mesh=new Mesh { name=obj.name };
                mesh.SetVertices(batch.Points);
                mesh.SetUVs(0,batch.Uv);
                mesh.SetTriangles(batch.Indices,0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                obj.GetComponent<MeshFilter>().sharedMesh=mesh;
                obj.GetComponent<MeshRenderer>().sharedMaterial=GetMaterial(pair.Key);
            }
        }

        private static Material GetMaterial(Color color)
        {
            if (Materials.TryGetValue(color,out Material material) && material)
                return material;
            material=new Material(RuntimeShaders.Lit()) { name="Стела МЕГА",color=color };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor",color);
            bool steel=color==Steel || color==Seam;
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness",steel?.33f:.18f);
            if (material.HasProperty("_Metallic") && steel)
                material.SetFloat("_Metallic",.26f);
            if (color==Grey)
                StreetMaterials.Apply(material,"WeatheredConcrete",.35f,.35f);
            if (color==Grass)
                StreetMaterials.Apply(material,"Grass",.75f,.75f);
            Materials[color]=material;
            return material;
        }

        private sealed class Batch
        {
            internal readonly List<Vector3> Points = new List<Vector3>();
            internal readonly List<Vector2> Uv = new List<Vector2>();
            internal readonly List<int> Indices = new List<int>();

            internal void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                int start=Points.Count;
                Vector3 normal=Vector3.Cross(b-a,c-a);
                AddUv(a,normal); AddUv(b,normal); AddUv(c,normal);
                Indices.Add(start); Indices.Add(start+1); Indices.Add(start+2);
            }

            internal void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int start=Points.Count;
                Vector3 normal=Vector3.Cross(b-a,c-a);
                AddUv(a,normal); AddUv(b,normal); AddUv(c,normal); AddUv(d,normal);
                Indices.Add(start); Indices.Add(start+1); Indices.Add(start+2);
                Indices.Add(start); Indices.Add(start+2); Indices.Add(start+3);
            }

            private void AddUv(Vector3 point,Vector3 normal)
            {
                Points.Add(point);
                if (Mathf.Abs(normal.y)>=Mathf.Max(Mathf.Abs(normal.x),Mathf.Abs(normal.z)))
                    Uv.Add(new Vector2(point.x,point.z));
                else if (Mathf.Abs(normal.x)>Mathf.Abs(normal.z))
                    Uv.Add(new Vector2(point.z,point.y));
                else
                    Uv.Add(new Vector2(point.x,point.y));
            }

            internal void Box(float x,float y,float z,float width,float height,float depth)
            {
                Vector3 center=At(x,y,z);
                Prism(center-Vector3.up*(height*.5f),center+Vector3.up*(height*.5f),
                    Right*(width*.5f),Outward*(depth*.5f));
            }

            internal void Beam(Vector3 a,Vector3 b,float halfWidth)
            {
                Vector3 direction=(b-a).normalized;
                Vector3 side=Vector3.Cross(direction,Mathf.Abs(direction.y)>.8f?Outward:Vector3.up).normalized;
                Prism(a,b,side*halfWidth,Vector3.Cross(direction,side)*halfWidth);
            }

            private void Prism(Vector3 bottom,Vector3 top,Vector3 side,Vector3 depth)
            {
                Vector3 a=bottom-side-depth, b=bottom+side-depth, c=bottom+side+depth, d=bottom-side+depth;
                Vector3 e=top-side-depth, f=top+side-depth, g=top+side+depth, h=top-side+depth;
                Vector3 tangent=Vector3.Cross(side,depth);
                if (Vector3.Dot(tangent,top-bottom)<0f)
                {
                    (b,d)=(d,b);
                    (f,h)=(h,f);
                }
                Quad(a,d,c,b); Quad(e,f,g,h);
                Quad(a,b,f,e); Quad(b,c,g,f); Quad(c,d,h,g); Quad(d,a,e,h);
            }
        }
    }
}
