using System;
using System.Collections.Generic;
using LifeCraftRP.Core;
using UnityEngine;

namespace LifeCraftRP
{
    public sealed class SwingDoor : MonoBehaviour
    {
        public bool Open { get; private set; }
        public void Toggle() => Open = !Open;
        private void Update()
        {
            Quaternion target = Quaternion.Euler(0,Open ? -103f : 0,0);
            transform.localRotation = Quaternion.RotateTowards(transform.localRotation,target,190f * Time.deltaTime);
        }
    }

    public sealed class SiteBuilder
    {
        private readonly GameManager game;
        private readonly Transform root;
        private readonly Dictionary<string,Material> materials = new Dictionary<string,Material>();
        private readonly Font font;
        private readonly MaterialPropertyBlock streetTiling = new MaterialPropertyBlock();
        private readonly Dictionary<string,float> surfaceTiling = new Dictionary<string,float>
        {
            {"asphalt",.226f}, {"walk",.58f}, {"wall",.34f},
            {"roof",.28f}, {"floor",.46f}, {"grass",.31f},
        };
        private static readonly Color OffWhite = new Color(.9f,.92f,.88f);
        private static readonly Color Mint = new Color(.32f,.90f,.73f);

        public SiteBuilder(GameManager game)
        {
            this.game = game;
            root = new GameObject("Игровой квартал · улица и интерьеры").transform;
            font = Resources.Load<Font>("Art/DejaVuSans") ??
                   Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Mat("asphalt", new Color(.17f,.20f,.22f), "Asphalt");
            Mat("walk", new Color(.49f,.52f,.50f), "Concrete");
            Mat("wall", new Color(.62f,.67f,.66f), "Concrete");
            Mat("roof", new Color(.30f,.34f,.38f), "Concrete");
            Mat("floor", new Color(.44f,.49f,.49f), "Concrete");
            Mat("navy", new Color(.08f,.18f,.25f), null, .2f);
            Mat("mint", Mint, null, .2f);
            Mat("light", OffWhite);
            Mat("roadmark", new Color(.79f,.76f,.62f));
            Mat("cargo", new Color(.47f,.32f,.18f), "Cardboard");
            Mat("dark", new Color(.08f,.11f,.13f), null, .4f);
            Mat("violet", new Color(.29f,.23f,.42f));
            Mat("rust", new Color(.65f,.28f,.17f));
            Mat("trunk", new Color(.28f,.20f,.15f));
            Mat("grass", new Color(.30f,.41f,.32f), "Concrete");
            Mat("screen",new Color(.20f,.54f,.55f),null,.42f);
            StreetMaterial("asphalt","Asphalt",new Color(.56f,.54f,.52f));
            StreetMaterial("walk","ConcretePavers",new Color(.77f,.73f,.68f));
            StreetMaterial("wall","WeatheredConcrete",new Color(.85f,.81f,.75f));
            StreetMaterial("roof","WeatheredConcrete",new Color(.48f,.48f,.46f));
            StreetMaterial("floor","WeatheredConcrete",new Color(.74f,.72f,.68f));
            StreetMaterial("grass","Grass",new Color(.76f,.78f,.62f));
            StreetMaterial("paving","ConcretePavers",new Color(.78f,.75f,.70f));
            StreetMaterial("panelIvory","WeatheredConcrete",new Color(.83f,.79f,.73f));
            StreetMaterial("panelSand","WeatheredConcrete",new Color(.78f,.72f,.65f));
            StreetMaterial("brickWall","Brick",new Color(.76f,.70f,.64f));
            StreetMaterial("yardGrass","Grass",new Color(.75f,.76f,.59f));
            StreetMaterial("roadPatch","Asphalt",new Color(.45f,.44f,.43f));
            StreetMaterial("border","WeatheredConcrete",new Color(.64f,.62f,.59f));
            Mat("eave",new Color(.30f,.30f,.29f));
            Mat("facadeBand",new Color(.50f,.49f,.47f));
            Mat("railMetal",new Color(.09f,.105f,.11f),null,.34f);
            Mat("glassBlue",new Color(.24f,.34f,.39f),null,.78f);
            Mat("glassDeep",new Color(.11f,.16f,.18f),null,.68f);
            Mat("glassWarm",new Color(.51f,.47f,.39f),null,.48f);
            Mat("windowFrame",new Color(.68f,.67f,.62f));
            Mat("balconyPanel",new Color(.31f,.36f,.37f),null,.36f);
            Mat("bark",new Color(.34f,.27f,.21f));
            Mat("leavesShade",new Color(.17f,.28f,.12f));
            Mat("leavesMoss",new Color(.30f,.41f,.19f));
            Mat("leavesTop",new Color(.44f,.49f,.25f));
        }

        private void StreetMaterial(string key,string texture,Color color)
        {
            Mat(key,color);
            StreetMaterials.Apply(materials[key],texture);
        }

        private void Mat(string key,Color color,string texture = null,float gloss = .07f)
        {
            Shader shader = RuntimeShaders.Lit();
            var mat = new Material(shader) { name = "LC · " + key, color = color };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor",color);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness",gloss);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness",gloss);
            if (texture != null)
            {
                Texture2D albedo = Resources.Load<Texture2D>("Art/" + texture);
                if (albedo != null)
                    mat.mainTexture = albedo;
            }
            materials[key] = mat;
        }

        private GameObject Primitive(string label, Vector3 position, Vector3 size,
                                     string material, bool solid, Transform parent, PrimitiveType kind)
        {
            GameObject obj = GameObject.CreatePrimitive(kind);
            obj.name = label;
            obj.transform.SetParent(parent ?? root, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = size;
            var renderer=obj.GetComponent<Renderer>();
            renderer.sharedMaterial=materials[material];
            if (kind == PrimitiveType.Cube && surfaceTiling.TryGetValue(material,out float repeat))
                StreetMaterials.SetTiling(renderer,streetTiling,
                    Mathf.Max(.15f,size.x*repeat),Mathf.Max(.15f,Mathf.Max(size.y,size.z)*repeat));
            if (!solid)
                obj.GetComponent<Collider>().enabled = false;
            return obj;
        }

        private GameObject Box(string label, float x,float y,float z,float w,float h,float d,
                               string material,bool solid = true,Transform parent = null)
        {
            return Primitive(label, new Vector3(x,y,z), new Vector3(w,h,d),
                             material, solid, parent, PrimitiveType.Cube);
        }

        private void Sign(string label,float x,float y,float z,float size,Color color = default)
        {
            var obj = new GameObject(label, typeof(TextMesh));
            obj.transform.SetParent(root);
            obj.transform.position = new Vector3(x,y,z);
            var sign = obj.GetComponent<TextMesh>();
            sign.font = font;
            sign.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            sign.text = label;
            sign.characterSize = size;
            sign.fontSize = 42;
            sign.anchor = TextAnchor.MiddleCenter;
            sign.alignment = TextAlignment.Center;
            sign.color = color == default ? OffWhite : color;
        }

        private void Room(string label,float x,float z,float w,float d,float h,
                          float doorX,string facade,bool roof = true)
        {
            float front = z - d/2;
            const float doorWidth = 2.45f;
            const float doorHeight = 2.85f;
            float start = x - w/2;
            float end = x + w/2;
            Box(label+" · пол",x,.11f,z,w,.20f,d,"floor");
            Box(label+" · задняя стена",x,h/2,z+d/2,w,h,.36f,facade);
            Box(label+" · левая стена",start,h/2,z,.33f,h,d,facade);
            Box(label+" · правая стена",end,h/2,z,.33f,h,d,facade);
            float leftWidth = doorX - doorWidth/2 - start;
            float rightWidth = end - doorX - doorWidth/2;
            Box(label+" · фасад",start+leftWidth/2,h/2,front,leftWidth,h,.35f,facade);
            Box(label+" · фасад",end-rightWidth/2,h/2,front,rightWidth,h,.35f,facade);
            Box(label+" · над дверью",doorX,(h+doorHeight)/2,front,doorWidth,h-doorHeight,.35f,facade);
            if (roof)
                Box(label+" · кровля",x,h+.16f,z,w+.45f,.32f,d+.45f,"roof");
            Sign(label,x,h-.65f,front-.205f,.185f);
        }

        private void Door(string label,float x,float z,Func<bool> canOpen = null)
        {
            var hinge = new GameObject(label, typeof(SwingDoor));
            hinge.transform.SetParent(root, false);
            hinge.transform.localPosition = new Vector3(x-1.18f,0,z);
            Box("Створка",1.16f,1.37f,0,2.32f,2.69f,.13f,"navy",true,hinge.transform);
            Box("Ручка",2.05f,1.15f,-.09f,.24f,.08f,.07f,"mint",false,hinge.transform);
            game.Interactions.Add(label,new Vector3(x,0,z),() =>
            {
                if (canOpen != null && !canOpen())
                {
                    game.Notify("Нужен действующий договор аренды.");
                    return;
                }
                game.Player.Animation.OpenDoor();
                hinge.GetComponent<SwingDoor>().Toggle();
            });
        }

        private void Spot(string label,float x,float z,Action action)
        {
            Primitive("Маркер · "+label,new Vector3(x,.235f,z),new Vector3(.63f,.027f,.63f),
                      "mint",false,root,PrimitiveType.Cylinder);
            game.Interactions.Add(label,new Vector3(x,.2f,z),action);
        }

        private void Lamp(float x,float z)
        {
            Box("Столб",x,2.5f,z,.125f,5f,.125f,"dark",false);
            Box("Кронштейн",x+.55f,4.95f,z,1.10f,.1f,.12f,"dark",false);
            Box("Светильник",x+1.08f,4.94f,z,.39f,.12f,.29f,"light",false);
        }

        public void Build()
        {
            Box("Асфальт · продольная улица",0,.12f,-36,228,.12f,13.4f,"asphalt");
            Box("Асфальт · проезд",17,.121f,0,9.4f,.12f,199f,"asphalt");
            foreach(float z in new [] { -44.1f,-27.8f })
            {
                Box("Тротуар",-51,.17f,z,126,.18f,2.2f,"walk");
                Box("Тротуар",68,.17f,z,92,.18f,2.2f,"walk");
            }
            for (int x = -108; x < 111; x += 8)
            {
                if (Math.Abs(x-17) > 12)
                    Box("Разметка",x,.188f,-36,3.2f,.014f,.16f,"roadmark",false);
            }
            for (int x = -101; x < 112; x += 17)
            {
                Lamp(x,-46);

            }
            for (int x = 0; x < 10; x++)
                Box("Пешеходный переход",10+x*.59f,.192f,-36,.31f,.014f,5.7f,"roadmark",false);

            Warehouse();
            Services();
            Apartments();
            new StreetScenery(root,materials).Build();
        }

        private void Warehouse()
        {
            Room("LC ЛОГИСТИКА",-30,24,44,44,6.6f,-27,"navy");
            Door("Дверь склада",-27,2);
            Box("Вход на склад",-27,.18f,-.7f,5,.20f,4.9f,"walk");
            Box("Зона разгрузки",-29,.09f,49,46,.13f,5.7f,"asphalt",false);
            for (int i=0;i<4;i++)
                Box("Погрузочные ворота",-45+i*9,2.1f,46.22f,6.2f,4.1f,.18f,"roof",false);

            Box("Стойка приёма",-39,.59f,8.8f,4,1.18f,.7f,"navy");
            Sign("ПРИЁМ СОТРУДНИКОВ",-39,1.72f,8.34f,.098f);
            Spot("Устроиться на склад",-39,10,()=>game.UseWarehouse(WarehouseStation.Employment));
            for (int i=0;i<3;i++)
            {
                Box("Шкаф формы",-48,1,15.3f+i*1.4f,1.15f,2,1.27f,"roof");
                Box("Ручка шкафа",-47.37f,1.02f,15.4f+i*1.4f,.08f,.23f,.13f,"mint",false);
            }
            Spot("Получить рабочую форму",-46,17,()=>game.UseWarehouse(WarehouseStation.Locker));
            Box("Терминал · стойка",-40,.56f,22.9f,1.1f,1.1f,.72f,"dark");
            Box("Терминал · экран",-40,1.33f,22.85f,.64f,.56f,.17f,"screen");
            Spot("Терминал заказов",-40,24,()=>game.UseWarehouse(WarehouseStation.Terminal));

            for (int i=0;i<JobManager.ShelfCount;i++)
            {
                int shelf = i;
                float x = -36+i*7;
                for (int a=-1;a<=1;a+=2)
                    for(int b=-1;b<=1;b+=2)
                        Box("Стойка стеллажа",x+a*2.1f,1.64f,30.2f+b*.75f,.16f,3.27f,.16f,"rust");
                for (int row=0;row<3;row++)
                {
                    float y=.44f+row*1.05f;
                    Box("Полка",x,y,30.2f,4.35f,.11f,1.67f,"dark");
                    for (int crate=0;crate<4;crate++)
                        Box("Коробка",x-1.55f+crate*1.03f,y+.35f,30.10f,
                            .68f,.58f,.87f,"cargo",false);
                }
                Sign("СТЕЛЛАЖ "+(i+1),x,3.46f,29.25f,.092f);
                Spot("Сканировать / взять коробку №"+(i+1),x,27,
                     ()=>game.UseWarehouse(WarehouseStation.Shelf,shelf));
            }
            for(int i=0;i<6;i++)
            {
                Box("Паллета",-45+i*5.1f,.20f,40.75f,2.2f,.25f,1.4f,"trunk");
                Box("Штабель",-45+i*5.1f,.76f,40.75f,2.1f,.87f,1.3f,"cargo");
            }
            Box("Упаковочный стол",-12,.63f,22.3f,3.4f,1.24f,1.2f,"navy");
            Box("Тара",-11.4f,1.51f,22.4f,.78f,.53f,.65f,"cargo",false);
            Spot("Упаковать заказ",-12,24,()=>game.UseWarehouse(WarehouseStation.Packing));
            Box("Рама конвейера",-11,.57f,36,1.1f,1.1f,9,"navy");
            for (int i=0;i<17;i++)
                Box("Ролик",-11,1.16f,32.3f+i*.455f,1.14f,.11f,.32f,"roof",false);
            Spot("Передать заказ на конвейер",-9,35,()=>game.UseWarehouse(WarehouseStation.Conveyor));

            for(int j=0;j<3;j++)
            {
                var fixture = new GameObject("Свет в рабочей зоне",typeof(Light));
                fixture.transform.SetParent(root);
                fixture.transform.position = new Vector3(-38+j*10,5.8f,24f);
                var light = fixture.GetComponent<Light>();
                light.type=LightType.Point;
                light.range=19f;
                light.intensity=1.7f;
                light.color=new Color(.90f,.95f,1);
                light.shadows=LightShadows.None;
            }
        }

        private void Services()
        {
            Room("МАГАЗИН У ДОМА",49,0,20,14,4.5f,46,"violet");
            Door("Вход в магазин",46,-7);
            Box("Продуктовая стойка",54.5f,1.12f,1.5f,2.2f,2.1f,.75f,"roof");
            for(int i=0;i<4;i++)
                Box("Продукты",53.76f+i*.51f,1.51f,.99f,.31f,.51f,.31f,"cargo",false);
            Box("Касса",50.6f,.61f,-1,1.55f,1.21f,.76f,"navy");
            Spot("Купить продукты",50.6f,-2.3f,()=>game.OpenService("shop"));
            Box("Пункт курьеров",40f,1.17f,-8.6f,2.9f,2.34f,.40f,"navy");
            Sign("КУРЬЕРЫ",40f,1.88f,-8.82f,.097f);
            Spot("Взять задание курьера",40f,-10f,()=>game.UseCourier(0));
            Box("Готовые доставки",43.5f,.57f,3.3f,1.4f,1.14f,.75f,"navy");
            Box("Посылка",43.5f,1.31f,3.3f,.54f,.32f,.73f,"cargo",false);
            Spot("Забрать посылку",43.5f,2.15f,()=>game.UseCourier(1));

            Box("Павильон LC БАНК",-73,1.62f,-22,6.4f,3.24f,3.8f,"navy");
            Sign("LC БАНК",-73,2.72f,-23.94f,.129f);
            Box("Банкомат",-73,1.06f,-24.03f,1.25f,2.13f,.62f,"dark");
            Box("Экран",-73,1.35f,-24.35f,.76f,.52f,.035f,"screen",false);
            Spot("Банкомат",-73,-25.7f,()=>game.OpenService("bank"));
            Box("Постамат",-80.5f,1.03f,-22.8f,1.5f,2.06f,.57f,"navy");
            Sign("ПОСТАМАТ",-80.5f,1.55f,-23.11f,.083f);
            Spot("Доставить посылку",-80.5f,-24.7f,()=>game.UseCourier(2));

            Room("LC АВТО",53,-19,21,13,4.2f,51,"navy");
            Door("Вход в автосалон",51,-25.5f);
            Box("Стойка продаж",55,.59f,-20,3.3f,1.16f,.85f,"roof");
            Spot("Автосалон",55,-21.48f,()=>game.OpenService("car"));
            Box("Парковка салона",42,.10f,-49,15,.118f,8,"asphalt");
            for (int i=0;i<4;i++)
                Box("Парковочная линия",36+i*3.8f,.168f,-49,.12f,.013f,6.2f,"roadmark",false);

            Box("Площадка АЗС",76,.13f,-50,22,.15f,12.6f,"asphalt");
            Box("Навес АЗС",76,4.14f,-51,14,.25f,9,"navy");
            Box("Колонка АЗС",77,1.02f,-52.5f,1.3f,2.04f,.8f,"mint");
            for (int i=-1;i<=1;i+=2)
                Box("Опора навеса",76+i*6,2.04f,-51,.26f,4.08f,.26f,"roof");
            Sign("АЗС",76,3.85f,-55.58f,.135f);
            Spot("Заправить автомобиль",77,-53.8f,()=>game.OpenService("fuel"));

            Box("Киоск",71,1.62f,1,6.4f,3.24f,3.8f,"rust");
            Sign("КИОСК",71,2.66f,-.94f,.126f);
            Spot("Управление киоском",71,-2.2f,()=>game.OpenService("business"));
            Box("Окно выдачи",73.7f,1.55f,-.96f,.84f,.72f,.12f,"screen",false);
            Spot("Выдать заказ покупателю",75.4f,-1.9f,()=>
            {
                if (game.Online) game.Notify("Киоск доступен в одиночной игре.");
                else game.Show(game.Session.Businesses.DispatchOrder());
            });
        }

        private void Apartments()
        {
            Room("КВАРТИРА 01",56,32,20,13,3.6f,55,"wall");
            Door("Дверь квартиры",55,25.5f,()=>game.Player.transform.position.z>26f ||
                (!game.Online && game.Session.Property.HasActiveRental));
            Box("Скамья у входа",58,.47f,22.9f,2.8f,.34f,.79f,"trunk");
            Spot("Договор аренды",58,21.6f,()=>game.OpenService("property"));
            Box("Кровать · каркас",50,.19f,35.6f,2.0f,.38f,3,"trunk");
            Box("Кровать · матрас",50,.50f,35.6f,1.91f,.24f,2.86f,"light");
            Box("Кровать · подушка",50,.66f,36.45f,.73f,.13f,.56f,"violet",false);
            Box("Диван",56,.44f,36.9f,3,.88f,.99f,"violet");
            Box("Диван · спинка",56,1.02f,37.35f,3,.59f,.25f,"violet");
            Box("Стол",56,.65f,34.4f,1.49f,.13f,1.05f,"trunk");
            Box("Холодильник",63.4f,1,35.7f,1.01f,2,1,"light");
            Box("Кухня",63.4f,.51f,33.5f,1.05f,1.02f,2.6f,"roof");
            Box("Шкаф",63.9f,1.10f,28.85f,1,2.2f,1.2f,"trunk");
            Spot("Инвентарь",62.2f,28.85f,()=>game.Open("inventory"));
        }

    }
}
