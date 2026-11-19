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
                    float portalWidth = Mathf.Min(frame.Length-4f,mega ? 14.4f : 28f);
                    float mid = Mathf.Clamp(doorCentre,portalWidth*.5f+1f,frame.Length-portalWidth*.5f-1f);
                    float left = mid-portalWidth*.5f, right = mid+portalWidth*.5f;
                    Wall(model,frame,0f,left,height,mega,i);
                    Wall(model,frame,right,frame.Length,height,mega,i);
                    if (mega)
                        MegaEntrance(model,frame,mid,portalWidth,height);
                    else
                    {
                        ParkEntrance(model,frame,mid,portalWidth,height);
                        float roofSignX=Mathf.Min(mid+portalWidth*.95f,frame.Length-7.2f);
                        OmskMallSigns.ParkRoof(root,frame.At(roofSignX,19.12f,.82f),frame.Normal,13.1f);
                    }
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
                MegaWing(model,frame,left,right,height);
                return;
            }
            model.Box(frame,Finish.SheetShade,left,right,Ground,.83f,-.06f,.15f);
            model.Box(frame,Finish.Trim,left,right,4.23f,4.40f,-.08f,.13f);
            model.Box(frame,Finish.ParkOrange,left,right,16.67f,16.88f,-.04f,.19f);
            int windows = Mathf.Max(1,Mathf.CeilToInt(width/1.85f));
            for (int col=0;col<windows;col++)
            {
                float x0 = left+width*col/windows;
                float x1 = left+width*(col+1)/windows;
                Finish glass = (col+side)%4 == 0 ? Finish.GlassShade : Finish.Glass;
                model.Box(frame,glass,x0+.027f,x1-.027f,.86f,4.23f,.006f,.095f);
                model.Box(frame,Finish.Trim,x0+.012f,x0+.067f,.81f,4.25f,.04f,.124f);
            }
            model.Box(frame,Finish.Aluminium,left,right,2.52f,2.58f,.064f,.122f);
            ParkMosaic(model,frame,left,right,4.43f,16.61f,side);
        }

        private static void ParkMosaic(Building model,Face frame,float left,float right,float bottom,float top,int face)
        {
            int rows = Mathf.CeilToInt((top-bottom)/.48f);
            for (int row=0;row<rows;row++)
            {
                float y0=Mathf.Lerp(bottom,top,row/(float)rows);
                float y1=Mathf.Lerp(bottom,top,(row+1)/(float)rows);
                float x = left;
                for (int col=0;x<right-.012f;col++)
                {
                    int pattern=(col*19+row*11+col*row*7+face*23)%37;
                    float panelWidth=.74f+(pattern%6)*.29f;
                    float next=Mathf.Min(x+panelWidth,right);
                    model.ParkCladdingFace(frame,ParkPanel(pattern),x+.009f,next-.009f,
                        y0+.010f,y1-.010f,.092f+(pattern%3)*.009f);
                    x=next;
                }
            }
        }

        private static Finish ParkPanel(int index)
        {
            if (index < 2) return Finish.ParkBurgundy;
            if (index < 6) return Finish.ParkRust;
            if (index < 8) return Finish.ParkTerracotta;
            if (index < 10) return Finish.ParkLemon;
            if (index < 15) return Finish.ParkSand;
            if (index < 19) return Finish.ParkOchre;
            if (index < 24) return Finish.Sheet;
            return index < 32 ? Finish.SheetShade : Finish.ParkWall;
        }

        private static void ParkEntrance(Building model,Face frame,float mid,float width,float height)
        {
            float left = mid-width*.5f, right = mid+width*.5f;
            model.Box(frame,Finish.ParkWall,left,right,16.12f,height,-.30f,0,true);
            model.Box(frame,Finish.ParkOrange,left-.66f,right+.66f,16.25f,17.95f,-.42f,.83f);
            for (int side=-1;side<=1;side+=2)
            {
                float x = mid+side*width*.5f;
                model.Box(frame,Finish.ParkOrange,x-.63f,x+.63f,Ground,17.90f,-.37f,.81f);
                model.Box(frame,Finish.ParkTerracotta,x-.62f,x-.55f,Ground+.05f,16.24f,.815f,.854f);
            }
            model.Box(frame,Finish.Trim,left,right,4.46f,4.61f,-.68f,2.98f);
            model.Box(frame,Finish.ParkOrange,left-.44f,right+.44f,4.59f,4.81f,2.79f,3.05f);
            model.Box(frame,Finish.Ceiling,left+.43f,right-.43f,4.424f,4.453f,0f,2.77f);
            model.Box(frame,Finish.ParkPale,left+.55f,right-.55f,4.83f,7.05f,-.16f,.11f);
            for (int bay=0;bay<14;bay++)
            {
                float x0=Mathf.Lerp(left+.55f,right-.55f,bay/14f);
                float x1=Mathf.Lerp(left+.55f,right-.55f,(bay+1)/14f);
                for (int row=0;row<5;row++)
                {
                    float y0=Mathf.Lerp(7.05f,16.24f,row/5f);
                    float y1=Mathf.Lerp(7.05f,16.24f,(row+1)/5f);
                    Finish glass=(bay*3+row*7)%13<4 ? Finish.GlassShade : Finish.Glass;
                    model.Box(frame,glass,x0+.022f,x1-.022f,y0+.025f,y1-.025f,-.065f,.10f);
                }
                model.Box(frame,Finish.Aluminium,x0-.030f,x0+.030f,4.82f,16.24f,.086f,.157f);
            }
            for (int row=0;row<5;row++)
            {
                float y=Mathf.Lerp(7.05f,16.24f,row/5f);
                model.Box(frame,Finish.Aluminium,left+.55f,right-.55f,y-.037f,y+.037f,.094f,.169f);
            }
            float leftWing=frame.Length-right;
            float rightWing=left;
            ParkTenant(model,frame,"kari ГИПЕР",right+leftWing*.57f,13.21f,leftWing*.42f,Finish.ParkBurgundy,.78f);
            ParkTenant(model,frame,"COFFEE ANYTIME",right+leftWing*.72f,11.52f,leftWing*.45f,Finish.SheetShade,.47f);
            ParkTenant(model,frame,"ЭКСПРЕСС СУШИ",right+leftWing*.27f,11.43f,leftWing*.42f,Finish.ParkBurgundy,.48f);
            ParkTenant(model,frame,"ФАРМАКОПЕЙКА",right+leftWing*.63f,9.70f,leftWing*.51f,Finish.ParkGreen,.52f);
            ParkTenant(model,frame,"KOREANBULL",right+leftWing*.31f,7.49f,leftWing*.43f,Finish.Trim,.56f);
            ParkTenant(model,frame,"DOSMARCO",rightWing*.66f,12.40f,rightWing*.59f,Finish.ParkWall,.57f);
            ParkTenant(model,frame,"ЭКОНОМ",rightWing*.31f,10.74f,rightWing*.53f,Finish.ParkBlue,.58f);
            ParkTenant(model,frame,"РОССЮВЕЛИРТОРГ",rightWing*.54f,8.80f,rightWing*.87f,Finish.ParkBurgundy,.46f);
            for (int floor=0;floor<2;floor++)
            {
                float y = 8.89f+3.58f*floor;
                model.Box(frame,Finish.Ceiling,left+.45f,right-.45f,y,y+.17f,-5.5f,-.25f);
                model.Box(frame,Finish.SheetShade,left+.45f,right-.45f,y+.19f,y+.50f,-.28f,-.23f);
            }
            float grilleX=right+leftWing*.45f;
            model.Box(frame,Finish.Aluminium,grilleX-2.8f,grilleX+2.8f,height-.03f,height+.81f,-4.95f,-3.12f);
            for (int grille=0;grille<4;grille++)
            {
                float y=height+.11f+grille*.15f;
                model.Box(frame,Finish.Trim,grilleX-2.7f,grilleX+2.7f,y,y+.052f,-3.16f,-3.10f);
            }
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
            const float glassBottom = 4.05f, glassTop = 20.79f, glazing = .82f;
            const int columns = 9, rows = 9;
            const float upright = .085f;
            float paneHeight=(glassTop-glassBottom)/rows;
            model.Box(frame,Finish.MegaPanel,left-.27f,right+.27f,glassTop,21.42f,-6.75f,1.16f);
            for (int side=-1;side<=1;side+=2)
            {
                float edge = mid+side*width*.5f;
                model.Box(frame,Finish.MegaPanel,edge-.24f,edge+.24f,glassBottom,glassTop,-6.75f,glazing+.16f);
                model.Box(frame,Finish.MegaPanel,edge-.18f,edge+.18f,MegaDeck-.24f,glassBottom+.43f,-.28f,1.08f);
            }
            model.Box(frame,Finish.MegaPanel,left,right,height-.14f,glassTop,-6.58f,-6.34f);
            model.Box(frame,Finish.Ceiling,left+.22f,right-.22f,20.34f,20.43f,-6.45f,glazing-.11f);
            for (int col=0;col<columns;col++)
            {
                float x0=left+col*width/columns, x1=left+(col+1)*width/columns;
                for (int row=0;row<rows;row++)
                {
                    float y=glassBottom+row*paneHeight;
                    Finish glass=(col+row*3)%11 < 3 ? Finish.GlassShade : Finish.Glass;
                    model.Box(frame,glass,x0+.047f,x1-.047f,y+.047f,y+paneHeight-.047f,glazing-.045f,glazing+.045f);
                }
                model.Box(frame,Finish.Aluminium,x0-upright*.5f,x0+upright*.5f,
                    glassBottom,glassTop,glazing-.23f,glazing+.11f);
            }
            model.Box(frame,Finish.Aluminium,right-upright*.5f,right+upright*.5f,
                glassBottom,glassTop,glazing-.23f,glazing+.11f);
            for (int row=0;row<=rows;row++)
            {
                float y=glassBottom+row*paneHeight;
                model.Box(frame,Finish.Aluminium,left,right,y-.043f,y+.043f,glazing-.19f,glazing+.105f);
            }
            model.Box(frame,Finish.SheetShade,left-.39f,right+.39f,3.67f,4.20f,-.29f,4.28f);
            model.Box(frame,Finish.Aluminium,left-.41f,right+.41f,3.65f,3.73f,4.23f,4.30f);
            model.Box(frame,Finish.Ceiling,left+.08f,right-.08f,3.63f,3.69f,.37f,4.25f);
            model.Sign(frame,"Вход 2",mid,3.94f,4.34f,.54f,White);
            MegaVestibule(model,frame,mid,width);
            Forecourt(model,frame,mid,width,7.6f);
            OmskMallSigns.MegaRoof(model.Root,frame.At(mid,23.55f,1.42f),frame.Normal,13f);
            const float wingSignWidth=10.2f, wingSignMargin=5.50f;
            float auchan = Mathf.Min(29f,frame.Length-mid-wingSignMargin);
            float mvideo = Mathf.Min(29f,mid-wingSignMargin);
            if (auchan > width*.5f+wingSignMargin)
                OmskMallSigns.MegaWing(model.Root,frame.At(mid+auchan,16.83f,.48f),frame.Normal,wingSignWidth,true);
            if (mvideo > width*.5f+wingSignMargin)
                OmskMallSigns.MegaWing(model.Root,frame.At(mid-mvideo,16.83f,.48f),frame.Normal,wingSignWidth,false);
            OmskMegaPlaza.Build(model.Root,model.Root.position+frame.At(mid,Ground,0f),frame.Normal);
        }

        private static void MegaWing(Building model,Face frame,float left,float right,float height)
        {
            float width=right-left;
            model.Box(frame,Finish.MegaPanel,left,right,MegaDeck+.03f,height-.22f,-.05f,.047f);
            model.Box(frame,Finish.SheetShade,left,right,MegaDeck-.36f,MegaDeck+.06f,-.22f,.15f);
            model.Box(frame,Finish.Aluminium,left,right,height-.25f,height-.16f,-.07f,.07f);
            int cells = Mathf.Max(1,Mathf.CeilToInt(width/4.12f));
            for (int cell=1;cell<cells;cell++)
            {
                float x=left+cell*width/cells;
                model.Box(frame,Finish.Aluminium,x-.02f,x+.02f,MegaDeck+.06f,height-.25f,.047f,.073f);
            }
            for (float y=MegaDeck+2.23f;y<height-.28f;y+=2.23f)
                model.Box(frame,Finish.Aluminium,left,right,y-.023f,y+.023f,.047f,.073f);

            int bays = Mathf.Max(1,Mathf.FloorToInt(width/8.7f));
            model.Box(frame,Finish.Trim,left,right,3.78f,MegaDeck-.34f,-3.12f,-.22f);
            for (int bay=1;bay<bays;bay++)
            {
                float x=left+bay*width/bays;
                model.Box(frame,Finish.MegaPanel,x-.32f,x+.32f,Ground,3.77f,-.67f,-.24f);
                model.Box(frame,Finish.Trim,x-.25f,x+.25f,Ground,3.72f,-3.39f,-2.77f);
            }
        }

        private static void MegaVestibule(Building model,Face frame,float mid,float width)
        {
            GlazedDoor(model,frame,mid,width,3.63f);
            for (int sign=-1;sign<=1;sign+=2)
            {
                // Side arcs frame an always-clear centre channel at the arrival heading.
                const float sideRadius=2.43f, outwardRadius=1.50f, drumCentre=1.94f;
                const float glassSill=Ground+.095f, glassHead=3.28f;
                const int pieces=4;
                for (int panel=0;panel<pieces;panel++)
                {
                    float a = -.66f+panel*1.32f/pieces, b = -.66f+(panel+1)*1.32f/pieces;
                    float x0=mid+sign*sideRadius*Mathf.Cos(a), x1=mid+sign*sideRadius*Mathf.Cos(b);
                    float z0=drumCentre+outwardRadius*Mathf.Sin(a), z1=drumCentre+outwardRadius*Mathf.Sin(b);
                    model.FacadeStrip(frame,Finish.Glass,x0,z0,x1,z1,glassSill,glassHead);
                    model.FacadeStrip(frame,Finish.Trim,x0,z0,x1,z1,glassSill-.071f,glassSill);
                    model.FacadeStrip(frame,Finish.Trim,x0,z0,x1,z1,glassHead,glassHead+.072f);
                    model.Box(frame,Finish.Aluminium,x0-.038f,x0+.038f,glassSill-.05f,glassHead+.072f,
                        z0-.038f,z0+.038f);
                }
                float endX=mid+sign*sideRadius*Mathf.Cos(.66f);
                float endZ=drumCentre+outwardRadius*Mathf.Sin(.66f);
                model.Box(frame,Finish.Aluminium,endX-.038f,endX+.038f,
                    glassSill-.05f,glassHead+.072f,endZ-.038f,endZ+.038f);
            }
            model.Box(frame,Finish.Trim,mid-2.61f,mid+2.61f,3.39f,3.56f,.57f,3.52f);
            model.Box(frame,Finish.Aluminium,mid-1.70f,mid+1.70f,3.09f,3.34f,2.83f,3.11f);
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
            Aluminium,Glass,GlassShade,Ceiling,ParkGreen,RoadMark,Asphalt,ParkRust,ParkLemon,ParkBlue,MegaPanel,
            ParkBurgundy,ParkTerracotta,ParkPale
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
                case Finish.ParkOrange: color=new Color(.94f,.32f,.065f);gloss=.43f;break;
                case Finish.ParkOchre: color=new Color(.64f,.45f,.31f);break;
                case Finish.ParkSand: color=new Color(.65f,.59f,.52f);break;
                case Finish.ParkRust: color=new Color(.54f,.23f,.18f);gloss=.32f;break;
                case Finish.ParkLemon: color=new Color(.70f,.54f,.33f);gloss=.32f;break;
                case Finish.ParkBlue: color=new Color(.12f,.34f,.59f);gloss=.25f;break;
                case Finish.ParkBurgundy: color=new Color(.32f,.125f,.126f);gloss=.37f;break;
                case Finish.ParkTerracotta: color=new Color(.73f,.34f,.21f);gloss=.34f;break;
                case Finish.ParkPale: color=new Color(.75f,.75f,.70f);gloss=.40f;break;
                case Finish.Sheet: color=new Color(.63f,.65f,.64f);gloss=.42f;break;
                case Finish.SheetShade: color=new Color(.41f,.43f,.43f);gloss=.32f;break;
                case Finish.Trim: color=new Color(.10f,.119f,.124f);gloss=.37f;break;
                case Finish.Aluminium: color=new Color(.67f,.71f,.72f);gloss=.50f;break;
                case Finish.Glass: color=new Color(.27f,.40f,.46f,.31f);gloss=.86f;break;
                case Finish.GlassShade: color=new Color(.22f,.32f,.38f,.47f);gloss=.77f;break;
                case Finish.Ceiling: color=new Color(.86f,.85f,.81f);break;
                case Finish.ParkGreen: color=new Color(.25f,.43f,.26f);break;
                case Finish.MegaPanel: color=new Color(.82f,.82f,.78f);gloss=.36f;break;
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
            internal Transform Root => root;

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

            internal void ParkCladdingFace(Face frame,Finish finish,float left,float right,float bottom,float top,float depth)
            {
                if (left>=right)
                    return;
                Group(finish).Quad(frame.At(left,bottom,depth),frame.At(right,bottom,depth),
                    frame.At(right,top,depth),frame.At(left,top,depth));
            }

            internal void FacadeStrip(Face frame,Finish finish,float x0,float z0,float x1,float z1,float y0,float y1)
            {
                var a=frame.At(x0,y0,z0);
                var b=frame.At(x1,y0,z1);
                var c=frame.At(x1,y1,z1);
                var d=frame.At(x0,y1,z0);
                Geometry mesh=Group(finish);
                mesh.Quad(a,b,c,d);
                if(finish!=Finish.Glass)
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
