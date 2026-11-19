using System.Collections.Generic;
using LifeCraftRP;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeCraft.World
{
    /// <summary>Landmark buildings over the surveyed outlines of Omsk's two shopping centres.</summary>
    internal static class OmskMalls
    {
        private const float Ground = .14f;
        private const float MegaDeck = 4.25f;
        private static readonly Dictionary<int, Material> Finishes = new Dictionary<int, Material>();
        private static Font signFont;
        private static readonly Color White = new Color(.94f,.94f,.91f);

        // Near the actual public sides on Maslennikova and the southeast pedestrian approach at MEGA.
        internal static void BuildPark(Transform root,IReadOnlyList<Vector2> footprint)
            => Build(root,footprint,false,new Vector2(1297.51f,209.15f));

        internal static void BuildMega(Transform root,IReadOnlyList<Vector2> footprint)
            => Build(root,footprint,true,new Vector2(-6800.55f,-177.59f));

        private static void Build(Transform root,IReadOnlyList<Vector2> footprint,bool mega,Vector2 worldEntry)
        {
            var ring = new List<Vector2>(footprint.Count);
            foreach (Vector2 vertex in footprint)
            {
                if (ring.Count == 0 || (vertex-ring[ring.Count-1]).sqrMagnitude > .0025f)
                    ring.Add(vertex);
            }
            if (ring.Count > 1 && (ring[0]-ring[ring.Count-1]).sqrMagnitude < .0025f)
                ring.RemoveAt(ring.Count-1);
            if (ring.Count < 3)
                return;
            if (Area(ring) > 0f)
                ring.Reverse();

            Vector2 target = worldEntry-new Vector2(root.position.x,root.position.z);
            int front = 0;
            float best = float.MaxValue;
            float doorCentre = 0;
            for (int i=0;i<ring.Count;i++)
            {
                var frame = new Face(ring[i],ring[(i+1)%ring.Count]);
                if (frame.Length < 26f)
                    continue;
                float along = Mathf.Clamp(Vector2.Dot(target-frame.Start,frame.Along),0f,frame.Length);
                float distance = (target-frame.Start-frame.Along*along).sqrMagnitude;
                if (distance < best)
                {
                    front = i;
                    doorCentre = along;
                    best = distance;
                }
            }

            var model = new Building(root);
            float height = mega ? 15.35f : 17.1f;
            for (int i=0;i<ring.Count;i++)
            {
                var frame = new Face(ring[i],ring[(i+1)%ring.Count]);
                if (i == front)
                {
                    float portalWidth = Mathf.Min(frame.Length-4f,mega ? 34f : 28f);
                    float mid = Mathf.Clamp(doorCentre,portalWidth*.5f+1f,frame.Length-portalWidth*.5f-1f);
                    float left = mid-portalWidth*.5f, right = mid+portalWidth*.5f;
                    Wall(model,frame,0f,left,height,mega,i);
                    Wall(model,frame,right,frame.Length,height,mega,i);
                    if (mega)
                        MegaEntrance(model,frame,mid,portalWidth,height);
                    else
                        ParkEntrance(model,frame,mid,portalWidth,height);
                    Lobby(model,frame,mid,mega);
                }
                else
                {
                    Wall(model,frame,0f,frame.Length,height,mega,i);
                }

                model.Box(frame,Finish.Roof,0,frame.Length,height-.18f,height+.32f,-.40f,.20f);
                if (frame.Length > 19f)
                    RoofPlant(model,frame,frame.Length*.58f,height);
            }

            List<int> roof = Triangulate(ring);
            model.Surface(Finish.Roof,ring,roof,height-.12f,true);
            if (mega)
            {
                model.Surface(Finish.Concrete,ring,roof,MegaDeck-.23f,false);
                model.Surface(Finish.Asphalt,ring,roof,.10f,true);
                Parking(model,ring);
            }
            model.Publish();
        }

        private static void Wall(Building model, Face frame,float left,float right,float height,bool mega,int side)
        {
            float width = right-left;
            if (width <= .04f)
                return;
            float baseHeight = mega ? MegaDeck : Ground;
            Finish shell = mega ? Finish.Sheet : Finish.ParkWall;
            model.Box(frame,shell,left,right,baseHeight,height,-.34f,0f,true);
            if (width < 4f)
                return;

            if (mega)
            {
                model.Box(frame,Finish.Concrete,left,right,MegaDeck-.32f,MegaDeck+.14f,-.24f,.12f);
                model.Box(frame,Finish.Trim,left,right,8.78f,9.04f,-.05f,.12f);
                model.Box(frame,Finish.SheetShade,left,right,12.15f,13.53f,-.01f,.07f);
                int grids = Mathf.Max(1,Mathf.FloorToInt(width/5.2f));
                for (int i=1;i<grids;i++)
                {
                    float x = left+width*i/grids;
                    model.Box(frame,Finish.Trim,x-.025f,x+.025f,MegaDeck+.21f,12.13f,.025f,.078f);
                }
                model.Box(frame,Finish.Sheet,left,right,13.55f,13.64f,-.03f,.11f);
                return;
            }
            model.Box(frame,Finish.SheetShade,left,right,Ground,1.18f,-.06f,.135f);
            model.Box(frame,Finish.Trim,left,right,4.56f,4.76f,-.08f,.13f);
            model.Box(frame,Finish.Trim,left,right,12.1f,12.28f,-.05f,.09f);
            int panels = Mathf.Max(1,Mathf.CeilToInt(width/2.25f));
            for (int col=0;col<panels;col++)
            {
                float x0 = left+width*col/panels;
                float x1 = left+width*(col+1)/panels;
                Finish glass = (col+side)%4 == 0 ? Finish.GlassShade : Finish.Glass;
                model.Box(frame,glass,x0+.036f,x1-.036f,1.22f,4.50f,.006f,.09f);
                model.Box(frame,Finish.Trim,x0+.016f,x0+.069f,1.16f,4.61f,.041f,.111f);

                for (int row=0;row<5;row++)
                {
                    float y = 5.07f+row*1.36f;
                    Finish color = ParkPanel(col,row,side);
                    model.Box(frame,color,x0+.027f,x1-.027f,y,y+1.305f,0,.103f);
                }
                if ((col+2*side)%3 == 0)
                    model.Box(frame,glass,x0+.23f,x1-.23f,13.20f,15.23f,.007f,.073f);
            }
        }

        private static Finish ParkPanel(int column,int row,int face)
        {
            // Irregular staggered panels on the Maslennikova facade.
            int index = (column*19+row*7+column*row*3+face*11)%31;
            if (index < 4) return Finish.ParkRust;
            if (index < 7) return Finish.ParkLemon;
            if (index < 12) return Finish.ParkSand;
            if (index < 18) return Finish.Sheet;
            return index < 25 ? Finish.SheetShade : Finish.ParkWall;
        }

        private static void ParkEntrance(Building model,Face frame,float mid,float width,float height)
        {
            float left = mid-width*.5f, right = mid+width*.5f;
            model.Box(frame,Finish.ParkWall,left,right,8.06f,height,-.30f,0,true);
            model.Box(frame,Finish.ParkOrange,left-.36f,right+.36f,13.85f,14.91f,-.15f,.76f);
            for (int side=-1;side<=1;side+=2)
            {
                float x = mid+side*width*.5f;
                model.Box(frame,Finish.ParkOrange,x-.36f,x+.36f,Ground,14.88f,-.28f,.74f);
                model.Box(frame,Finish.ParkOrange,x-.28f,x+.28f,15.32f,16.6f,-.11f,.42f);
            }
            model.Box(frame,Finish.Trim,left,right,4.47f,4.71f,-.68f,3.18f);
            model.Box(frame,Finish.ParkOrange,left-.22f,right+.22f,4.68f,5.10f,2.85f,3.30f);
            model.Box(frame,Finish.Ceiling,left+.35f,right-.35f,4.451f,4.477f,0f,2.83f);
            model.Box(frame,Finish.GlassShade,left,right,5.14f,8.08f,-.14f,.035f);
            for (int k=1;k<12;k++)
            {
                float x = left+k*width/12f;
                model.Box(frame,Finish.Trim,x-.041f,x+.041f,5.12f,8.14f,.015f,.093f);
            }
            int bays = Mathf.Max(1,Mathf.CeilToInt(width/2.19f));
            for (int col=0;col<bays;col++)
            for (int row=0;row<4;row++)
            {
                float x0=left+width*col/bays, x1=left+width*(col+1)/bays;
                float y=8.14f+row*1.29f;
                model.Box(frame,ParkPanel(col,row,0),x0+.035f,x1-.035f,y,y+1.236f,.005f,.11f);
            }
            // The upper facade is a tenant rail set in front of the panel mosaic.
            model.Box(frame,Finish.Trim,left+.53f,right-.53f,9.54f,9.61f,.14f,.26f);
            model.Box(frame,Finish.Trim,left+.53f,right-.53f,10.78f,10.85f,.14f,.26f);
            ParkTenant(model,frame,"kari",mid+width*.360f,10.20f,2.15f,Finish.ParkRust,.58f);
            ParkTenant(model,frame,"SOKOLOV",mid+width*.194f,10.20f,3.18f,Finish.Trim,.54f);
            ParkTenant(model,frame,"COFFEE ANYTIME",mid+width*.014f,10.20f,4.20f,Finish.ParkWall,.36f);
            ParkTenant(model,frame,"ДЕТСКИЙ МИР",mid-width*.193f,10.20f,4.76f,Finish.ParkBlue,.47f);
            ParkTenant(model,frame,"ZARINA",mid-width*.388f,10.20f,2.72f,Finish.Trim,.57f);
            model.Sign(frame,"ПАРК",mid+width*.27f,12.65f,.20f,1.49f,White);
            model.Sign(frame,"МАСЛЕННИКОВА, 134",mid,5.37f,.225f,.30f,White);
            GlazedDoor(model,frame,mid,width,4.47f);
            Forecourt(model,frame,mid,width,6.0f);
        }

        private static void ParkTenant(Building model,Face frame,string text,float x,float y,
            float width,Finish finish,float size)
        {
            model.Box(frame,finish,x-width*.5f,x+width*.5f,y-.45f,y+.45f,.17f,.29f);
            model.Sign(frame,text,x,y,.32f,size,White);
        }

        private static void MegaEntrance(Building model,Face frame,float mid,float width,float height)
        {
            float left = mid-width*.5f, right = mid+width*.5f;
            const float glassTop = 12.62f;
            model.Box(frame,Finish.SheetShade,left,right,glassTop,height,-.39f,0f,true);
            model.Box(frame,Finish.Aluminium,left-.43f,right+.43f,12.55f,12.86f,-.18f,.76f);
            int columns = Mathf.Max(10,Mathf.RoundToInt(width/1.93f));
            for (int col=0;col<columns;col++)
            {
                float x0=left+col*width/columns, x1=left+(col+1)*width/columns;
                float paneHeight=(glassTop-4.19f)/4f;
                for (int row=0;row<4;row++)
                {
                    float y=4.19f+row*paneHeight;
                    Finish glass=(col*7+row*3)%9 < 2 ? Finish.GlassShade : Finish.Glass;
                    model.Box(frame,glass,x0+.038f,x1-.038f,y+.025f,y+paneHeight-.025f,-.137f,.002f);
                }
                model.Box(frame,col%4==0?Finish.Aluminium:Finish.Trim,
                    x0-.042f,x0+.042f,4.11f,glassTop,.025f,.162f);
            }
            model.Box(frame,Finish.Aluminium,right-.046f,right+.046f,4.11f,glassTop,.025f,.162f);
            for (int row=1;row<4;row++)
            {
                float y=4.19f+row*(glassTop-4.19f)/4f;
                model.Box(frame,Finish.Trim,left,right,y-.042f,y+.042f,.023f,.143f);
            }
            // Pale primary steel struts are visible through the full-height glazing.
            for (int bay=0;bay<6;bay++)
            {
                float x0=left+bay*width/6f, x1=left+(bay+1)*width/6f;
                model.Box(frame,Finish.Aluminium,x0-.084f,x0+.084f,4.30f,glassTop,-.78f,-.48f);
                for (int k=0;k<2;k++)
                    model.Brace(frame,Finish.Aluminium,x0,4.39f+k*4.02f,x1,8.24f+k*4.02f,-.53f,.081f);
            }
            model.Box(frame,Finish.Aluminium,left-.88f,right+.88f,4.20f,4.59f,-.85f,3.90f);
            model.Box(frame,Finish.Ceiling,left+.12f,right-.12f,4.17f,4.223f,0f,3.7f);
            string glyphs = "МЕГА";
            var colors = new[]{new Color(.91f,.21f,.19f),new Color(.22f,.70f,.32f),
                new Color(.22f,.50f,.84f),new Color(.97f,.66f,.15f)};
            for (int j=0;j<4;j++)
                model.Sign(frame,glyphs[j].ToString(),mid+(1.5f-j)*3.18f,14.03f,.36f,2.36f,colors[j]);
            model.Sign(frame,"ВХОД",mid,4.40f,3.95f,.43f,White);
            model.Sign(frame,"БУЛЬВАР АРХИТЕКТОРОВ, 35",mid,3.79f,3.96f,.24f,White);
            GlazedDoor(model,frame,mid,width,4.17f);
            Forecourt(model,frame,mid,width,7.6f);
            for (int side=-1;side<=1;side+=2)
            {
                float x=mid+side*(width*.5f-2.6f);
                model.Box(frame,Finish.Trim,x-.085f,x+.085f,Ground,2.54f,5.23f,5.37f);
                model.Box(frame,Finish.ParkGreen,x-.51f,x+.51f,1.72f,2.53f,5.1f,5.5f);
                model.Sign(frame,side<0?"10О":"10Л",x,2.12f,5.54f,.43f,White);
                model.Box(frame,Finish.Concrete,x-1.96f,x+1.96f,Ground,Ground+.31f,6.19f,7.15f);
                model.Box(frame,Finish.Hedge,x-1.77f,x+1.77f,Ground+.27f,Ground+.88f,6.32f,7.02f);
            }
        }

        private static void GlazedDoor(Building model, Face frame,float mid,float width,float top)
        {
            float left = mid-width*.5f, right = mid+width*.5f;
            const float doorHalf = 1.78f;
            for (int side=-1;side<=1;side+=2)
            {
                float edge = mid+side*doorHalf;
                float start = side<0 ? left : edge, end = side<0 ? edge : right;
                model.Box(frame,Finish.Glass,start,end,Ground+.17f,top,-.09f,.027f,true);
                int panes=Mathf.Max(1,Mathf.CeilToInt((end-start)/2.25f));
                for (int k=0;k<=panes;k++)
                {
                    float x=Mathf.Lerp(start,end,k/(float)panes);
                    model.Box(frame,Finish.Trim,x-.035f,x+.035f,Ground,top,.02f,.111f);
                }
                model.Box(frame,Finish.Aluminium,edge-.067f,edge+.067f,Ground,3.19f,-.05f,.21f);
            }
            model.Box(frame,Finish.Trim,mid-doorHalf,mid+doorHalf,3.12f,3.29f,-.12f,.13f);
            model.Box(frame,Finish.Glass,mid-doorHalf,mid+doorHalf,3.25f,top,-.12f,.025f,true);
        }

        private static void Forecourt(Building model, Face frame,float mid,float width,float depth)
        {
            model.Box(frame,Finish.Paving,mid-width*.5f-.48f,mid+width*.5f+.48f,
                -.08f,Ground+.057f,0f,depth,true);
            // Drains, approach contrast and protective bollards outside the vehicle circulation lanes.
            model.Box(frame,Finish.Trim,mid-3.4f,mid+3.4f,Ground+.058f,Ground+.083f,2.04f,2.13f);
            for (int i=-4;i<=4;i++)
            {
                if (i >= -1 && i <= 1)
                    continue;
                float x=mid+i*3.1f;
                model.Box(frame,Finish.Aluminium,x-.058f,x+.058f,Ground+.05f,Ground+.94f,
                    depth-1.05f,depth-.93f);
                model.Box(frame,Finish.ParkOchre,x-.064f,x+.064f,Ground+.73f,Ground+.84f,
                    depth-1.06f,depth-.92f);
            }
        }

        private static void Lobby(Building model,Face frame,float mid,bool mega)
        {
            float half = mega ? 13f : 11.4f, depth = mega ? 13.3f : 15.6f;
            float top = mega ? 3.93f : 4.42f;
            model.Box(frame,Finish.Paving,mid-half,mid+half,-.10f,Ground+.063f,-depth-.1f,.1f,true);
            model.Box(frame,Finish.Ceiling,mid-half,mid+half,top,top+.13f,-depth,.08f);
            model.Box(frame,Finish.Concrete,mid-half,mid+half,Ground,top+.13f,-depth-.24f,-depth,true);
            for (int side=-1;side<=1;side+=2)
            {
                float x=mid+side*half;
                model.Box(frame,Finish.Concrete,x-.22f,x+.22f,Ground,top+.14f,-depth,.04f,true);
                model.Box(frame,Finish.ParkSand,x-1.36f,x+1.36f,Ground,Ground+.55f,-8.91f,-8.26f);
                for (int i=0;i<3;i++)
                {
                    float lx=mid+side*(2.15f+i*2.6f);
                    model.Box(frame,Finish.Ceiling,lx-.64f,lx+.64f,top-.041f,top-.010f,-11.7f,-11.36f);
                    model.Box(frame,Finish.Ceiling,lx-.64f,lx+.64f,top-.041f,top-.010f,-4.4f,-4.06f);
                }
            }

            // A finite visitable lobby. The glazed end wall bounds the rest of the retail floor.
            model.Box(frame,Finish.Trim,mid-4.41f,mid+4.41f,Ground+.2f,3.22f,-depth+.06f,-depth+.16f);
            model.Box(frame,Finish.ParkGreen,mid-3.97f,mid+3.97f,1.18f,2.96f,-depth+.14f,-depth+.22f);
            model.Sign(frame,mega?"МЕГА ОМСК":"ТОРГОВЫЙ ЦЕНТР «ПАРК»",mid,2.34f,-depth+.25f,.42f,White);
            model.Sign(frame,"ВХОДНАЯ ГАЛЕРЕЯ",mid,1.67f,-depth+.25f,.25f,White);
            model.Box(frame,Finish.SheetShade,mid-1.45f,mid+1.45f,Ground,Ground+.95f,
                -depth+1.85f,-depth+2.60f,true);
            model.LobbyLight(frame.At(mid,top-.59f,-3.6f));
            model.LobbyLight(frame.At(mid,top-.76f,-9.8f));
        }

        private static void RoofPlant(Building model,Face frame,float x,float height)
        {
            model.Box(frame,Finish.SheetShade,x-2.6f,x+2.6f,height-.12f,height+1.03f,-5f,-2.55f);
            for (int i=0;i<5;i++)
            {
                float y=height+.12f+i*.15f;
                model.Box(frame,Finish.Trim,x-2.40f,x+2.40f,y,y+.041f,-2.59f,-2.51f);
            }
        }

        private static void Parking(Building model,List<Vector2> ring)
        {
            Vector2 min=ring[0],max=ring[0];
            foreach (Vector2 p in ring)
            {
                min=Vector2.Min(min,p);
                max=Vector2.Max(max,p);
            }
            const float spacing=24f;
            for (float x=min.x+12f;x<max.x-9f;x+=spacing)
            for (float z=min.y+12f;z<max.y-9f;z+=spacing)
            {
                var p=new Vector2(x,z);
                if (!Interior(p,ring,17f))
                    continue;
                var post=new Face(p,p+Vector2.right*3f);
                model.Box(post,Finish.Concrete,-.49f,.49f,Ground,MegaDeck-.18f,-.49f,.49f,true);
                model.Box(post,Finish.ParkGreen,-.502f,.502f,1.18f,1.79f,-.502f,.502f);
                for (int i=0;i<4;i++)
                {
                    float lane=4.2f+i*2.36f;
                    model.Box(post,Finish.RoadMark,lane,lane+.09f,Ground+.018f,Ground+.027f,-4.4f,.81f);
                }
            }
        }

        private static float Area(IReadOnlyList<Vector2> ring)
        {
            float sum=0f;
            for (int a=0,b=ring.Count-1;a<ring.Count;b=a++)
                sum += Cross(ring[b],ring[a]);
            return sum*.5f;
        }

        private static float Cross(Vector2 a,Vector2 b) => a.x*b.y-a.y*b.x;

        private static bool Interior(Vector2 point,IReadOnlyList<Vector2> ring,float clearance)
        {
            bool inside=false;
            for (int i=0,j=ring.Count-1;i<ring.Count;j=i++)
            {
                Vector2 a=ring[j], b=ring[i], direction=b-a, relative=point-a;
                float along=Mathf.Clamp01(Vector2.Dot(relative,direction)/direction.sqrMagnitude);
                if ((relative-direction*along).sqrMagnitude < clearance*clearance)
                    return false;
                if ((a.y>point.y)!=(b.y>point.y) && point.x<(b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x)
                    inside=!inside;
            }
            return inside;
        }

        private static List<int> Triangulate(IReadOnlyList<Vector2> ring)
        {
            var pending = new List<int>(ring.Count);
            var mesh = new List<int>((ring.Count-2)*3);
            for (int i=0;i<ring.Count;i++)
                pending.Add(i);
            while (pending.Count>2)
            {
                bool found=false;
                for (int j=0;j<pending.Count;j++)
                {
                    int a=pending[(j+pending.Count-1)%pending.Count], b=pending[j], c=pending[(j+1)%pending.Count];
                    Vector2 v0=ring[b]-ring[a],v1=ring[c]-ring[b],v2=ring[a]-ring[c];
                    if (Cross(v0,v1)>=-.001f)
                        continue;
                    bool occupied=false;
                    foreach(int index in pending)
                    {
                        if(index==a||index==b||index==c)
                            continue;
                        Vector2 p=ring[index];
                        if(Cross(v0,p-ring[a])<-.001f && Cross(v1,p-ring[b])<-.001f &&
                            Cross(v2,p-ring[c])<-.001f)
                        {
                            occupied=true;
                            break;
                        }
                    }
                    if (occupied)
                        continue;
                    mesh.Add(a); mesh.Add(b); mesh.Add(c);
                    pending.RemoveAt(j);
                    found=true;
                    break;
                }
                if (!found)
                    break;
            }
            return mesh;
        }

        private readonly struct Face
        {
            internal readonly Vector2 Start,Along;
            internal readonly float Length;
            internal Vector3 Normal => new Vector3(-Along.y,0,Along.x);
            internal Face(Vector2 a,Vector2 b)
            {
                Start=a;
                Length=(b-a).magnitude;
                Along=(b-a)/Length;
            }
            internal Vector3 At(float x,float y,float z)
                => new Vector3(Start.x+Along.x*x-Along.y*z,y,Start.y+Along.y*x+Along.x*z);
        }

        private enum Finish
        {
            Concrete,Paving,Roof,ParkWall,ParkOrange,ParkOchre,ParkSand,Sheet,SheetShade,Trim,
            Aluminium,Glass,GlassShade,Ceiling,ParkGreen,RoadMark,Asphalt,ParkRust,ParkLemon,ParkBlue,Hedge
        }

        private static Material MaterialFor(Finish finish)
        {
            if (Finishes.TryGetValue((int)finish,out Material cached) && cached)
                return cached;
            Color color;
            string texture=null;
            float gloss=.16f;
            switch (finish)
            {
                case Finish.Concrete: color=new Color(.76f,.74f,.69f);texture="WeatheredConcrete";break;
                case Finish.Paving: color=new Color(.83f,.79f,.73f);texture="ConcretePavers";break;
                case Finish.Roof: color=new Color(.48f,.48f,.45f);texture="WeatheredConcrete";break;
                case Finish.ParkWall: color=new Color(.34f,.34f,.33f);break;
                case Finish.ParkOrange: color=new Color(.88f,.30f,.075f);gloss=.39f;break;
                case Finish.ParkOchre: color=new Color(.64f,.45f,.31f);break;
                case Finish.ParkSand: color=new Color(.65f,.59f,.52f);break;
                case Finish.ParkRust: color=new Color(.58f,.225f,.16f);gloss=.27f;break;
                case Finish.ParkLemon: color=new Color(.82f,.65f,.235f);gloss=.27f;break;
                case Finish.ParkBlue: color=new Color(.12f,.34f,.59f);gloss=.25f;break;
                case Finish.Sheet: color=new Color(.63f,.65f,.64f);gloss=.42f;break;
                case Finish.SheetShade: color=new Color(.41f,.43f,.43f);gloss=.32f;break;
                case Finish.Trim: color=new Color(.10f,.119f,.124f);gloss=.37f;break;
                case Finish.Aluminium: color=new Color(.67f,.71f,.72f);gloss=.50f;break;
                case Finish.Glass: color=new Color(.27f,.40f,.46f,.31f);gloss=.86f;break;
                case Finish.GlassShade: color=new Color(.22f,.32f,.38f,.47f);gloss=.77f;break;
                case Finish.Ceiling: color=new Color(.86f,.85f,.81f);break;
                case Finish.ParkGreen: color=new Color(.25f,.43f,.26f);break;
                case Finish.Hedge: color=new Color(.36f,.52f,.23f);texture="Foliage";break;
                case Finish.Asphalt: color=new Color(.62f,.60f,.56f);texture="Asphalt";break;
                default: color=new Color(.79f,.78f,.73f);break;
            }
            var m=new Material(RuntimeShaders.Lit()) { name="Омск · "+finish,color=color };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor",color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness",gloss);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness",gloss);
            if (texture!=null) StreetMaterials.Apply(m,texture);
            if (finish==Finish.Glass || finish==Finish.GlassShade)
            {
                m.SetFloat("_Surface",1f);
                m.SetFloat("_Mode",2f);
                m.SetFloat("_Cull",0f);
                m.SetFloat("_SrcBlend",(int)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite",0f);
                m.SetOverrideTag("RenderType","Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.SetShaderPassEnabled("ShadowCaster",false);
                m.renderQueue=(int)RenderQueue.Transparent;
            }
            Finishes[(int)finish]=m;
            return m;
        }

        private sealed class Building
        {
            private readonly Transform root;
            private readonly Dictionary<Finish,Geometry> groups=new Dictionary<Finish,Geometry>();

            internal Building(Transform root) { this.root=root; }

            private Geometry Group(Finish finish)
            {
                if (!groups.TryGetValue(finish,out Geometry geometry))
                {
                    geometry=new Geometry();
                    groups.Add(finish,geometry);
                }
                return geometry;
            }

            internal void Box(Face frame,Finish finish,float x0,float x1,float y0,float y1,float z0,
                float z1,bool collision=false)
            {
                if(x1-x0<.0001f||y1-y0<.0001f||z1-z0<.0001f)
                    return;
                Vector3 a=frame.At(x0,y0,z0),b=frame.At(x1,y0,z0),c=frame.At(x1,y0,z1),d=frame.At(x0,y0,z1);
                Vector3 e=frame.At(x0,y1,z0),f=frame.At(x1,y1,z0),g=frame.At(x1,y1,z1),h=frame.At(x0,y1,z1);
                Geometry mesh=Group(finish);
                mesh.Quad(d,c,g,h);mesh.Quad(b,a,e,f);mesh.Quad(a,d,h,e);
                mesh.Quad(c,b,f,g);mesh.Quad(h,g,f,e);mesh.Quad(a,b,c,d);
                if (!collision)
                    return;
                var obj=new GameObject("Коллизия",typeof(BoxCollider));
                obj.transform.SetParent(root,false);
                obj.transform.localPosition=frame.At((x0+x1)*.5f,(y0+y1)*.5f,(z0+z1)*.5f);
                obj.transform.localRotation=Quaternion.LookRotation(frame.Normal,Vector3.up);
                obj.GetComponent<BoxCollider>().size=new Vector3(x1-x0,y1-y0,z1-z0);
            }

            internal void Surface(Finish finish,List<Vector2> ring,List<int> indices,float y,bool top)
            {
                Geometry mesh=Group(finish);
                int offset=mesh.Vertices.Count;
                foreach (Vector2 point in ring)
                {
                    mesh.Vertices.Add(new Vector3(point.x,y,point.y));
                    mesh.UV.Add(point*.32f);
                }
                for (int i=0;i<indices.Count;i+=3)
                {
                    mesh.Indices.Add(offset+indices[i]);
                    mesh.Indices.Add(offset+indices[top?i+1:i+2]);
                    mesh.Indices.Add(offset+indices[top?i+2:i+1]);
                }
            }

            internal void Brace(Face frame,Finish finish,float x0,float y0,float x1,float y1,float z,float radius)
            {
                Vector2 offset = new Vector2(y0-y1,x1-x0);
                Vector2 normal = offset*(radius/offset.magnitude);
                var a=frame.At(x0-normal.x,y0-normal.y,z);
                var b=frame.At(x0+normal.x,y0+normal.y,z);
                var c=frame.At(x1+normal.x,y1+normal.y,z);
                var d=frame.At(x1-normal.x,y1-normal.y,z);
                Geometry mesh=Group(finish);
                mesh.Quad(a,b,c,d);
                mesh.Quad(d,c,b,a);
            }

            internal void Sign(Face frame,string label,float x,float y,float z,float size,Color color)
            {
                if (!signFont)
                    signFont=Resources.Load<Font>("Art/DejaVuSans") ??
                        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (!signFont)
                    return;
                var obj=new GameObject("Вывеска · "+label,typeof(TextMesh));
                obj.transform.SetParent(root,false);
                obj.transform.localPosition=frame.At(x,y,z);
                obj.transform.localRotation=Quaternion.LookRotation(-frame.Normal,Vector3.up);
                TextMesh text=obj.GetComponent<TextMesh>();
                text.font=signFont;
                text.fontSize=56;
                text.characterSize=size;
                text.color=color;
                text.text=label;
                text.anchor=TextAnchor.MiddleCenter;
                text.alignment=TextAlignment.Center;
                var renderer=obj.GetComponent<MeshRenderer>();
                renderer.sharedMaterial=signFont.material;
                renderer.shadowCastingMode=ShadowCastingMode.Off;
            }

            internal void LobbyLight(Vector3 position)
            {
                var obj=new GameObject("Свет в вестибюле",typeof(Light));
                obj.transform.SetParent(root,false);
                obj.transform.localPosition=position;
                var light=obj.GetComponent<Light>();
                light.type=LightType.Point;
                light.range=10.4f;
                light.intensity=2.15f;
                light.color=new Color(1f,.92f,.82f);
                light.shadows=LightShadows.None;
            }

            internal void Publish()
            {
                foreach(var pair in groups)
                {
                    var obj=new GameObject("ТЦ · "+pair.Key,typeof(MeshFilter),typeof(MeshRenderer));
                    obj.transform.SetParent(root,false);
                    var mesh=new Mesh{name=obj.name};
                    Geometry data=pair.Value;
                    if(data.Vertices.Count>65535)
                        mesh.indexFormat=IndexFormat.UInt32;
                    mesh.SetVertices(data.Vertices);
                    mesh.SetUVs(0,data.UV);
                    mesh.SetTriangles(data.Indices,0);
                    mesh.RecalculateNormals();
                    mesh.RecalculateTangents();
                    mesh.RecalculateBounds();
                    obj.GetComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=obj.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial=MaterialFor(pair.Key);
                    if(pair.Key==Finish.Glass||pair.Key==Finish.GlassShade)
                        renderer.shadowCastingMode=ShadowCastingMode.Off;
                    mesh.UploadMeshData(true);
                }
            }
        }

        private sealed class Geometry
        {
            internal readonly List<Vector3> Vertices=new List<Vector3>();
            internal readonly List<Vector2> UV=new List<Vector2>();
            internal readonly List<int> Indices=new List<int>();

            internal void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int index=Vertices.Count;
                Vertices.Add(a);Vertices.Add(b);Vertices.Add(c);Vertices.Add(d);
                float w=Vector3.Distance(a,b)*.32f,h=Vector3.Distance(a,d)*.32f;
                UV.Add(Vector2.zero);UV.Add(new Vector2(w,0f));UV.Add(new Vector2(w,h));UV.Add(new Vector2(0f,h));
                Indices.Add(index);Indices.Add(index+1);Indices.Add(index+2);
                Indices.Add(index);Indices.Add(index+2);Indices.Add(index+3);
            }
        }
    }
}
