using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraftRP
{
    internal sealed class StreetScenery
    {
        private readonly Transform parent;
        private readonly Dictionary<string,Material> materials;
        private readonly Dictionary<string,Geometry> groups = new Dictionary<string,Geometry>();
        private readonly System.Random variation = new System.Random(24851);

        internal StreetScenery(Transform parent,Dictionary<string,Material> materials)
        {
            this.parent=parent;
            this.materials=materials;
        }

        internal void Build()
        {
            // A paved route and greenery on the way from spawn to the warehouse.
            Box("yardGrass",-5,.010f,-5,34,.018f,42);
            Box("yardGrass",-56,.010f,-16,33,.018f,16);
            Box("yardGrass",64,.010f,-9,13,.018f,16);
            Box("yardGrass",-7,.010f,-55,68,.018f,16);
            Pavement(-27,.119f,-14.5f,3.4f,.14f,26);
            Pavement(-4,.097f,-14,3.5f,.17f,27);
            Pavement(-15.6f,.099f,-6.6f,23,.16f,2.8f);
            Pavement(35,.131f,-22.7f,17,.18f,3);
            Pavement(63,.117f,19.7f,19,.17f,2.3f);

            // Curbs stop at the cross street, leaving the driving area clear.
            for(int side=-1;side<=1;side+=2)
            {
                float z=-36+side*7f;
                Box("border",-51,.203f,z,126,.132f,.19f);
                Box("border",68,.203f,z,92,.132f,.19f);
            }

            for (int i=0;i<4;i++)
            {
                float x=-104+i*17.5f;
                Facade(x,-74,14.5f,11.5f,3+i%3,i);
                Box("paving",x,.013f,-60,2.2f,.020f,17);
                Facade(69+i*14f,15,11.2f,9.6f,3+i%2,i+2);
            }
            Facade(-87,10,17,11,5,2);
            Facade(-66,16,17,11,4,4);

            const float west=-119f;
            for (int i=0;i<15;i++)
            {
                float x=west+i*15.5f;
                if(x < -34 || x>24)
                    AddTree(x,-23.7f,1.0f + (float)variation.NextDouble()*.37f);
                if(x < 26 || x>90)
                    AddTree(x,-48.5f,.8f+(float)variation.NextDouble()*.36f);
            }
            foreach (Vector2 p in new[]{
                new Vector2(-17,-20), new Vector2(-14,-10), new Vector2(0,-18), new Vector2(6,-13),
                new Vector2(8,4), new Vector2(-10,9), new Vector2(-59,-13), new Vector2(-67,-13),
                new Vector2(35,13), new Vector2(44,17), new Vector2(63,22), new Vector2(-28,-54),
            })
                AddTree(p.x,p.y,.86f+(float)variation.NextDouble()*.35f);

            for(int i=0;i<22;i++)
            {
                float x=-19.8f+i*1.1f;
                if (Mathf.Abs(x+4)<2.7f)
                    continue;
                float z=-22.3f+Mathf.Sin(i*.52f)*.52f;
                Crown(i%3==0?"leavesMoss":"leavesShade",new Vector3(x,.48f,z),
                      new Vector3(.65f,.49f,.57f),5,9);
            }

            for(int i=0;i<8;i++)
                StreetLamp(-101+i*27.7f,-26.4f);
            BusShelter(-15.3f,-26.3f);
            Bench(-9,-20.6f);
            Bench(-21,-19.5f);
            Bench(-60,-17.4f);
            for(int i=0;i<4;i++)
                Bench(32+i*10,55);
            foreach (Vector2 p in new[] {new Vector2(-12,-26.4f),new Vector2(-66,-25f),
                     new Vector2(22,-26.1f),new Vector2(41,-24f)})
                Box("railMetal",p.x,.38f,p.y,.38f,.67f,.32f);

            // Drains and subtle asphalt patches break up the distant road surface.
            for (int i=0;i<9;i++)
            {
                float x=-102+i*24;
                if(Mathf.Abs(x-17)<9f) continue;
                for(int slat=0;slat<6;slat++)
                    Box("railMetal",x+slat*.072f,.183f,-42.4f,.035f,.008f,.23f);
                Box("roadPatch",x+2.5f,.1815f,-39+(i%2)*5.4f,1.6f+(i%4)*.74f,.005f,
                    .6f+(i%3)*.39f);
            }

            foreach(var pair in groups)
                Publish(pair.Key,pair.Value);
        }

        private void Facade(float x,float z,float width,float depth,int floors,int style)
        {
            float height = floors*2.73f+.55f;
            string shell = style%3==1 ? "brickWall" : style%3==2 ? "panelSand" : "panelIvory";
            Box(shell,x,height*.5f,z,width,height,depth);
            Box("border",x,.35f,z,width+.16f,.7f,depth+.16f);
            Box("eave",x,height+.06f,z,width+.28f,.24f,depth+.27f);
            Box("roof",x+width*.22f,height+.66f,z-1.7f,2.15f,1.1f,2.6f);
            Box("eave",x+width*.22f,height+1.25f,z-1.7f,2.24f,.17f,2.72f);
            AddCollision(x,height*.5f,z,width,height,depth);
            int bays=Mathf.FloorToInt(width/2.42f);
            for(int facing=-1;facing<=1;facing+=2)
            {
                float face=z+depth*.5f*facing;
                for(int floor=0;floor<floors;floor++)
                {
                    float y=1.64f+floor*2.73f;
                    Box("facadeBand",x,y+.97f,face+facing*.025f,width,.038f,.07f);
                    for(int b=0;b<bays;b++)
                    {
                        float xx=x-width*.5f+(b+.5f)*width/bays;
                        bool entrance=floor==0 && b==bays/2;
                        if(entrance)
                        {
                            Box("railMetal",xx,1.21f,face+facing*.05f,1.47f,2.38f,.10f);
                            Box("glassDeep",xx,1.59f,face+facing*.11f,.99f,.71f,.035f);
                            Box("eave",xx,2.6f,face+facing*.51f,2.23f,.14f,1.1f);
                            continue;
                        }
                        string glass=(b+floor*3+style)%6==0?"glassWarm":
                            (b+style+floor)%2==0?"glassBlue":"glassDeep";
                        Box("railMetal",xx,y,face+facing*.061f,1.38f,1.39f,.099f);
                        Box(glass,xx,y,face+facing*.122f,1.22f,1.22f,.028f);
                        Box("windowFrame",xx,y,face+facing*.146f,.042f,1.22f,.029f);
                        Box("windowFrame",xx,y-.68f,face+facing*.194f,1.55f,.089f,.38f);
                        if(floor>0 && (style+b)%4==0)
                            Balcony(xx,y,face,facing);
                    }
                }
            }
            for(int facing=-1;facing<=1;facing+=2)
            for(int floor=1;floor<floors;floor++)
            {
                float xpos=x+facing*(width*.5f+.041f),y=1.64f+floor*2.73f;
                for(int b=-1;b<=1;b++)
                {
                    float zz=z+b*2.62f;
                    Box("railMetal",xpos,y,zz,.105f,1.27f,1.20f);
                    Box((b+floor)%2==0?"glassBlue":"glassDeep",xpos+facing*.067f,y,zz,
                        .024f,1.12f,1.04f);
                    Box("windowFrame",xpos+facing*.094f,y-.62f,zz,.26f,.073f,1.32f);
                }
            }
        }

        private void Balcony(float x,float y,float z,int side)
        {
            Box("border",x,y-.78f,z+side*.55f,2.01f,.145f,1.08f);
            Box("railMetal",x,y+.11f,z+side*1.06f,1.94f,.057f,.064f);
            Box("balconyPanel",x,y-.38f,z+side*1.04f,1.85f,.62f,.065f);
            for(int i=-1;i<=1;i+=2)
                Box("railMetal",x+i*.95f,y-.32f,z+side*1.04f,.052f,.83f,.052f);
        }

        private void BusShelter(float x,float z)
        {
            Box("paving",x,.15f,z,5.45f,.12f,2.1f);
            Box("eave",x,2.63f,z,4.9f,.17f,1.74f);
            Box("balconyPanel",x,1.54f,z+.69f,4.67f,1.57f,.05f);
            for(int i=-1;i<=1;i++)
                Box("railMetal",x+i*2.26f,1.29f,z+.65f,.065f,2.51f,.081f);
            Box("railMetal",x,2.31f,z+.67f,4.58f,.083f,.11f);
            Box("railMetal",x,.81f,z+.68f,4.58f,.071f,.103f);
            Box("trunk",x,.54f,z+.22f,3.38f,.12f,.42f);
            for(int i=-1;i<=1;i+=2)
                Box("railMetal",x+i*1.35f,.28f,z+.23f,.10f,.42f,.30f);
            Box("navy",x-2.34f,2.81f,z-.08f,.44f,.35f,.045f);
            Box("windowFrame",x-2.34f,2.81f,z-.109f,.22f,.13f,.015f);
        }

        private void Bench(float x,float z)
        {
            for(int slat=0;slat<4;slat++)
            {
                Box("trunk",x,.51f,z+slat*.095f,1.9f,.058f,.076f);
                Box("trunk",x,.65f+slat*.081f,z+.35f,1.9f,.064f,.047f);
            }
            for(int i=-1;i<=1;i+=2)
            {
                Box("railMetal",x+i*.68f,.285f,z+.12f,.075f,.46f,.29f);
                Box("railMetal",x+i*.68f,.67f,z+.34f,.075f,.49f,.055f);
            }
        }

        private void AddTree(float x,float z,float s)
        {
            Vector3 bottom=new Vector3(x,.1f,z);
            Vector3 joint=bottom+new Vector3(.08f,3.36f*s,.08f);
            Tube("bark",bottom,joint,.17f*s,.073f*s);
            Tube("bark",joint,joint+new Vector3(.12f,1.40f,.04f)*s,.073f*s,.024f*s);
            for(int i=0;i<7;i++)
            {
                float a=i*2.4f+(float)variation.NextDouble()*.9f;
                Vector3 branch=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                float r=(.75f+(float)variation.NextDouble()*.6f)*s;
                Vector3 branchStart=joint-Vector3.up*(.26f+(i%3)*.23f)*s;
                Vector3 crown=joint+branch*r+Vector3.up*(.27f+i*.23f)*s;
                Tube("bark",branchStart,crown,.059f*s,.016f*s);
                Crown(i%3==0?"leavesShade":i%3==1?"leavesMoss":"leavesTop",crown,
                      new Vector3(1.17f,1.31f,1.12f)*s,6,11);
                Crown(i%2==0?"leavesMoss":"leavesTop",crown+branch*.62f*s+Vector3.up*.33f*s,
                      new Vector3(.69f,.81f,.65f)*s,5,9);
            }
            Crown("leavesTop",joint+Vector3.up*2.02f*s,new Vector3(1.03f,1.23f,.99f)*s,6,11);
        }

        private void Crown(string style,Vector3 center,Vector3 radii,int rows,int sides)
        {
            Geometry g=Get(style);
            int offset=g.Points.Count;
            float seed=(float)variation.NextDouble()*6f;
            for(int row=0;row<=rows;row++)
            {
                float latitude=Mathf.PI*row/rows;
                float r=Mathf.Sin(latitude),h=-Mathf.Cos(latitude);
                for(int i=0;i<=sides;i++)
                {
                    float a=Mathf.PI*2f*i/sides;
                    float variation=1f+.17f*Mathf.Sin(a*4+row*1.5f+seed)
                        +.085f*Mathf.Cos(a*7-row*1.43f-seed);
                    var v=new Vector3(Mathf.Cos(a)*r*radii.x,h*radii.y,Mathf.Sin(a)*r*radii.z)*variation;
                    g.Points.Add(center+v);
                    g.UV.Add(new Vector2(i/(float)sides,row/(float)rows));
                    if (row==0 || i==sides) continue;
                    int p=offset+(row-1)*(sides+1)+i;
                    g.Face(p,p+sides+1,p+sides+2,p+1);
                }
            }
        }

        private void Tube(string style,Vector3 start,Vector3 end,float bottom,float top)
        {
            Geometry g=Get(style);
            Vector3 axis=(end-start).normalized;
            Vector3 radial=Vector3.Cross(axis,Mathf.Abs(axis.y)>.92f?Vector3.forward:Vector3.up).normalized;
            Vector3 tangent=Vector3.Cross(radial,axis);
            const int sides=8;
            int offset=g.Points.Count;
            for(int j=0;j<2;j++)
            for(int i=0;i<=sides;i++)
            {
                float angle=2f*Mathf.PI*i/sides;
                g.Points.Add((j==0?start:end)+(radial*Mathf.Cos(angle)+tangent*Mathf.Sin(angle))*(j==0?bottom:top));
                g.UV.Add(new Vector2(i/(float)sides,j));
                if(j==0||i==sides) continue;
                int p=offset+i;
                g.Face(p,p+sides+1,p+sides+2,p+1);
            }
        }

        private void StreetLamp(float x,float z)
        {
            var foot=new Vector3(x,.25f,z);
            var arm=new Vector3(x,5.2f,z-.15f);
            var head=arm+new Vector3(.04f,.52f,-1.35f);
            Tube("railMetal",foot,arm,.095f,.050f);
            Tube("railMetal",arm,head,.050f,.039f);
            Box("railMetal",x+.04f,head.y,head.z-.14f,.29f,.12f,.65f);
            Box("windowFrame",x+.04f,head.y-.064f,head.z-.17f,.24f,.022f,.48f);
        }

        private void Pavement(float x,float y,float z,float w,float h,float d)
        {
            Box("paving",x,y,z,w,h,d);
            AddCollision(x,y,z,w,h,d,"Коллизия · тротуар");
        }

        private void AddCollision(float x,float y,float z,float w,float h,float d,string name="Коллизия · жилой дом")
        {
            var obj=new GameObject(name,typeof(BoxCollider));
            obj.transform.SetParent(parent,false);
            obj.transform.localPosition=new Vector3(x,y,z);
            obj.GetComponent<BoxCollider>().size=new Vector3(w,h,d);
        }

        private void Box(string style,float x,float y,float z,float w,float h,float d)
        {
            Geometry g=Get(style);
            var p=new Vector3(x,y,z);
            var a=p+new Vector3(-w,-h,-d)*.5f;
            var b=p+new Vector3(w,-h,-d)*.5f;
            var c=p+new Vector3(w,-h,d)*.5f;
            var e=p+new Vector3(-w,-h,d)*.5f;
            var t=Vector3.up*h;
            g.Quad(a,b,c,e);
            g.Quad(a+t,e+t,c+t,b+t);
            g.Quad(a,a+t,b+t,b);
            g.Quad(e,c,c+t,e+t);
            g.Quad(a,e,e+t,a+t);
            g.Quad(b,b+t,c+t,c);
        }

        private Geometry Get(string style)
        {
            if(!groups.TryGetValue(style,out var group))
                groups.Add(style,group=new Geometry());
            return group;
        }

        private void Publish(string style,Geometry geometry)
        {
            var obj=new GameObject("Улица · "+style,typeof(MeshFilter),typeof(MeshRenderer));
            obj.transform.SetParent(parent,false);
            var mesh=new Mesh {name=obj.name};
            if(geometry.Points.Count>65535)
                mesh.indexFormat=IndexFormat.UInt32;
            mesh.SetVertices(geometry.Points);
            mesh.SetUVs(0,geometry.UV);
            mesh.SetTriangles(geometry.Indices,0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            obj.GetComponent<MeshFilter>().sharedMesh=mesh;
            obj.GetComponent<MeshRenderer>().sharedMaterial=materials[style];
        }

        private sealed class Geometry
        {
            internal readonly List<Vector3> Points = new List<Vector3>();
            internal readonly List<Vector2> UV = new List<Vector2>();
            internal readonly List<int> Indices = new List<int>();

            internal void Face(int a,int b,int c,int d)
            {
                Indices.Add(a);Indices.Add(b);Indices.Add(c);
                Indices.Add(a);Indices.Add(c);Indices.Add(d);
            }

            internal void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int start=Points.Count;
                Points.Add(a);Points.Add(b);Points.Add(c);Points.Add(d);
                float u=Vector3.Distance(a,b)*.38f, v=Vector3.Distance(a,d)*.38f;
                UV.Add(Vector2.zero);UV.Add(new Vector2(u,0));
                UV.Add(new Vector2(u,v));UV.Add(new Vector2(0,v));
                Face(start,start+1,start+2,start+3);
            }
        }
    }
}
