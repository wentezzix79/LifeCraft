using System;
using System.Collections.Generic;
using LifeCraftRP;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraft.World
{
    /// <summary>Procedural meshes laid over the real footpaths and water bodies of the park.</summary>
    public sealed class OmskPark : MonoBehaviour
    {
        private static readonly Vector3 GateCenter = new Vector3(1263.4f,0,-17.7f);
        private static readonly Vector3 GateForward = new Vector3(.9944f,0,.1057f);
        private const string LayoutPath = "Omsk/ParkLayout";
        private const float PavementHeight = .074f;
        private const float TreeCellSize = 220f;

        private readonly Dictionary<string,Material> materials = new Dictionary<string,Material>();
        private readonly Dictionary<(string Style,int X,int Z),Geometry> batches =
            new Dictionary<(string Style,int X,int Z),Geometry>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<(MeshRenderer Renderer,Bounds Bounds)> vegetation =
            new List<(MeshRenderer,Bounds)>();
        private float refreshAt;
        private bool lowDetail;
        private Font lettering;
        private Vector3 origin;

        [Serializable]
        private sealed class Layout
        {
            public Route[] routes;
            public Shore[] water;
            public float[] bridge;
            public float[] trees;
        }

        [Serializable]
        private sealed class Route
        {
            public float[] xz;
            public float width;
            public int style;
            public bool lit;
        }

        [Serializable]
        private sealed class Shore
        {
            public string id;
            public float[] xz;
        }

        internal static OmskPark Build(Transform root)
        {
            var park = root.gameObject.AddComponent<OmskPark>();
            park.origin = root.position;
            park.Compose();
            return park;
        }

        internal void RefreshVegetation(Vector3 position,bool low)
        {
            if (Time.unscaledTime < refreshAt && low == lowDetail)
                return;
            lowDetail = low;
            refreshAt = Time.unscaledTime+.27f;
            float distance = low ? 430f : 860f;
            foreach (var cell in vegetation)
            {
                float distanceSquared = cell.Bounds.SqrDistance(position);
                cell.Renderer.enabled = distanceSquared < distance*distance;
                cell.Renderer.shadowCastingMode = !low && distanceSquared < 145f*145f
                    ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        private void Compose()
        {
            CreateMaterials();
            lettering = Resources.Load<Font>("Art/DejaVuSans");
            MainEntrance();
            var asset = Resources.Load<TextAsset>(LayoutPath);
            if (asset != null)
            {
                var data = JsonUtility.FromJson<Layout>(asset.text);
                DrawRoutes(data.routes);
                foreach (Shore shore in data.water)
                    Waterfront(shore);
                Footbridge(data.bridge);
                for (int i=0;i<data.trees.Length;i+=4)
                {
                    Vector3 point=new Vector3(data.trees[i],0,data.trees[i+1]);
                    if (Application.isMobilePlatform && i%8!=0 && (point-GateCenter).sqrMagnitude>40000f)
                        continue;
                    Tree(point,data.trees[i+2],(int)data.trees[i+3],i);
                }
            }
            GardensAndFountains();
            foreach (var pair in batches)
                Publish(pair.Key.Style,pair.Key.X,pair.Key.Z,pair.Value);
            batches.Clear();
        }

        private void CreateMaterials()
        {
            AddMaterial("paver",0xBDB4AA,"ConcretePavers",.2f);
            AddMaterial("tracing",0xC4B4A0,"WeatheredConcrete",.65f);
            AddMaterial("asphalt",0xAAA8A1,"Asphalt",.23f);
            AddMaterial("cycling",0x9D7770,"Asphalt",.28f);
            AddMaterial("stone",0xC7BFB1,"WeatheredConcrete",.45f);
            AddMaterial("soil",0x596044,"Grass",.55f);
            AddMaterial("plank",0x78604B,null,1,.19f);
            AddMaterial("metal",0x383D3D,null,1,.33f);
            AddMaterial("gate",0xE1E0D7,null,1,.29f);
            AddMaterial("green",0x254A36,null,1,.22f);
            AddMaterial("glass",0xC9DBD7,null,1,.48f);
            AddMaterial("water",0x526D69,null,1,.52f);
            AddMaterial("waterRipple",0x618380,null,1,.56f);
            AddMaterial("jet",0xA3C5CB,null,1,.56f);
            AddMaterial("fountainCoping",0xC3C0B8,"WeatheredConcrete",.34f,.31f);
            AddMaterial("fountainBelt",0x686B66,"WeatheredConcrete",.5f,.20f);
            AddMaterial("boothPanel",0xCED3C9,null,1,.29f);
            AddMaterial("boothScreen",0x2E5963,null,1,.60f);
            AddMaterial("birch",0xC6C0A9,"WeatheredConcrete",.32f);
            AddMaterial("bark",0x65584A,null,1,.11f);
            AddMaterial("foliage",0x526640,"Foliage",.23f);
            AddMaterial("leafLight",0x67804E,"Foliage",.29f);
            AddMaterial("pine",0x3D593C,"Foliage",.24f);
            AddMaterial("reeds",0x7B854A,"Foliage",.46f);
            AddMaterial("petalGold",0xD28B29,null,1,.16f);
            AddMaterial("petalRed",0xB74E50,null,1,.16f);
            AddMaterial("petalPink",0xBA6C93,null,1,.19f);
        }

        private void AddMaterial(string style,int rgb,string texture,float tile,float gloss=.12f)
        {
            Color c=new Color32((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb,255);
            var material = new Material(RuntimeShaders.Lit()) { name="Омск · ПКиО · "+style,color=c };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor",c);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness",gloss);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness",gloss);
            if (texture!=null)
                StreetMaterials.Apply(material,texture,tile,tile);
            materials.Add(style,material);
        }

        private void MainEntrance()
        {
            Basis gate = new Basis(GateCenter,GateForward);
            Box("paver",gate,new Vector3(0,.034f,17.2f),new Vector3(22.6f,.068f,46f));
            for (int side=-1;side<=1;side+=2)
            {
                float edge=side*7.1f;
                Box("stone",gate,new Vector3(edge,.24f,0),new Vector3(1.35f,.48f,1.28f));
                Collision("Опора главного входа",gate.At(edge,3.85f,0),gate.Forward,new Vector3(1.05f,7.7f,1.12f));
                for(int column=-1;column<=1;column+=2)
                {
                    float x=edge+.41f*column;
                    Box("gate",gate,new Vector3(x,4.03f,-.41f),new Vector3(.16f,7.66f,.15f));
                    Box("gate",gate,new Vector3(x,4.03f,.41f),new Vector3(.16f,7.66f,.15f));
                }
                for(int step=0;step<11;step++)
                {
                    float y=.48f+.66f*step;
                    Box("gate",gate,new Vector3(edge,y,-.41f),new Vector3(.83f,.050f,.10f));
                    Box("gate",gate,new Vector3(edge,y,.41f),new Vector3(.83f,.050f,.10f));
                    Tube("gate",gate.At(edge-.40f,y,-.40f),gate.At(edge+.40f,y+.63f,-.40f),.027f,.027f,4);
                    Tube("gate",gate.At(edge+.40f,y,-.40f),gate.At(edge+.40f,y+.63f,.40f),.025f,.025f,4);
                }
                Box("metal",gate,new Vector3(side*4.59f,3.11f,.16f),new Vector3(4.84f,.41f,.89f));
                Box("green",gate,new Vector3(side*4.59f,3.06f,-.302f),new Vector3(4.68f,.31f,.05f));
                Text("ДОБРО ПОЖАЛОВАТЬ",gate.At(side*4.59f,3.06f,-.337f),gate.Forward,.19f,Color.white);
                for(int picket=0;picket<21;picket++)
                {
                    float x=side*(8.2f+picket*.29f);
                    Box("green",gate,new Vector3(x,.93f,.44f),new Vector3(.039f,1.73f,.048f));
                }
                Box("green",gate,new Vector3(side*11.1f,1.48f,.46f),new Vector3(6.03f,.072f,.061f));
                Box("green",gate,new Vector3(side*11.1f,.48f,.46f),new Vector3(6.03f,.072f,.061f));
                Bench(gate.At(side*8.6f,0,10.8f),gate.Forward);
                Bench(gate.At(side*8.6f,0,18.8f),gate.Forward);
                Lantern(gate.At(side*9.8f,0,4.8f));
                Lantern(gate.At(side*10.2f,0,26.2f));
                Bed(gate.At(side*9.5f,0,32.5f),2.9f,side+13);
            }
            Box("gate",gate,new Vector3(0,7.83f,0),new Vector3(15.3f,.19f,1.10f));
            Box("stone",gate,new Vector3(0,6.92f,-.24f),new Vector3(11.4f,1.70f,.43f));
            Box("gate",gate,new Vector3(0,6.92f,-.47f),new Vector3(11.14f,1.53f,.046f));
            var historicalGreen=new Color(.105f,.32f,.21f);
            Text("ПАРК КУЛЬТУРЫ И ОТДЫХА",gate.At(0,7.15f,-.510f),gate.Forward,.51f,historicalGreen);
            Text("им. 30-летия ВЛКСМ",gate.At(0,6.58f,-.510f),gate.Forward,.39f,historicalGreen);
            Signpost(gate.At(-8.75f,0,29.2f),gate.Forward,"ЦЕНТРАЛЬНАЯ АЛЛЕЯ","ОЗЁРА  •  ПРОГУЛОЧНЫЙ МАРШРУТ");
            InstantPhotoBooth(gate.At(-9.55f,0,13.25f),-gate.Forward);
        }

        private void InstantPhotoBooth(Vector3 point,Vector3 forward)
        {
            // The autumn 2026 installation stands near the central entrance.
            var frame = new Basis(point,forward);
            Box("stone",frame,new Vector3(0,.045f,0),new Vector3(1.60f,.09f,1.44f));
            Box("boothPanel",frame,new Vector3(0,1.09f,0),new Vector3(1.07f,2.08f,.72f));
            Box("metal",frame,new Vector3(0,.18f,0),new Vector3(1.12f,.19f,.76f));
            Box("green",frame,new Vector3(0,1.975f,-.372f),new Vector3(1.03f,.24f,.08f));
            Text("ФОТО",frame.At(0,1.98f,-.415f),forward,.21f,Color.white);
            Box("metal",frame,new Vector3(0,1.45f,-.370f),new Vector3(.69f,.84f,.08f));
            Box("boothScreen",frame,new Vector3(0,1.40f,-.415f),new Vector3(.58f,.56f,.024f));
            Text("Моментальное фото",frame.At(0,1.46f,-.429f),forward,.060f,Color.white);
            Box("glass",frame,new Vector3(0,1.77f,-.419f),new Vector3(.065f,.062f,.033f));
            Box("metal",frame,new Vector3(0,.82f,-.378f),new Vector3(.51f,.065f,.055f));
            Box("green",frame,new Vector3(0,.53f,-.371f),new Vector3(.76f,.23f,.035f));
            Text("ГОРОДСКИЕ ПАРКИ",frame.At(0,.53f,-.393f),forward,.09f,Color.white);
            Collision("Фотобудка у центрального входа",frame.At(0,1.09f,0),forward,new Vector3(1.07f,2.08f,.72f));
        }

        private void DrawRoutes(Route[] routes)
        {
            for (int index=0;index<routes.Length;index++)
            {
                Route route=routes[index];
                // Keep overlapping OSM paths and the entrance plaza on distinct planes.
                var points=Positions(route.xz,PavementHeight+.016f*route.style+.00011f*index);
                string surface=route.style==0 ? "paver" : route.style==1 ? "asphalt" : "cycling";
                Ribbon(surface,points,route.width);
                if (!route.lit)
                    continue;
                float covered=0f,next=28f;
                for(int i=0;i<points.Length-1;i++)
                {
                    Vector3 direction=points[i+1]-points[i];
                    float length=direction.magnitude;
                    if(length<.1f)
                        continue;
                    direction/=length;
                    Vector3 edge=Vector3.Cross(Vector3.up,direction)*(route.width*.5f+1.25f);
                    for (;next<covered+length;next+=35f)
                    {
                        float fraction=(next-covered)/length;
                        if(fraction<.06f || fraction>.94f)
                            continue;
                        Vector3 position=Vector3.Lerp(points[i],points[i+1],fraction)+edge;
                        Lantern(position);
                        if (Mathf.RoundToInt(next/35)%2==0)
                            Bench(position+direction*2.65f,-edge.normalized);
                    }
                    covered+=length;
                }
            }
        }

        private void GardensAndFountains()
        {
            Bed(new Vector3(1313.15f,0,-14.48f),10.75f,11);
            Vector3 plaza=new Vector3(1423.16f,0,122.24f);
            Disc("paver",plaza,8.15f,48,.098f);
            Ring("tracing",plaza,7.68f,7.94f,48,.102f);
            for (int i=0;i<16;i++)
            {
                Vector3 radial=Radial(i,16);
                Vector3 hub=plaza+radial*5.4f;
                Disc("metal",hub,.105f,8,.112f);
                Spray(hub+Vector3.up*.115f,-radial*.16f,1.20f+(i%3)*.17f);
            }
            for (int i=0;i<4;i++)
            {
                Vector3 radial=Radial(i,4);
                Bench(plaza+radial*10.8f,-radial);
            }
            RebuiltFountain(new Vector3(1978.32f,0,61.38f));
        }

        private void RebuiltFountain(Vector3 basin)
        {
            // The fountain at the end of the main path reopened with a wider circular bowl.
            const int segments=64;
            Disc("paver",basin,12.65f,segments,.101f);
            Ring("tracing",basin,9.19f,9.35f,segments,.105f);
            Tube("fountainBelt",basin+Vector3.up*.08f,basin+Vector3.up*.61f,8.51f,8.51f,segments,false);
            Tube("fountainCoping",basin+Vector3.up*.61f,basin+Vector3.up*.70f,8.60f,8.64f,segments,false);
            Tube("fountainCoping",basin+Vector3.up*.48f,basin+Vector3.up*.70f,8.05f,8.05f,segments,false);
            Ring("fountainCoping",basin,8.05f,8.64f,segments,.701f);
            Disc("water",basin,8.07f,segments,.495f);
            for(int i=0;i<32;i++)
            {
                Vector3 radial=Radial(i,32), tangent=Radial(i+8,32);
                Vector3 seam=basin+radial*8.37f+Vector3.up*.703f;
                Box("fountainBelt",new Basis(seam,radial),Vector3.zero,new Vector3(.026f,.004f,.48f));
                if (i%2==0)
                {
                    Vector3 outlet=basin+radial*6.30f;
                    Disc("metal",outlet,.097f,8,.515f);
                    Spray(outlet+Vector3.up*.51f,-radial*(2.32f+(i%3)*.35f),1.65f+(i%3)*.29f);
                    Ring("waterRipple",outlet,.36f,.39f,18,.501f);
                }
                Collision("Борт фонтана",basin+radial*8.34f+Vector3.up*.37f,radial,new Vector3(1.66f,.68f,.57f));
                if (i%4==0)
                {
                    Vector3 hub=basin+radial*1.65f;
                    Spray(hub+Vector3.up*.51f,radial*.65f+tangent*.16f,2.45f+(i%3)*.34f);
                }
            }
            Ring("waterRipple",basin,7.28f,7.33f,segments,.502f);
            Ring("waterRipple",basin,6.96f,6.99f,segments,.501f);
            Spray(basin+Vector3.up*.51f,Vector3.zero,3.92f);
            for(int i=0;i<4;i++)
            {
                Vector3 radial=Radial(i,4);
                Bench(basin+radial*13.9f,-radial);
            }
        }

        private void Bed(Vector3 center,float radius,int seed)
        {
            Tube("stone",center+Vector3.up*.04f,center+Vector3.up*.31f,radius,radius,40,false);
            Disc("soil",center,radius-.11f,40,.289f);
            Ring("tracing",center,radius-.21f,radius+.04f,40,.321f);
            var rng=new System.Random(seed);
            string[] shades={"petalGold","petalRed","petalPink"};
            int number=Mathf.RoundToInt(radius*13);
            for(int i=0;i<number;i++)
            {
                float t=(float)rng.NextDouble()*Mathf.PI*2;
                float r=radius*(.20f+.72f*Mathf.Sqrt((float)rng.NextDouble()));
                Vector3 flower=center+new Vector3(r*Mathf.Cos(t),.34f,r*Mathf.Sin(t));
                float size=.17f+.17f*(float)rng.NextDouble();
                Crown(i%5==0?"leafLight":"foliage",flower,new Vector3(size*1.40f,size*.35f,size),4,7);
                for(int crest=0;crest<3;crest++)
                {
                    Vector3 basePoint=flower+Radial(crest,3)*size*.51f;
                    Crown(shades[(i/4+seed+crest)%shades.Length],basePoint+Vector3.up*(size*.64f),
                        new Vector3(size*.49f,size*.19f,size*.47f),3,5);
                }
            }
        }

        private void Spray(Vector3 start,Vector3 drift,float height)
        {
            if (drift.sqrMagnitude<.0001f)
            {
                Tube("jet",start,start+Vector3.up*height,.046f,.017f,6,false);
                return;
            }
            Vector3 last=start;
            for(int i=1;i<=7;i++)
            {
                float t=i/7f;
                Vector3 tip=start+Vector3.up*(height*Mathf.Sin(Mathf.PI*t))+drift*t;
                if ((tip-last).sqrMagnitude>.0001f)
                    Tube("jet",last,tip,.034f-i*.002f,.031f-i*.002f,5,false);
                last=tip;
            }
        }

        private void Waterfront(Shore shore)
        {
            Vector3[] path=Positions(shore.xz,.057f);
            Vector3 center=Vector3.zero;
            int limit=path.Length-1;
            for (int i=0;i<limit;i++)
                center+=path[i];
            center/=limit;
            float area=0;
            for(int i=0;i<limit;i++)
            {
                Vector3 a=path[i]-center,b=path[i+1]-center;
                area+=a.x*b.z-b.x*a.z;
            }
            bool small=shore.id=="way/714338540";
            if (small)
                Disc("water",center,14.8f,40,.064f);

            var rng=new System.Random(small ? 1329 : 5168);
            float distance=0,next=1f;
            for(int i=0;i<limit;i++)
            {
                Vector3 delta=path[i+1]-path[i];
                float length=delta.magnitude;
                if(length<.05f)
                    continue;
                Vector3 along=delta/length;
                Vector3 normal=Vector3.Cross(Vector3.up,along)*(area<0?-1f:1f);
                for (;next<distance+length;next+=small ? 2.5f : 5f)
                {
                    float t=(next-distance)/length;
                    Vector3 at=path[i]+along*length*t+normal*(.34f+(float)rng.NextDouble()*.75f);
                    if (small && at.z>-315.5f && at.x>1780f && at.x<1797f)
                        continue;
                    for(int blade=0;blade<7;blade++)
                    {
                        Vector3 root=at+Radial(blade,7)*(.25f+(float)rng.NextDouble()*.42f);
                        Vector3 point=root+Vector3.up*(.38f+(float)rng.NextDouble()*.8f)
                            +normal*((float)rng.NextDouble()-.50f)*.37f;
                        Tube("reeds",root,point,.023f,.004f,4,false);
                    }
                }
                distance+=length;
            }
            if (small)
            {
                Basis access=new Basis(new Vector3(1788.4f,0,-302.7f),Vector3.back);
                Box("plank",access,new Vector3(0,.117f,0),new Vector3(4.55f,.21f,3.4f));
                Collision("Настил у Круглого озера",access.At(0,.115f,0),access.Forward,new Vector3(4.55f,.21f,3.4f));
                Bench(access.At(-3.8f,0,-2.8f),access.Forward);
                Bench(access.At(3.8f,0,-2.8f),access.Forward);
                Signpost(access.At(-4.4f,0,-5.3f),access.Forward,"КРУГЛОЕ ОЗЕРО","");
            }
        }

        private void Footbridge(float[] xz)
        {
            var points=Positions(xz,0);
            for (int i=0;i<points.Length-1;i++)
            {
                Vector3 a=points[i],b=points[i+1],across=b-a;
                float length=across.magnitude;
                if (length<.1f)
                    continue;
                Basis deck=new Basis((a+b)*.5f,across);
                Box("metal",deck,new Vector3(0,.083f,0),new Vector3(2.7f,.16f,length));
                int boards=Mathf.CeilToInt(length/.48f);
                for (int board=0;board<boards;board++)
                    Box("plank",deck,new Vector3(0,.195f,length*((board+.5f)/boards-.5f)),
                        new Vector3(2.55f,.085f,length/boards-.014f));
                Collision("Пешеходный мост · настил",deck.At(0,.112f,0),deck.Forward,new Vector3(2.7f,.25f,length));
                for (int side=-1;side<=1;side+=2)
                {
                    float x=1.29f*side;
                    Box("metal",deck,new Vector3(x,1.15f,0),new Vector3(.073f,.074f,length));
                    Box("metal",deck,new Vector3(x,.64f,0),new Vector3(.053f,.052f,length));
                    int posts=Mathf.CeilToInt(length/1.75f);
                    for(int post=0;post<=posts;post++)
                        Box("metal",deck,new Vector3(x,.68f,length*(post/(float)posts-.5f)),new Vector3(.086f,1f,.09f));
                    Collision("Пешеходный мост · перила",deck.At(x,.66f,0),deck.Forward,new Vector3(.083f,1.12f,length));
                }
            }
        }

        private void Tree(Vector3 point,float height,int species,int seed)
        {
            float tilt=Mathf.Sin(seed*3.93f)*height*.059f;
            Vector3 top=point+new Vector3(tilt,height*.76f,tilt*.31f);
            string bark=species==0?"birch":"bark";
            Tube(bark,point,top,height*.023f,height*.004f,7,true,true);
            if(species==2)
            {
                for (int i=0;i<4;i++)
                {
                    Vector3 from=point+Vector3.up*(height*(.31f+.15f*i));
                    Vector3 tip=from+new Vector3(tilt*.85f,height*.39f,tilt*.2f);
                    Tube("pine",from,tip,height*(.238f-i*.037f),.08f,10,true,true);
                }
                return;
            }
            for (int i=0;i<5;i++)
            {
                Vector3 outward=Radial(i+(seed%8)*.37f,5);
                Vector3 branch=point+Vector3.up*(height*(.52f+i*.062f));
                Vector3 edge=branch+outward*(height*(.12f+i%2*.03f))+Vector3.up*(height*.11f);
                Tube(bark,branch,edge,height*.009f,.025f,5,false,true);
                float radius=height*(species==0 ? .185f : .220f);
                Crown(i%3==0?"leafLight":"foliage",edge,new Vector3(radius,radius*1.16f,radius*.88f),4,8,true);
            }
            if (species==0)
            {
                Vector3 x=new Vector3(1,0,0);
                Vector3 z=new Vector3(0,0,1);
                for (int i=0;i<6;i++)
                {
                    float t=.11f+i*.089f;
                    Vector3 at=Vector3.Lerp(point,top,t);
                    float width=height*(.023f-.019f*t)+.006f;
                    var band=Batch("bark",point,true);
                    Vector3 side=(i%2==0?x:-x)*width;
                    band.Quad(at+side-z*width*.42f,at+side+z*width*.35f,
                        at+side+z*width*.42f+Vector3.up*.047f,at+side-z*width*.44f+Vector3.up*.029f);
                }
            }
            Crown("leafLight",top+Vector3.up*(height*.10f),new Vector3(height*.16f,height*.19f,height*.17f),4,9,true);
        }

        private void Bench(Vector3 point,Vector3 forward)
        {
            point.y=0;
            var frame=new Basis(point,forward);
            for(int side=-1;side<=1;side+=2)
            {
                float x=side*.82f;
                Box("metal",frame,new Vector3(x,.27f,-.06f),new Vector3(.076f,.53f,.36f));
                Box("metal",frame,new Vector3(x,.73f,-.24f),new Vector3(.057f,.60f,.052f));
            }
            for(int plank=0;plank<4;plank++)
                Box("plank",frame,new Vector3(0,.52f,-.17f+plank*.111f),new Vector3(2.03f,.067f,.096f));
            for(int plank=0;plank<3;plank++)
                Box("plank",frame,new Vector3(0,.67f+plank*.128f,-.225f-plank*.021f),new Vector3(2.03f,.102f,.044f));
            Collision("Парковая скамейка",frame.At(0,.56f,-.04f),frame.Forward,new Vector3(2.02f,.95f,.56f));
        }

        private void Lantern(Vector3 point)
        {
            point.y=.032f;
            Tube("metal",point,point+Vector3.up*.24f,.15f,.11f,8);
            Tube("metal",point+Vector3.up*.19f,point+Vector3.up*3.41f,.067f,.035f,8);
            Tube("glass",point+Vector3.up*3.35f,point+Vector3.up*3.73f,.119f,.154f,6);
            Tube("metal",point+Vector3.up*3.73f,point+Vector3.up*3.83f,.159f,.102f,6);
        }

        private void Signpost(Vector3 point,Vector3 forward,string title,string directions)
        {
            var frame=new Basis(point,forward);
            for(int side=-1;side<=1;side+=2)
                Box("metal",frame,new Vector3(side*.65f,.99f,0),new Vector3(.083f,1.96f,.083f));
            Box("green",frame,new Vector3(0,1.63f,0),new Vector3(1.59f,.57f,.08f));
            Text(title,frame.At(0,1.76f,-.05f),forward,.095f,Color.white);
            if (!string.IsNullOrEmpty(directions))
                Text(directions,frame.At(0,1.58f,-.05f),forward,.050f,Color.white);
        }

        private void Text(string label,Vector3 point,Vector3 forward,float height,Color color)
        {
            if(lettering==null)
                return;
            var obj=new GameObject(label,typeof(TextMesh));
            obj.transform.SetParent(transform,false);
            obj.transform.SetPositionAndRotation(point,Quaternion.LookRotation(forward,Vector3.up));
            var text=obj.GetComponent<TextMesh>();
            text.font=lettering;
            text.fontSize=72;
            text.characterSize=height*10f/72f;
            text.text=label;
            text.color=color;
            text.anchor=TextAnchor.MiddleCenter;
            text.alignment=TextAlignment.Center;
            obj.GetComponent<MeshRenderer>().sharedMaterial=lettering.material;
        }

        private void Collision(string label,Vector3 point,Vector3 forward,Vector3 size)
        {
            var obj=new GameObject(label,typeof(BoxCollider));
            obj.transform.SetParent(transform,false);
            obj.transform.SetPositionAndRotation(point,Quaternion.LookRotation(forward,Vector3.up));
            obj.GetComponent<BoxCollider>().size=size;
        }

        private void Ribbon(string style,Vector3[] points,float width)
        {
            var edges=new Vector3[points.Length];
            bool closed=(points[0]-points[points.Length-1]).sqrMagnitude<.0001f;
            for(int i=0;i<points.Length;i++)
            {
                int previousIndex=i==0&&closed ? points.Length-2 : Mathf.Max(0,i-1);
                int nextIndex=i==points.Length-1&&closed ? 1 : Mathf.Min(i+1,points.Length-1);
                Vector3 previous=(points[i]-points[previousIndex]).normalized;
                Vector3 next=(points[nextIndex]-points[i]).normalized;
                Vector3 axis=previous.sqrMagnitude>.01f ? previous : next;
                Vector3 bisector=previous+next;
                Vector3 normal=Vector3.Cross(Vector3.up,bisector.sqrMagnitude>.01f?bisector.normalized:axis);
                float miter=Mathf.Max(.80f,Vector3.Dot(normal,Vector3.Cross(Vector3.up,axis)));
                edges[i]=normal*(width*.5f/miter);
            }
            for(int i=0;i<points.Length-1;i++)
                Batch(style,points[i]).Quad(points[i]-edges[i],points[i+1]-edges[i+1],
                    points[i+1]+edges[i+1],points[i]+edges[i]);
        }

        private void Box(string style,Basis frame,Vector3 local,Vector3 size)
        {
            Vector3 half=size*.5f;
            Vector3[] floor={
                frame.At(local.x-half.x,local.y-half.y,local.z-half.z),
                frame.At(local.x-half.x,local.y-half.y,local.z+half.z),
                frame.At(local.x+half.x,local.y-half.y,local.z+half.z),
                frame.At(local.x+half.x,local.y-half.y,local.z-half.z)
            };
            Vector3 up=Vector3.up*size.y;
            Geometry mesh=Batch(style,floor[0]);
            mesh.Quad(floor[0]+up,floor[1]+up,floor[2]+up,floor[3]+up);
            for(int i=0;i<4;i++)
            {
                Vector3 a=floor[i],b=floor[(i+1)%4];
                mesh.Quad(a,b,b+up,a+up);
            }
            mesh.Quad(floor[3],floor[2],floor[1],floor[0]);
        }

        private void Disc(string style,Vector3 center,float radius,int sides,float y)
        {
            center.y=y;
            Geometry g=Batch(style,center);
            for (int side=0;side<sides;side++)
                g.Triangle(center,center+Radial(side+1,sides)*radius,center+Radial(side,sides)*radius);
        }

        private void Ring(string style,Vector3 center,float inner,float outer,int sides,float y)
        {
            center.y=y;
            Geometry g=Batch(style,center);
            for(int i=0;i<sides;i++)
            {
                Vector3 a=Radial(i,sides),b=Radial(i+1,sides);
                g.Quad(center+a*inner,center+b*inner,center+b*outer,center+a*outer);
            }
        }

        private void Tube(string style,Vector3 from,Vector3 to,float radiusA,float radiusB,int sides,
            bool caps=true,bool regional=false)
        {
            Vector3 axis=(to-from).normalized;
            Vector3 u=Vector3.Cross(axis,Mathf.Abs(axis.y)>.85f?Vector3.forward:Vector3.up).normalized;
            Vector3 v=Vector3.Cross(axis,u);
            Geometry g=Batch(style,from,regional);
            for (int i=0;i<sides;i++)
            {
                Vector3 a=Radial(i,sides),b=Radial(i+1,sides);
                Vector3 x=u*a.x+v*a.z,z=u*b.x+v*b.z;
                g.Quad(from+x*radiusA,from+z*radiusA,to+z*radiusB,to+x*radiusB);
                if(caps)
                {
                    g.Triangle(from,from+z*radiusA,from+x*radiusA);
                    g.Triangle(to,to+x*radiusB,to+z*radiusB);
                }
            }
        }

        private void Crown(string style,Vector3 center,Vector3 scale,int rows,int sides,bool regional=false)
        {
            var g=Batch(style,center,regional);
            int initial=g.Points.Count;
            float seed=Mathf.Sin(center.x*.075f+center.z*.19f);
            for(int row=0;row<=rows;row++)
            for(int side=0;side<=sides;side++)
            {
                float polar=Mathf.PI*row/rows;
                Vector3 radial=Radial(side,sides);
                float bulge=1f+.105f*Mathf.Sin((side%sides)*6.26f/sides*3f+row*.82f+seed*2f);
                Vector3 point=new Vector3(radial.x*Mathf.Sin(polar)*scale.x*bulge,
                    -Mathf.Cos(polar)*scale.y,radial.z*Mathf.Sin(polar)*scale.z*bulge);
                g.Vertex(center+point,new Vector2(center.x%7f+side/(float)sides*scale.x*2.6f,
                    center.z%7f+row/(float)rows*scale.y*2.6f));
                if(row==0||side==sides)
                    continue;
                int a=initial+(row-1)*(sides+1)+side,b=a+sides+1;
                if(row>1)
                    g.Add(a,b,a+1);
                if(row<rows)
                    g.Add(a+1,b,b+1);
            }
        }

        private Geometry Batch(string style,Vector3 point,bool regional=false)
        {
            var key=(style,regional?Mathf.FloorToInt(point.x/TreeCellSize):0,regional?Mathf.FloorToInt(point.z/TreeCellSize):0);
            if(!batches.TryGetValue(key,out Geometry geometry))
            {
                geometry=new Geometry(origin);
                batches.Add(key,geometry);
            }
            return geometry;
        }

        private void Publish(string style,int x,int z,Geometry data)
        {
            if(data.Indices.Count==0)
                return;
            var label="ПКиО · "+style+" · "+x+" "+z;
            var group=new GameObject(label,typeof(MeshRenderer),typeof(MeshFilter));
            group.transform.SetParent(transform,false);
            var mesh=new Mesh { name=label,indexFormat=data.Points.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16 };
            mesh.SetVertices(data.Points);
            mesh.SetUVs(0,data.UV);
            mesh.SetTriangles(data.Indices,0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            meshes.Add(mesh);
            group.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer = group.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=materials[style];
            if (style == "pine" || style == "foliage" || style == "leafLight" ||
                style == "birch" || style == "bark")
                vegetation.Add((renderer,renderer.bounds));
        }

        private void OnDestroy()
        {
            foreach(Mesh mesh in meshes)
                Destroy(mesh);
            foreach(Material material in materials.Values)
                Destroy(material);
        }

        private static Vector3[] Positions(float[] pairs,float y)
        {
            var points=new Vector3[pairs.Length/2];
            for(int i=0;i<points.Length;i++)
                points[i]=new Vector3(pairs[i*2],y,pairs[i*2+1]);
            return points;
        }

        private static Vector3 Radial(float step,int count)
        {
            float angle=2f*Mathf.PI*step/count;
            return new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
        }

        private readonly struct Basis
        {
            internal readonly Vector3 Forward;
            private readonly Vector3 right,origin;

            internal Basis(Vector3 origin,Vector3 forward)
            {
                this.origin=origin;
                Forward=forward.normalized;
                right=Vector3.Cross(Vector3.up,Forward);
            }

            internal Vector3 At(float x,float y,float z)=>origin+right*x+Vector3.up*y+Forward*z;
        }

        private sealed class Geometry
        {
            internal readonly List<Vector3> Points=new List<Vector3>();
            internal readonly List<Vector2> UV=new List<Vector2>();
            internal readonly List<int> Indices=new List<int>();
            private readonly Vector3 origin;

            internal Geometry(Vector3 origin) => this.origin=origin;

            internal void Vertex(Vector3 position,Vector2 uv)
            {
                Points.Add(position-origin);
                UV.Add(uv);
            }

            internal void Add(int a,int b,int c)
            {
                Indices.Add(a);
                Indices.Add(b);
                Indices.Add(c);
            }

            internal void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {
                int index=Points.Count;
                Vector3 along=b-a,normal=Vector3.Cross(along,c-a);
                Vertex(a,Project(a,normal));
                Vertex(b,Project(b,normal));
                Vertex(c,Project(c,normal));
                Add(index,index+1,index+2);
            }

            internal void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int index=Points.Count;
                Vector3 normal=Vector3.Cross(b-a,c-a);
                Vertex(a,Project(a,normal));
                Vertex(b,Project(b,normal));
                Vertex(c,Project(c,normal));
                Vertex(d,Project(d,normal));
                Add(index,index+1,index+2);
                Add(index,index+2,index+3);
            }

            private static Vector2 Project(Vector3 p,Vector3 normal)
            {
                float x=Mathf.Abs(normal.x),y=Mathf.Abs(normal.y),z=Mathf.Abs(normal.z);
                if (y>=x&&y>=z)
                    return new Vector2(p.x,p.z);
                return x>=z?new Vector2(p.z,p.y):new Vector2(p.x,p.y);
            }
        }
    }
}
