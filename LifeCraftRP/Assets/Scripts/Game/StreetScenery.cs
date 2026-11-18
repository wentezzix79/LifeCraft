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
        private readonly Font signFont;

        internal StreetScenery(Transform parent,Dictionary<string,Material> materials)
        {
            this.parent=parent;
            this.materials=materials;
            signFont=Resources.Load<Font>("Art/DejaVuSans");
            AddTint("oxideSign","eave",new Color(.43f,.125f,.089f));
            AddTint("signFace","navy",new Color(.075f,.24f,.32f));
            AddTint("birchPale","windowFrame",new Color(.78f,.75f,.66f));
            AddTint("birchKnot","bark",new Color(.17f,.16f,.14f));
            AddTint("panelJoint","facadeBand",new Color(.40f,.37f,.33f));
            AddTint("paverTone","paving",new Color(.69f,.64f,.59f));
            AddTint("aluminium","railMetal",new Color(.53f,.54f,.53f));
        }

        private void AddTint(string key,string source,Color tint)
        {
            var material=new Material(materials[source]) {name="LC · "+key,color=tint};
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor",tint);
            materials[key]=material;
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
            GroundFloorRetail(-66,10.28f);
            ServiceFronts();

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
            Wayfinding();
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
            RoofDetails(x,z,width,depth,height,style);
            AddCollision(x,height*.5f,z,width,height,depth);
            int bays=Mathf.FloorToInt(width/2.42f);
            for(int facing=-1;facing<=1;facing+=2)
            {
                float face=z+depth*.5f*facing;
                for(int floor=0;floor<floors;floor++)
                {
                    float y=1.64f+floor*2.73f;
                    if (style%3!=1)
                        Box("panelJoint",x,y+1.25f,face+facing*.008f,width,.036f,.026f);
                    if (floor!=0 && (style%3==0 || floor%2==0))
                        Box("facadeBand",x,y-.97f,face+facing*.022f,width,.067f,.064f);
                    for(int b=0;b<bays;b++)
                    {
                        float xx=x-width*.5f+(b+.5f)*width/bays;
                        bool entrance=floor==0 && b==bays/2;
                        if(entrance)
                        {
                            ResidenceEntry(xx,face,facing,style);
                            continue;
                        }
                        string glass=(b+floor*3+style)%6==0?"glassWarm":
                            (b+style+floor)%2==0?"glassBlue":"glassDeep";
                        Window(xx,y,face,facing,glass,(b+style)%3);
                        if(floor>0 && (style+b)%4==0)
                            Balcony(xx,y,face,facing,(floor+b+style)%4);
                        else if (floor>0 && (floor*3+b+style)%11==0)
                            AirConditioner(xx+.75f,y-.41f,face,facing);
                        if (style%3!=1 && floor>0 && b<bays-1 && (b+floor+style)%3!=1)
                            Box("panelJoint",xx+width/(bays*2f),y+.03f,
                                face+facing*.012f,.023f,2.63f,.019f);
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

        private void Window(float x,float y,float z,int facing,string glass,int layout)
        {
            float width = layout==2 ? 1.18f : 1.42f;
            Box("railMetal",x,y,z+facing*.044f,width,1.41f,.094f);
            Box(glass,x,y,z+facing*.102f,width-.11f,1.28f,.032f);
            for (int side=-1;side<=1;side+=2)
            {
                Box("windowFrame",x+side*(width*.5f-.043f),y,z+facing*.125f,.053f,1.31f,.030f);
                Box("windowFrame",x,y+side*.638f,z+facing*.128f,width-.052f,.052f,.031f);
            }
            Box("windowFrame",x+(layout==1 ? -.19f : .035f),y,z+facing*.129f,
                .043f,1.25f,.028f);
            if (layout!=2)
                Box("windowFrame",x,y+.29f,z+facing*.131f,width-.11f,.035f,.028f);
            Box("border",x,y-.68f,z+facing*.179f,width+.11f,.077f,.32f);
        }

        private void ResidenceEntry(float x,float z,int facing,int style)
        {
            string tile = style%2==0 ? "border" : "panelSand";
            Box(tile,x,1.36f,z+facing*.055f,2.14f,2.68f,.12f);
            Box("railMetal",x,1.21f,z+facing*.135f,1.46f,2.36f,.070f);
            Box("glassDeep",x-.23f,1.57f,z+facing*.178f,.48f,.86f,.035f);
            Box("glassDeep",x+.23f,1.57f,z+facing*.178f,.48f,.86f,.035f);
            Box("aluminium",x,1.15f,z+facing*.197f,.034f,.29f,.05f);
            Box("border",x,.11f,z+facing*.65f,2.28f,.22f,1.38f);
            Box("panelIvory",x,2.65f,z+facing*.55f,2.47f,.14f,1.19f);
            Box("railMetal",x,2.54f,z+facing*1.105f,2.36f,.049f,.053f);
            Box("signFace",x+.87f,2.27f,z+facing*.144f,.27f,.24f,.026f);
            Box("light",x,2.51f,z+facing*.47f,.34f,.035f,.17f);
        }

        private void Balcony(float x,float y,float z,int side,int variant)
        {
            Box("border",x,y-.78f,z+side*.55f,2.01f,.145f,1.08f);
            if (variant<2)
            {
                Box(variant==0?"glassBlue":"glassDeep",x,y+.031f,z+side*1.066f,1.91f,1.56f,.048f);
                Box("balconyPanel",x,y-.42f,z+side*1.095f,1.93f,.57f,.051f);
                Box("windowFrame",x,y+.86f,z+side*.60f,2.03f,.07f,1.02f);
                for(int i=-1;i<=1;i++)
                    Box("windowFrame",x+i*.92f,y+.29f,z+side*1.11f,.048f,1.02f,.05f);
                Box("windowFrame",x,y+.54f,z+side*1.117f,1.94f,.047f,.048f);
                for (int f=-1;f<=1;f+=2)
                {
                    Box("balconyPanel",x+f*.973f,y-.43f,z+side*.65f,.047f,.61f,.83f);
                    Box("glassBlue",x+f*.973f,y+.30f,z+side*.65f,.046f,.83f,.83f);
                }
            }
            else
            {
                Box("railMetal",x,y+.11f,z+side*1.06f,1.94f,.057f,.064f);
                Box("balconyPanel",x,y-.39f,z+side*1.04f,1.85f,.62f,.065f);
                for(int i=-1;i<=1;i+=2)
                {
                    Box("railMetal",x+i*.95f,y-.32f,z+side*1.04f,.052f,.83f,.052f);
                    Box("railMetal",x+i*.95f,y+.11f,z+side*.56f,.044f,.046f,.98f);
                }
            }
        }

        private void AirConditioner(float x,float y,float z,int facing)
        {
            Box("panelIvory",x,y,z+facing*.27f,.54f,.37f,.28f);
            Box("eave",x,y,z+facing*.419f,.43f,.23f,.021f);
            for(int vane=0;vane<4;vane++)
                Box("aluminium",x,y-.083f+vane*.054f,z+facing*.435f,.397f,.015f,.015f);
        }

        private void RoofDetails(float x,float z,float width,float depth,float height,int style)
        {
            for(int side=-1;side<=1;side+=2)
            {
                Box("panelSand",x,height+.31f,z+side*(depth*.5f-.06f),width+.08f,.44f,.18f);
                Box("eave",x,height+.55f,z+side*(depth*.5f-.06f),width+.12f,.065f,.23f);
                Box("panelSand",x+side*(width*.5f-.08f),height+.31f,z,.185f,.44f,depth);
            }
            for(int i=0;i<2;i++)
            {
                float tower=x-width*.32f+i*width*.41f;
                Box("panelSand",tower,height+.38f,z+.80f+i*1.22f,.78f,.72f,.65f);
                Box("eave",tower,height+.77f,z+.80f+i*1.22f,.87f,.105f,.74f);
            }
            if (style%2==0)
            {
                float mastX=x+width*.21f,mastZ=z-1.75f;
                Tube("railMetal",new Vector3(mastX,height+1.25f,mastZ),
                    new Vector3(mastX,height+2.89f,mastZ),.023f,.016f);
                Box("railMetal",mastX,height+2.74f,mastZ,1.16f,.031f,.036f);
                for(int i=-1;i<=1;i++)
                    Box("railMetal",mastX+i*.39f,height+2.74f,mastZ,.027f,.029f,.52f);
            }
        }

        private void ServiceFronts()
        {
            // Store geometry stays outside the real window and door collision planes.
            Pavement(49,.115f,-8.30f,19.66f,.15f,2.65f);
            Box("border",41.79f,.24f,-7.21f,5.58f,.40f,.13f);
            Box("border",53.21f,.24f,-7.21f,11.58f,.40f,.13f);
            Box("oxideSign",49,3.82f,-7.296f,19.95f,.84f,.20f);
            Box("eave",49,4.29f,-7.525f,20.14f,.13f,.67f);
            Sign("ЛАВКА У ДОМА",49,3.91f,-7.409f,.167f);
            Sign("ПРОДУКТЫ  •  ЕЖЕДНЕВНО",49,3.64f,-7.411f,.059f);
            float left=48.595f,right=57.82f,width=right-left;
            Box("railMetal",(left+right)*.5f,2.877f,-7.219f,width,.094f,.11f);
            Box("railMetal",(left+right)*.5f,.747f,-7.219f,width,.094f,.11f);
            Box("windowFrame",(left+right)*.5f,2.394f,-7.231f,width,.041f,.082f);
            for(int pane=0;pane<=4;pane++)
                Box("railMetal",Mathf.Lerp(left,right,pane/4f),1.81f,-7.22f,.079f,2.22f,.114f);
            Portal(46,-7,2.85f,"railMetal");
            Box("signFace",50.17f,1.062f,-7.255f,.77f,.325f,.053f);
            Sign("ОТКРЫТО",50.17f,1.066f,-7.288f,.059f);

            // Canopy finishes leave ample headroom and do not intrude in door swings.
            Portal(-27,2,2.85f,"aluminium");
            Box("eave",-27,3.13f,1.26f,4.50f,.19f,1.72f);
            Box("oxideSign",-27,3.095f,.385f,4.49f,.12f,.062f);
            Box("light",-27,3.023f,1.23f,2.94f,.040f,.38f);
            for (int i=0;i<11;i++)
            {
                float x=-51+i*4.19f;
                if(Mathf.Abs(x+27)<3.0f)
                    continue;
                Box("facadeBand",x,4.46f,1.794f,.055f,3.60f,.043f);
            }
            Box("aluminium",-30,6.32f,1.76f,44.30f,.108f,.135f);
            Portal(51,-25.5f,2.85f,"aluminium");
            Box("aluminium",53,3.16f,-26.063f,20.92f,.128f,1.22f);
            Box("eave",53,3.08f,-26.57f,20.95f,.076f,.15f);
            for(int i=0;i<3;i++)
            {
                float x=55.1f+i*2.6f;
                Box("railMetal",x,1.60f,-25.705f,2.34f,2.17f,.12f);
                Box(i%2==0?"glassBlue":"glassDeep",x,1.65f,-25.784f,2.18f,2.00f,.039f);
                Box("aluminium",x,1.65f,-25.809f,.063f,2.01f,.046f);
            }
            Portal(55,25.5f,2.85f,"border");
            Box("eave",55,2.984f,24.96f,3.80f,.125f,1.26f);
        }

        private void Portal(float x,float z,float height,string finish)
        {
            for(int side=-1;side<=1;side+=2)
                Box(finish,x+side*1.30f,height*.49f,z-.22f,.19f,height*.96f,.19f);
            Box(finish,x,height-.008f,z-.215f,2.78f,.128f,.21f);
        }

        private void GroundFloorRetail(float x,float z)
        {
            Box("panelSand",x,1.57f,z,16.93f,3.14f,.128f);
            Box("oxideSign",x,2.90f,z-.112f,16.82f,.54f,.144f);
            Box("eave",x,3.211f,z-.163f,17.02f,.12f,.294f);
            Sign("ПЕКАРНЯ  •  КОФЕ",x,2.922f,z-.194f,.126f);
            for(int bay=-3;bay<=3;bay++)
            {
                float xpos=x+bay*2.26f;
                Box("railMetal",xpos,1.37f,z-.097f,1.97f,2.12f,.095f);
                Box(bay%2==0?"glassDeep":"glassWarm",xpos,1.52f,z-.158f,1.84f,
                    bay==0?1.84f:1.62f,.044f);
                Box("windowFrame",xpos,1.52f,z-.193f,.061f,1.63f,.041f);
                Box("aluminium",xpos,2.30f,z-.192f,1.84f,.039f,.039f);
            }
        }

        private void Wayfinding()
        {
            Vector3 stop=new Vector3(-18.02f,.21f,-25.35f);
            Tube("railMetal",stop,stop+Vector3.up*3.04f,.036f,.031f);
            Box("signFace",stop.x,3.08f,stop.z,.57f,.53f,.069f);
            Box("navy",stop.x,3.08f,stop.z-.042f,.426f,.397f,.021f);
            Sign("А",stop.x,3.082f,stop.z-.062f,.115f);
            Box("railMetal",-13.48f,1.50f,-25.37f,1.16f,2.35f,.13f);
            Box("signFace",-13.48f,1.60f,-25.451f,.99f,1.88f,.024f);
            Sign("ОМСК", -13.48f,2.15f,-25.473f,.105f);
            Sign("ГОРОДСКОЙ МАРШРУТ", -13.48f,1.83f,-25.474f,.043f);
            for(int i=0;i<3;i++)
                Box("windowFrame",-13.48f,1.62f-i*.17f,-25.47f,.60f-i*.075f,.032f,.018f);
            for(int i=0;i<3;i++)
            {
                float x=-21.5f+i*1.14f;
                Box("paverTone",x,.153f,-25.72f,.78f,.069f,.57f);
                for(int row=0;row<3;row++)
                for(int col=0;col<5;col++)
                    Box("border",x-.262f+col*.132f,.195f,-25.876f+row*.156f,.070f,.023f,.079f);
            }
        }

        private void Sign(string label,float x,float y,float z,float scale)
        {
            if (!signFont)
                return;
            var obj=new GameObject("Вывеска · "+label,typeof(TextMesh));
            obj.transform.SetParent(parent,false);
            obj.transform.localPosition=new Vector3(x,y,z);
            var text=obj.GetComponent<TextMesh>();
            text.font=signFont;
            text.text=label;
            text.fontSize=52;
            text.characterSize=scale;
            text.color=new Color(.93f,.92f,.87f);
            text.anchor=TextAnchor.MiddleCenter;
            text.alignment=TextAlignment.Center;
            obj.GetComponent<MeshRenderer>().sharedMaterial=signFont.material;
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
            bool birch=Mathf.Abs(Mathf.RoundToInt(x*1.31f+z*.9f))%4<2;
            string bark=birch?"birchPale":"bark";
            Tube(bark,bottom,joint,.17f*s,.073f*s);
            Tube(bark,joint,joint+new Vector3(.12f,1.40f,.04f)*s,.073f*s,.024f*s);
            if (birch)
                BirchScars(bottom,joint,.17f*s,.073f*s);
            for(int i=0;i<7;i++)
            {
                float a=i*2.4f+(float)variation.NextDouble()*.9f;
                Vector3 branch=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                float r=(.75f+(float)variation.NextDouble()*.6f)*s;
                Vector3 branchStart=joint-Vector3.up*(.26f+(i%3)*.23f)*s;
                Vector3 crown=joint+branch*r+Vector3.up*(.27f+i*.23f)*s;
                Tube(bark,branchStart,crown,.059f*s,.016f*s);
                Crown(i%3==0?"leavesShade":i%3==1?"leavesMoss":"leavesTop",crown,
                      new Vector3(1.17f,1.31f,1.12f)*s,6,11);
                Crown(i%2==0?"leavesMoss":"leavesTop",crown+branch*.62f*s+Vector3.up*.33f*s,
                      new Vector3(.69f,.81f,.65f)*s,5,9);
            }
            Crown("leavesTop",joint+Vector3.up*2.02f*s,new Vector3(1.03f,1.23f,.99f)*s,6,11);
        }

        private void BirchScars(Vector3 start,Vector3 end,float lower,float upper)
        {
            Geometry geometry=Get("birchKnot");
            Vector3 axis=(end-start).normalized;
            Vector3 radial=Vector3.Cross(axis,Vector3.forward).normalized;
            Vector3 tangent=Vector3.Cross(radial,axis);
            for(int i=0;i<17;i++)
            {
                float t=.073f+i*.049f;
                float angle=i*2.4f+(float)variation.NextDouble()*.8f;
                Vector3 from=(radial*Mathf.Cos(angle)+tangent*Mathf.Sin(angle));
                Vector3 across=(radial*Mathf.Cos(angle+.57f)+tangent*Mathf.Sin(angle+.57f));
                float radius=Mathf.Lerp(lower,upper,t)+.006f;
                var anchor=Vector3.Lerp(start,end,t);
                var a=anchor+from*radius;
                var b=anchor+across*radius;
                var c=b+axis*(.040f+.019f*(i%3));
                var d=a+axis*(.027f+.014f*(i%2));
                geometry.Quad(a,b,c,d);
            }
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
                    float variation=1f+.10f*Mathf.Sin(a*4+row*1.5f+seed)
                        +.045f*Mathf.Cos(a*7-row*1.43f-seed);
                    var v=new Vector3(Mathf.Cos(a)*r*radii.x,h*radii.y,Mathf.Sin(a)*r*radii.z)*variation;
                    g.Points.Add(center+v);
                    g.UV.Add(new Vector2(i*2f/sides+seed,row/(float)rows+seed*.62f));
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
