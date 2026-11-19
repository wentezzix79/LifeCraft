using System.Collections.Generic;
using LifeCraftRP;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraft.World
{
    /// <summary>The pedestrian forecourt outside MEGA Entrance 2.</summary>
    internal static class OmskMegaPlaza
    {
        private const float Walk = .128f;
        private const float Play = .146f;
        private enum Finish
        {
            Paving, Road, Sand, Lawn, Stone, AsphaltBorder, Steel, Wood, Bark,
            Olive, Silver, DarkLeaves, Aqua, Blue, Yellow, Red
        }

        private static readonly Color32[] Palette =
        {
            new Color32(163,159,153,255), new Color32(137,137,133,255),
            new Color32(186,143,103,255), new Color32(133,149,101,255),
            new Color32(195,191,175,255), new Color32(76,77,72,255),
            new Color32(35,33,34,255), new Color32(128,88,52,255),
            new Color32(115,104,83,255), new Color32(124,149,123,255),
            new Color32(157,183,166,255), new Color32(67,106,68,255),
            new Color32(54,184,213,255), new Color32(55,120,177,255),
            new Color32(247,188,61,255), new Color32(203,90,75,255)
        };
        private static readonly Finish[] FlagColors =
            { Finish.Aqua, Finish.Yellow, Finish.Red, Finish.Blue, Finish.Yellow, Finish.DarkLeaves };
        private static readonly Dictionary<int,Material> Materials = new Dictionary<int,Material>();

        internal static void Build(Transform root,Vector3 entrance,Vector3 outward)
        {
            // X points to the right as seen from the approach, Z points away from the doors.
            Vector3 forward = new Vector3(outward.x,0,outward.z).normalized;
            Vector3 right = Vector3.Cross(forward,Vector3.up);
            Vector3 origin = root.InverseTransformPoint(new Vector3(entrance.x,root.position.y,entrance.z));
            Vector3 across = root.InverseTransformDirection(right);
            Vector3 away = root.InverseTransformDirection(forward);
            var batches = new Dictionary<Finish,MeshBatch>();

            void Ground(Finish finish,float x0,float x1,float z0,float z1,float y) =>
                Batch(batches,finish).Quad(new Vector3(x0,y,z0),new Vector3(x1,y,z0),
                    new Vector3(x1,y,z1),new Vector3(x0,y,z1));
            void Box(Finish finish,Vector3 midpoint,Vector3 size) => Batch(batches,finish).Box(midpoint,size);
            void Solid(string label,Vector3 center,Vector3 size) =>
                Collision(root,label,Position(root,origin,across,away,center),size,forward);
            void GroundCollision(float x0,float x1,float z0,float z1,float y) =>
                Solid("Покрытие площади",new Vector3((x0+x1)*.5f,y-.11f,(z0+z1)*.5f),
                    new Vector3(x1-x0,.22f,z1-z0));

            Ground(Finish.Paving,-23.7f,22f,7.46f,11.64f,Walk);
            Ground(Finish.Road,-24f,23f,11.50f,16.53f,.113f);
            Ground(Finish.Paving,-23.7f,22f,16.51f,47f,Walk);
            Ground(Finish.Lawn,-23.7f,-16.07f,17.91f,47f,Play);
            Ground(Finish.Sand,-16.05f,-3.65f,20.78f,46.9f,Play);
            Ground(Finish.Sand,10.06f,22f,20.31f,46.9f,Play);
            Ground(Finish.Sand,-1.6f,4.63f,16.60f,17.17f,.133f);
            GroundCollision(-23.7f,22f,7.46f,11.64f,Walk);
            GroundCollision(-24f,23f,11.64f,16.53f,.113f);
            GroundCollision(-23.7f,22f,16.53f,47f,Walk);
            Edging(batches,-16.05f,20.78f,46.9f);
            Edging(batches,-3.65f,20.78f,46.9f);
            Edging(batches,10.06f,20.31f,46.9f);
            for (int i=0;i<8;i++)
            {
                float z=11.98f+i*.55f;
                Ground(Finish.Stone,-2.1f,1.9f,z,z+.24f,.116f);
            }

            // Low domes protect the paving while the break in the row leads straight to the doors.
            for (int i=-7;i<=7;i++)
            {
                if (i==0) continue;
                float x=i*2.68f;
                var center=new Vector3(x,Walk,11.26f);
                Batch(batches,Finish.Stone).Ellipsoid(center,new Vector3(.275f,.265f,.275f),true);
                Solid("Бетонный ограничитель",center+Vector3.up*.11f,new Vector3(.47f,.24f,.47f));
            }
            for (int i=0;i<6;i++)
            {
                float x=4.9f+i*2.96f;
                Batch(batches,Finish.Stone).Ellipsoid(new Vector3(x,Walk,16.64f),
                    new Vector3(.24f,.22f,.24f),true);
            }

            for (int i=0;i<4;i++)
            {
                float z=19.65f+i*8.06f;
                Lattice(batches,new Vector3(-2.88f,Walk,z),3.84f);
                Lattice(batches,new Vector3(9.08f,Walk,z+.82f),3.84f);
                Solid("Ферма световой гирлянды",new Vector3(-2.88f,Walk+1.92f,z),new Vector3(.31f,3.84f,.31f));
                Solid("Ферма световой гирлянды",new Vector3(9.08f,Walk+1.92f,z+.82f),new Vector3(.31f,3.84f,.31f));
                Pennants(batches,new Vector3(-2.88f,3.79f,z),new Vector3(9.08f,3.71f,z+.82f),.78f,i*4);
                if (i<3)
                    Pennants(batches,new Vector3(-2.88f,3.68f,z),
                        new Vector3(9.08f,3.74f,z+8.88f),.67f,3+i);
                if (i>=1)
                {
                    Bench(batches,-2.06f,z+1.5f);
                    Bench(batches,8.21f,z-2.36f);
                }
            }
            SmallPole(batches,-21f,8.78f,3.52f);
            SmallPole(batches,20.6f,9.40f,3.52f);
            Pennants(batches,new Vector3(-21f,3.36f,8.78f),new Vector3(-2.88f,3.25f,19.65f),.80f,4);
            Pennants(batches,new Vector3(9.08f,3.14f,20.47f),new Vector3(20.6f,3.38f,9.4f),.60f,2);

            Tree(batches,-18.24f,24.30f,4.3f,2.40f);
            Tree(batches,-20.65f,33.10f,4.48f,3.12f);
            Tree(batches,-18.79f,42.22f,4.16f,2.67f);
            Tree(batches,20.84f,18.78f,3.01f,1.38f);
            SmallGarden(batches,-21.56f,9.85f);
            SmallGarden(batches,17.90f,9.31f);
            PottedTree(batches,-7.65f,6.18f);
            PottedTree(batches,7.86f,6.16f);

            Chaise(batches,-11.20f,28.50f,false);
            Chaise(batches,-18.60f,45.21f,true);
            Chaise(batches,17.58f,34.68f,true);
            Slide(batches,-11.85f,35.86f);
            PlaySculpture(batches,-7.18f,22.08f);
            for(int i=0;i<3;i++)
            {
                float z=23.70f+i*9.35f;
                Box(Finish.AsphaltBorder,new Vector3(10.06f,Walk+.46f,z),new Vector3(.55f,.92f,.52f));
                Box(Finish.Stone,new Vector3(10.06f,Walk+.88f,z),new Vector3(.61f,.07f,.56f));
            }

            foreach (var item in batches)
                Publish(root,origin,across,away,item.Key,item.Value);
        }

        private static Vector3 Position(Transform root,Vector3 origin,Vector3 across,Vector3 away,Vector3 point) =>
            root.TransformPoint(origin+across*point.x+Vector3.up*point.y+away*point.z);

        private static void Collision(Transform root,string label,Vector3 center,Vector3 size,Vector3 forward)
        {
            var marker = new GameObject(label,typeof(BoxCollider));
            marker.transform.SetParent(root,false);
            marker.transform.SetPositionAndRotation(center,Quaternion.LookRotation(forward,Vector3.up));
            marker.GetComponent<BoxCollider>().size = size;
        }

        private static void Edging(Dictionary<Finish,MeshBatch> batches,float x,float from,float to)
        {
            for(float z=from;z<to;z+=1.58f)
            {
                float span=Mathf.Min(1.58f,to-z);
                Batch(batches,Mathf.RoundToInt((z-from)/1.58f)%2==0 ? Finish.Stone : Finish.AsphaltBorder)
                    .Box(new Vector3(x,.135f,z+span*.5f),new Vector3(.106f,.105f,span));
            }
        }

        private static void Lattice(Dictionary<Finish,MeshBatch> batches,Vector3 foot,float height)
        {
            var steel=Batch(batches,Finish.Steel);
            steel.Box(foot+Vector3.up*.14f,new Vector3(.83f,.28f,.83f));
            Vector3[] corners =
            {
                new Vector3(-.135f,0,-.135f),new Vector3(.135f,0,-.135f),
                new Vector3(.135f,0,.135f),new Vector3(-.135f,0,.135f)
            };
            foreach(var corner in corners)
                steel.Tube(foot+corner,foot+corner+Vector3.up*height,.017f,4);
            for (int row=0;row<12;row++)
            {
                float bottom=.28f+row*(height-.28f)/12;
                float top=.28f+(row+1)*(height-.28f)/12;
                for(int face=0;face<4;face++)
                {
                    var a=foot+corners[face];
                    var b=foot+corners[(face+1)%4];
                    steel.Tube(a+Vector3.up*bottom,b+Vector3.up*bottom,.011f,4);
                    steel.Tube(row%2==0 ? a+Vector3.up*bottom : b+Vector3.up*bottom,
                        row%2==0 ? b+Vector3.up*top : a+Vector3.up*top,.01f,4);
                }
            }
        }

        private static void Pennants(Dictionary<Finish,MeshBatch> batches,Vector3 from,Vector3 to,float drop,int seed)
        {
            Vector3 P(float t) => Vector3.Lerp(from,to,t)-Vector3.up*(4f*drop*t*(1f-t));
            float length=(from-to).magnitude;
            int count=Mathf.FloorToInt(length/.62f);
            for(int i=0;i<count;i++)
            {
                float t0=i/(float)count,t1=(i+1)/(float)count;
                var a=P(t0);
                var b=P(t1);
                Batch(batches,Finish.Steel).Tube(a,b,.0055f,4);
                Vector3 inner=Vector3.Lerp(a,b,.69f);
                Vector3 tip=Vector3.Lerp(a,inner,.55f)+new Vector3(.014f,-.218f,.049f*(i%2==0?1f:-1f));
                Batch(batches,FlagColors[(i+seed)%FlagColors.Length]).DoubleTriangle(a,inner,tip);
            }
        }

        private static void SmallPole(Dictionary<Finish,MeshBatch> batches,float x,float z,float height)
        {
            var foot=new Vector3(x,Walk,z);
            var steel=Batch(batches,Finish.Steel);
            steel.Box(foot+Vector3.up*.16f,new Vector3(.46f,.32f,.46f));
            steel.Tube(foot,foot+Vector3.up*height,.039f,6);
        }

        private static void Tree(Dictionary<Finish,MeshBatch> batches,float x,float z,float height,float radius)
        {
            var foot=new Vector3(x,Play,z);
            var point=foot+new Vector3(.06f,height*.65f,.04f);
            Batch(batches,Finish.Bark).Tube(foot,point,.16f,9);
            for(int i=0;i<5;i++)
            {
                float a=i*Mathf.PI*.4f;
                Vector3 side=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                var branch=point+side*(radius*.51f);
                Batch(batches,Finish.Bark).Tube(Vector3.Lerp(foot,point,.66f),branch,.066f,6);
                Batch(batches,i%3==0?Finish.Silver:Finish.Olive)
                    .Ellipsoid(branch,new Vector3(radius*.57f,height*.26f,radius*.63f));
            }
            Batch(batches,Finish.Silver).Ellipsoid(point+Vector3.up*(height*.2f),
                new Vector3(radius*.74f,height*.30f,radius*.76f));
        }

        private static void SmallGarden(Dictionary<Finish,MeshBatch> batches,float x,float z)
        {
            Batch(batches,Finish.Steel).Ellipsoid(new Vector3(x,Walk-.17f,z),new Vector3(4.08f,.43f,1.71f),true);
            for(int i=-4;i<=4;i++)
            {
                float width=Mathf.Sqrt(1f-i*i/27f);
                Batch(batches,i%3==0 ? Finish.Olive : Finish.DarkLeaves)
                    .Ellipsoid(new Vector3(x+i*.78f,Walk+.35f,z),new Vector3(.67f,.50f,width*1.54f));
            }
        }

        private static void PottedTree(Dictionary<Finish,MeshBatch> batches,float x,float z)
        {
            var p=new Vector3(x,.16f,z);
            Batch(batches,Finish.Steel).Box(p+Vector3.up*.25f,new Vector3(.89f,.50f,.89f));
            Batch(batches,Finish.Bark).Tube(p+Vector3.up*.5f,p+Vector3.up*2.12f,.062f,7);
            for(int i=0;i<3;i++)
                Batch(batches,Finish.DarkLeaves).Ellipsoid(p+Vector3.up*(.83f+i*.43f),
                    new Vector3(.92f-i*.26f,.33f,.86f-i*.25f));
        }

        private static void Bench(Dictionary<Finish,MeshBatch> batches,float x,float z)
        {
            for(int row=-2;row<=2;row++)
                Batch(batches,Finish.Wood).Box(new Vector3(x+row*.117f,Walk+.47f,z),
                    new Vector3(.102f,.055f,2.26f));
            for(int side=-1;side<=1;side+=2)
                Batch(batches,Finish.Steel).Box(new Vector3(x,Walk+.22f,z+side*.82f),
                    new Vector3(.47f,.44f,.085f));
        }

        private static void Chaise(Dictionary<Finish,MeshBatch> batches,float x,float z,bool mirror)
        {
            const int segments = 19;
            Vector3 Surface(int i,float dx)
            {
                float t=i/(float)segments;
                float h=.39f+.58f*Mathf.Exp(-56f*(t-.19f)*(t-.19f))-.19f*t;
                return new Vector3(x+dx,Play+h,z+(mirror?-1f:1f)*(t-.5f)*2.4f);
            }
            for(int i=1;i<=segments;i++)
            {
                var a=Surface(i-1,-.51f); var b=Surface(i-1,.51f);
                var c=Surface(i,.51f); var d=Surface(i,-.51f);
                Batch(batches,Finish.Wood).Quad(a,b,Vector3.Lerp(b,c,.79f),Vector3.Lerp(a,d,.79f),true);
                Batch(batches,Finish.Steel).Tube(a,d,.031f,5);
                Batch(batches,Finish.Steel).Tube(b,c,.031f,5);
            }
            for(int side=-1;side<=1;side+=2)
            for(int end=-1;end<=1;end+=2)
                Batch(batches,Finish.Steel).Tube(new Vector3(x+side*.48f,Play,z+end*.9f),
                    Surface(end==(mirror?-1:1)?17:3,side*.48f),.032f,5);
        }

        private static void Slide(Dictionary<Finish,MeshBatch> batches,float x,float z)
        {
            var blue=Batch(batches,Finish.Blue);
            var yellow=Batch(batches,Finish.Yellow);
            const float length=2.04f;
            for(int side=-1;side<=1;side+=2)
            {
                float px=x+side*.34f;
                blue.Tube(new Vector3(px,Play,z-1.19f),new Vector3(px,Play+1.4f,z),.056f,7);
                blue.Tube(new Vector3(px,Play,z+.10f),new Vector3(px,Play+1.62f,z+.10f),.078f,7);
                yellow.Tube(new Vector3(px,Play+1.06f,z+.13f),new Vector3(px,Play+1.67f,z+.13f),.067f,7);
            }
            for(int i=1;i<6;i++)
            {
                float t=i/6f;
                blue.Tube(new Vector3(x-.335f,Play+1.4f*t,z-1.19f*(1f-t)),
                    new Vector3(x+.335f,Play+1.4f*t,z-1.19f*(1f-t)),.043f,7);
            }
            Vector3 Edge(float t,float s)
            {
                float height=1.40f-1.24f*(t*t*(3f-2f*t));
                return new Vector3(x+s*.33f,Play+height,z+.12f+t*length);
            }
            for(int i=0;i<12;i++)
            {
                float a=i/12f,b=(i+1)/12f;
                blue.Quad(Edge(a,-1),Edge(a,1),Edge(b,1),Edge(b,-1),true);
                for(int side=-1;side<=1;side+=2)
                {
                    Vector3 p=Edge(a,side),q=Edge(b,side);
                    yellow.Quad(p,q,q+Vector3.up*.127f,p+Vector3.up*.127f,true);
                }
            }
            Batch(batches,Finish.Red).Ellipsoid(new Vector3(x,.95f,z+.08f),new Vector3(.2f,.4f,.22f));
        }

        private static void PlaySculpture(Dictionary<Finish,MeshBatch> batches,float x,float z)
        {
            var aqua=Batch(batches,Finish.Aqua);
            Vector3 p=new Vector3(x,Play,z);
            aqua.Ellipsoid(p+new Vector3(0,.77f,0),new Vector3(.43f,.47f,.56f));
            aqua.Ellipsoid(p+new Vector3(.08f,1.13f,.48f),new Vector3(.35f,.37f,.33f));
            aqua.Ellipsoid(p+new Vector3(.16f,.94f,.72f),new Vector3(.14f,.34f,.17f));
            for(int side=-1;side<=1;side+=2)
            for(int end=-1;end<=1;end+=2)
                aqua.Tube(p+new Vector3(side*.24f,.03f,end*.33f),
                    p+new Vector3(side*.25f,.70f,end*.33f),.155f,10);
        }

        private static MeshBatch Batch(Dictionary<Finish,MeshBatch> batches,Finish finish)
        {
            if(!batches.TryGetValue(finish,out MeshBatch batch))
                batches.Add(finish,batch=new MeshBatch());
            return batch;
        }

        private static void Publish(Transform root,Vector3 origin,Vector3 across,Vector3 away,Finish style,MeshBatch batch)
        {
            var label="МЕГА · площадь · "+style;
            for(int i=0;i<batch.Vertices.Count;i++)
            {
                Vector3 p=batch.Vertices[i];
                batch.Vertices[i]=origin+across*p.x+Vector3.up*p.y+away*p.z;
            }
            var mesh=new Mesh {name=label};
            mesh.SetVertices(batch.Vertices);
            mesh.SetUVs(0,batch.UV);
            mesh.SetTriangles(batch.Triangles,0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            var obj=new GameObject(label,typeof(MeshFilter),typeof(MeshRenderer));
            obj.transform.SetParent(root,false);
            obj.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=MaterialFor(style);
            renderer.shadowCastingMode=style<Finish.Stone || style==Finish.AsphaltBorder
                ? ShadowCastingMode.Off : ShadowCastingMode.On;
            mesh.UploadMeshData(true);
        }

        private static Material MaterialFor(Finish finish)
        {
            int index=(int)finish;
            if(Materials.TryGetValue(index,out Material found)&&found)
                return found;
            var m=new Material(ActorArt.Paint(Palette[index],finish==Finish.Steel?.30f:.19f));
            switch(finish)
            {
                case Finish.Paving: StreetMaterials.Apply(m,"ConcretePavers",.38f,.38f); break;
                case Finish.Road: StreetMaterials.Apply(m,"Asphalt",.40f,.40f); break;
                case Finish.Sand: StreetMaterials.Apply(m,"WeatheredConcrete",.60f,.60f); break;
                case Finish.Lawn: StreetMaterials.Apply(m,"Grass",.25f,.25f); break;
                case Finish.Olive:
                case Finish.Silver:
                case Finish.DarkLeaves: StreetMaterials.Apply(m,"Foliage",.66f,.66f); break;
            }
            Materials.Add(index,m);
            return m;
        }

        private sealed class MeshBatch
        {
            internal readonly List<Vector3> Vertices=new List<Vector3>();
            internal readonly List<Vector2> UV=new List<Vector2>();
            internal readonly List<int> Triangles=new List<int>();

            private int Vertex(Vector3 point,Vector2 texture)
            {
                int index=Vertices.Count;
                Vertices.Add(point);
                UV.Add(texture);
                return index;
            }

            internal void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,bool both=false)
            {
                Vector3 normal=Vector3.Cross(b-a,c-a);
                Vector2 Tex(Vector3 p) => Mathf.Abs(normal.y)>.02f
                    ? new Vector2(p.x,p.z)
                    : Mathf.Abs(normal.x)>Mathf.Abs(normal.z) ? new Vector2(p.z,p.y) : new Vector2(p.x,p.y);
                int i=Vertex(a,Tex(a));
                Vertex(b,Tex(b)); Vertex(c,Tex(c)); Vertex(d,Tex(d));
                Triangles.Add(i);Triangles.Add(i+1);Triangles.Add(i+2);
                Triangles.Add(i);Triangles.Add(i+2);Triangles.Add(i+3);
                if(!both) return;
                Quad(d,c,b,a);
            }

            internal void Box(Vector3 center,Vector3 size)
            {
                var e=size*.5f;
                Vector3 a=center+new Vector3(-e.x,-e.y,-e.z);
                Vector3 b=center+new Vector3(e.x,-e.y,-e.z);
                Vector3 c=center+new Vector3(e.x,-e.y,e.z);
                Vector3 d=center+new Vector3(-e.x,-e.y,e.z);
                Vector3 up=Vector3.up*size.y;
                Quad(a+up,b+up,c+up,d+up); Quad(d,c,b,a);
                Quad(a,b,b+up,a+up); Quad(c,d,d+up,c+up);
                Quad(b,c,c+up,b+up); Quad(d,a,a+up,d+up);
            }

            internal void Tube(Vector3 a,Vector3 b,float radius,int sides)
            {
                Vector3 axis=(b-a).normalized;
                if(axis.sqrMagnitude<.1f) return;
                Vector3 across=Vector3.Cross(axis,Mathf.Abs(axis.y)>.89f?Vector3.forward:Vector3.up).normalized;
                Vector3 side=Vector3.Cross(axis,across);
                Vector3 P(int i)
                {
                    float angle=2f*Mathf.PI*i/sides;
                    return radius*(across*Mathf.Cos(angle)+side*Mathf.Sin(angle));
                }
                for(int i=0;i<sides;i++)
                {
                    Vector3 p=P(i),q=P(i+1);
                    Quad(a+q,a+p,b+p,b+q);
                }
            }

            internal void Ellipsoid(Vector3 origin,Vector3 radii,bool half=false)
            {
                const int sides=14,rows=6;
                int first=Vertices.Count;
                for(int row=0;row<=rows;row++)
                {
                    float angle=(half ? .5f : 1f)*Mathf.PI*row/rows;
                    for(int section=0;section<=sides;section++)
                    {
                        float theta=Mathf.PI*2*section/sides;
                        var p=new Vector3(radii.x*Mathf.Sin(angle)*Mathf.Cos(theta),
                            radii.y*Mathf.Cos(angle),radii.z*Mathf.Sin(angle)*Mathf.Sin(theta));
                        Vertex(origin+p,new Vector2(section/7f,row*.30f));
                    }
                }
                for(int row=0;row<rows;row++)
                for(int section=0;section<sides;section++)
                {
                    int a=first+row*(sides+1)+section,b=a+1,c=a+sides+1,d=c+1;
                    // The forecourt coordinate frame reflects the parametric rings.
                    if(row>0){Triangles.Add(a);Triangles.Add(c);Triangles.Add(b);}
                    if(half||row<rows-1){Triangles.Add(b);Triangles.Add(c);Triangles.Add(d);}
                }
            }

            internal void DoubleTriangle(Vector3 a,Vector3 b,Vector3 c)
            {
                int i=Vertex(a,new Vector2(0,0)); Vertex(b,new Vector2(1,0)); Vertex(c,new Vector2(.5f,1));
                int back=Vertex(a,new Vector2(0,0)); Vertex(b,new Vector2(1,0)); Vertex(c,new Vector2(.5f,1));
                Triangles.Add(i);Triangles.Add(i+1);Triangles.Add(i+2);
                Triangles.Add(back);Triangles.Add(back+2);Triangles.Add(back+1);
            }
        }
    }
}
