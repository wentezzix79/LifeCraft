using System;
using System.Globalization;
using System.Linq;
using LifeCraft.World;
using LifeCraftRP.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LifeCraftRP
{
    public sealed class UIManager
    {
        private static readonly Color Panel = new Color(.052f,.079f,.099f,.97f);
        private static readonly Color Line = new Color(.118f,.165f,.19f,.97f);
        private static readonly Color Paper = new Color(.95f,.965f,.95f,1);
        private static readonly Color Dim = new Color(.68f,.76f,.78f,1);
        private static readonly Color Mint = new Color(.43f,.94f,.76f,1);
        private static readonly Color Warm = new Color(1f,.69f,.39f,1);
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        private readonly GameManager game;
        private readonly Font font;
        private readonly RectTransform screen;
        private readonly RectTransform overlay;
        private readonly RectTransform modal;
        private readonly RectTransform body;
        private readonly Text clock,district,balance,details,task,interaction,toast,title,subtitle;
        private readonly Image health, armor, stamina;
        private readonly RawImage smallMap;
        private readonly RawImage bigMap;
        private readonly RectTransform bigMapDot;
        private readonly Texture2D mapTexture;
        private readonly Texture2D starterMap;
        private const float StarterMapHalfExtent = 1250f;
        private readonly RectTransform mapContainer;
        private RectTransform rows;
        private Text chat;
        private Text status;
        private Text pauseHint;
        private float toastUntil;
        private string host="http://127.0.0.1:8080";
        private string user="";
        private string password="";
        private bool mapZoom;
        private string phoneRoute;
        private static readonly string[] Presets = { "Very Low", "Low", "Medium", "High", "Ultra" };

        public UIManager(GameManager game)
        {
            this.game = game;
            font = Resources.Load<Font>("Art/DejaVuSans") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mapTexture = OmskWorld.LoadMapTexture();
            starterMap = Resources.Load<Texture2D>("Omsk/starter-map");
            if (EventSystem.current == null)
                new GameObject("События интерфейса",typeof(EventSystem),typeof(StandaloneInputModule));
            var canvas = new GameObject("LIFE CRAFT · интерфейс",typeof(Canvas),typeof(CanvasScaler),
                                        typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            canvas.GetComponent<Canvas>().sortingOrder=4;
            var scaler=canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1440,810);
            scaler.matchWidthOrHeight=.44f;
            screen=canvas.GetComponent<RectTransform>();

            var brand=ImagePanel("HUD · персонаж",screen,Panel);
            Place(brand,0,1,24,-24,312,151);
            var line=ImagePanel("Линия",brand,Mint);
            Place(line,0,1,0,0,3,151);
            Text mark=Label("LIFE CRAFT",brand,24,Paper,FontStyle.Bold);
            Place(mark.rectTransform,0,1,17,-10,186,37);
            clock=Label("",brand,18,Mint,FontStyle.Normal,TextAnchor.MiddleRight);
            Place(clock.rectTransform,1,1,-14,-12,72,30);
            district=Label("",brand,16,Dim);
            Place(district.rectTransform,0,1,17,-47,276,28);
            balance=Label("",brand,25,Paper,FontStyle.Bold);
            Place(balance.rectTransform,0,1,17,-75,208,39);
            details=Label("",brand,14,Dim,FontStyle.Normal,TextAnchor.MiddleRight);
            Place(details.rectTransform,1,1,-14,-85,112,25);
            health=Gauge(brand,17,-124,new Color(.41f,.87f,.59f));
            armor=Gauge(brand,111,-124,new Color(.51f,.70f,.97f));
            stamina=Gauge(brand,205,-124,Warm);

            var quest=ImagePanel("HUD · Текущая задача",screen,Panel);
            Place(quest,1,1,-24,-82,445,91);
            Text questTitle=Label("ТЕКУЩАЯ ЗАДАЧА",quest,13,Mint,FontStyle.Bold);
            Place(questTitle.rectTransform,0,1,15,-6,413,26);
            task=Label("",quest,17,Paper,FontStyle.Normal,TextAnchor.UpperLeft);
            Place(task.rectTransform,0,1,15,-38,413,49);

            var shortcuts=Create("Меню быстрого доступа",screen);
            Place(shortcuts,1,1,-24,-24,445,45);
            for (int i=0;i<3;i++)
            {
                string mode=new [] { "map","phone","menu" }[i];
                var button=Button("Кнопка "+mode,shortcuts,new [] { "Карта","Телефон","Меню" }[i],
                                  ()=>game.Toggle(mode),false);
                Place(button,0,1,i*151,0,143,45);
            }

            var mini=ImagePanel("HUD · миникарта",screen,Panel);
            Place(mini,0,Application.isMobilePlatform?1:0,24,Application.isMobilePlatform?-188:28,166,166);
            smallMap=CreateRaw("Кварталы",mini,mapTexture);
            Stretch(smallMap.rectTransform,5,5,5,5);
            var middle=ImagePanel("Метка игрока",mini,Mint);
            Center(middle,8,8);
            Text north=Label("С",mini,18,Paper,FontStyle.Bold,TextAnchor.MiddleCenter);
            Place(north.rectTransform,1,1,-6,-4,23,27);
            mini.GetComponent<Image>().raycastTarget=false;

            interaction=Label("",screen,19,Paper,FontStyle.Bold,TextAnchor.MiddleCenter);
            Place(interaction.rectTransform,.5f,0,0,29,650,54);
            interaction.rectTransform.pivot=new Vector2(.5f,0);
            interaction.raycastTarget=false;
            toast=Label("",screen,17,Paper,FontStyle.Bold,TextAnchor.MiddleCenter);
            Place(toast.rectTransform,.5f,0,0,94,840,63);
            toast.rectTransform.pivot=new Vector2(.5f,0);
            toast.raycastTarget=false;

            overlay=ImagePanel("Слой меню",screen,new Color(.015f,.027f,.034f,.73f));
            overlay.GetComponent<Image>().raycastTarget=true;
            Stretch(overlay,0,0,0,0);
            modal=ImagePanel("Окно",overlay,Panel);
            Center(modal,748,646);
            var accent=ImagePanel("Акцент",modal,Mint);
            Place(accent,0,1,0,0,5,101);
            title=Label("",modal,33,Paper,FontStyle.Bold);
            Place(title.rectTransform,0,1,25,-14,610,50);
            subtitle=Label("",modal,16,Dim);
            Place(subtitle.rectTransform,0,1,27,-64,668,32);
            var close=Button("Закрыть",modal,"×",Close,false);
            Place(close,1,1,-19,-20,45,45);
            body=Create("Содержимое",modal);
            Stretch(body,25,26,25,109);
            mapContainer=Create("Карта",body);
            Stretch(mapContainer,0,0,0,0);
            bigMap=CreateRaw("Геоданные Омска",mapContainer,mapTexture);
            Center(bigMap.rectTransform,628,446);
            bigMapDot=ImagePanel("Вы находитесь здесь",bigMap.rectTransform,Mint);
            Center(bigMapDot,12,12);
            var zoomButton=Button("Масштаб",mapContainer,"Весь город / район",()=>
            {
                mapZoom=!mapZoom;
                UpdateBigMap();
            },false);
            Place(zoomButton,0,0,0,0,236,37);
            Text attribution=Label(OmskWorld.Attribution,mapContainer,13,Dim,FontStyle.Normal,TextAnchor.MiddleRight);
            Place(attribution.rectTransform,1,0,-8,0,320,35);
            overlay.gameObject.SetActive(false);
        }

        public string Mode { get; private set; }
        public bool IsOpen => Mode != null;
        public bool IsTyping
        {
            get
            {
                EventSystem events=EventSystem.current;
                return events != null && events.currentSelectedGameObject != null &&
                       events.currentSelectedGameObject.TryGetComponent(out InputField currentField) && currentField.isFocused;
            }
        }

        public void Open(string mode)
        {
            if (game.Online && (mode=="inventory" || mode=="shop" || mode=="bank"))
            {
                Toast("Откройте эту услугу в одиночном режиме.");
                return;
            }
            Mode=mode;
            game.Player.MenusOpen=true;
            game.Player.Animation.Phone(mode=="phone");
            overlay.gameObject.SetActive(true);
            RefreshModal();
        }

        public void Close()
        {
            Mode=null;
            overlay.gameObject.SetActive(false);
            game.Player.MenusOpen=false;
            game.Player.Animation.Phone(false);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        public void RefreshHud(InteractionSystem.Site near)
        {
            PlayerState player=game.Session.State;
            clock.text=$"{game.Hour:00}:{game.Minute:00}";
            district.text=OmskWorld.DistrictName(game.Player.transform.position)+"  /  "+game.Weather;
            balance.text=Rubles(game.Online?game.Network.Cash:player.Cash);
            details.text=game.Online ? "В СЕТИ" : $"УР. {player.Level}  •  {player.Experience%100} XP";
            health.fillAmount=game.Player.Health01;
            armor.fillAmount=game.Player.Armor01;
            stamina.fillAmount=game.Player.Stamina01;
            task.text=game.CurrentTask;
            if (game.Vehicles.IsDriving)
                interaction.text=$"{game.Vehicles.Speed:0} км/ч    •    {game.Vehicles.Fuel:0.0} л";
            else if(near!=null)
                interaction.text=Application.isMobilePlatform ? near.Label : "[ E ]   "+near.Label;
            else
                interaction.text=game.Vehicles.IsNear && !Application.isMobilePlatform ?
                    "[ F ]   Автомобиль" : string.Empty;
            toast.enabled=Time.unscaledTime<toastUntil;
            UpdateMinimap(game.Player.transform.position);
            if(Mode=="map")
                UpdateBigMap();
            if(Mode=="network" && status!=null)
                status.text=game.Network.Status;
            if (Mode=="pause" && pauseHint!=null)
                pauseHint.text="Графика · "+Presets[game.Quality]+"   /   Погода · "+game.Weather;
        }

        public void Toast(string message)
        {
            if(string.IsNullOrWhiteSpace(message))
                return;
            toast.text=message;
            toast.enabled=true;
            toastUntil=Time.unscaledTime+4f;
        }

        public void RefreshChat()
        {
            if(Mode!="network" || chat==null)
                return;
            chat.text=string.Join("\n",game.Network.RecentChat.Reverse().Take(5).Reverse()
                .Select(m=>m.name+": "+m.message));
            if(chat.text.Length==0)
                chat.text="Сообщения участников появятся здесь.";
        }

        public void RefreshModal()
        {
            if(!IsOpen)
                return;
            if(rows!=null)
            {
                rows.parent.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(rows.parent.gameObject);
                rows=null;
            }
            chat=null; status=null; pauseHint=null;
            mapContainer.gameObject.SetActive(Mode=="map");
            if(Mode=="map")
            {
                Header("Карта Омска","Точка отмечает ваше текущее положение.");
                UpdateBigMap();
                return;
            }
            rows=ScrollRows(body);
            PlayerState state=game.Session.State;
            switch(Mode)
            {
                case "menu":
                    Header("Меню игрока","Омск · LIFE CRAFT RP 2.0");
                    Row("Телефон",()=>Open("phone"));
                    Row("Инвентарь",()=>Open("inventory"));
                    Row("Карта Омска",()=>Open("map"));
                    Row("Подключиться к серверу",()=>Open("network"));
                    Row("Настройки",()=>Open("pause"));
                    break;
                case "phone":
                    Header("Смартфон",game.Online?"Подключено · чат и работа":"Личные приложения");
                    Row("Работа · открыть маршрут",()=>Open("job"));
                    Row("Карта · ваше положение",()=>Open("map"));
                    Row("Банк · состояние счёта",()=>Open("bank-status"));
                    Row("Недвижимость · договор",()=>Open("property-status"));
                    Row("Автомобиль · состояние",()=>Open("vehicle-status"));
                    Row("Сообщения · сетевой чат",()=>Open("network"));
                    break;
                case "job":
                    Header("Работа","LC Логистика · склад и курьерская доставка");
                    Info(game.CurrentTask,74);
                    Row("Склад · показать район на карте",()=>
                    {
                        mapZoom=true; phoneRoute="warehouse"; Open("map");
                    });
                    if(!game.Online)
                        Info("Чтобы начать доставку, найдите терминал «Курьеры» у магазина.",72);
                    if(!game.Online && game.Session.Jobs.Stage!=WarehouseStage.NotHired)
                        Row("Закончить смену",()=>
                        {
                            GameResult result=game.Session.Jobs.EndShift();
                            game.Player.ApplyWorkUniform(false);
                            game.Player.Animation.Carry(false);
                            game.Show(result);
                            RefreshModal();
                        });
                    break;
                case "inventory":
                    Header("Инвентарь",$"Занято {state.Backpack.Count} из {InventorySystem.Capacity} ячеек");
                    if(state.Backpack.Count==0)
                        Info("Рюкзак пуст. Продукты можно купить в магазине у дома.",90);
                    foreach(ItemStack stack in state.Backpack)
                    {
                        InventoryItem item=InventorySystem.Find(stack.Id);
                        if(item!=null)
                        {
                            if(item.Energy>0)
                                Row(item.Name+"  × "+stack.Count+"  ·  использовать",()=>
                                {
                                    GameResult result=game.Session.Inventory.Use(item.Id);
                                    if(result.Success)
                                        game.Player.Heal(item.Energy*.006f);
                                    Apply(result);
                                });
                            else
                                Info(item.Name+"  × "+stack.Count,50);
                        }
                    }
                    break;
                case "shop":
                    Header("Магазин у дома","Списание с наличных и банковского счёта");
                    foreach(InventoryItem item in InventorySystem.Catalog)
                        Row(item.Name+"  /  "+Rubles(item.Price),()=>Apply(game.Session.Inventory.Buy(item.Id)));
                    break;
                case "bank":
                    Header("Банкомат LC",Rubles(state.Cash)+" наличными  /  "+Rubles(state.Bank)+" на счёте");
                    Row("Внести 1 000 ₽",()=>Apply(game.Session.Economy.Deposit(1000)));
                    Row("Внести 5 000 ₽",()=>Apply(game.Session.Economy.Deposit(5000)));
                    Row("Снять 1 000 ₽",()=>Apply(game.Session.Economy.Withdraw(1000)));
                    break;
                case "bank-status":
                    Header("Мой банк",game.Online?"Серверный счёт":"Наличные и банковский счёт");
                    Info(game.Online ? Rubles(game.Network.Cash)+" наличными" :
                         Rubles(state.Cash)+" наличными  /  "+Rubles(state.Bank)+" на счёте",70);
                    Info("Для внесения и снятия наличных подойдите к банкомату LC.",70);
                    break;
                case "car":
                    Header("LC Авто","Волна 1.5 · городской автомобиль");
                    Info("Парковка салона расположена через дорогу от входа.",65);
                    if(!state.CarOwned)
                        Row("Купить автомобиль  /  "+Rubles(EconomyManager.StarterCarPrice),
                            ()=>Apply(game.Session.Economy.BuyStarterCar()));
                    else
                        Info("Автомобиль принадлежит вам. Подойдите к нему и нажмите F.",65);
                    break;
                case "vehicle-status":
                    Header("Транспорт","LC Авто");
                    Info(!game.Online && state.CarOwned?$"Волна 1.5 · бак {game.Vehicles.Fuel:0.0} л":"В гараже пока пусто.",65);
                    break;
                case "fuel":
                    Header("Топливо","АЗС · "+Rubles(EconomyManager.FuelPricePerLitre)+" за литр");
                    if(!state.CarOwned || Vector3.Distance(game.Vehicles.Position,new Vector3(77f,0f,-53.8f))>16f)
                        Info("Подгоните свой автомобиль к колонке и выйдите.",80);
                    else
                        Row("Залить до 10 л",()=>Apply(game.Vehicles.Refuel(10)));
                    break;
                case "property":
                    Header("Аренда квартиры","Квартира 01 · меблированная");
                    if(game.Session.Property.HasActiveRental)
                        Info("Договор действует. Дверь рядом со скамьёй открывается клавишей E.",75);
                    else
                        Row("Арендовать на 7 дней  /  "+Rubles(PropertyManager.StarterApartmentRent),
                            ()=>Apply(game.Session.Property.RentStarterApartment()));
                    break;
                case "property-status":
                    Header("Недвижимость","Договоры игрока");
                    Info(!game.Online && state.HasActiveRental ?
                         "Квартира 01 · аренда до "+new DateTime(state.ApartmentUntilUtcTicks,DateTimeKind.Utc)
                             .ToLocalTime().ToString("dd.MM HH:mm",Ru)
                         : "Активных договоров аренды нет.",65);
                    break;
                case "business":
                    Header("Киоск","Закупка и розничная торговля");
                    if(!state.KioskOwned)
                        Row("Купить киоск  /  "+Rubles(BusinessManager.KioskPrice),
                            ()=>Apply(game.Session.Businesses.BuyKiosk()));
                    else
                    {
                        Info($"Товаров {state.KioskStock}  •  Продаж {state.KioskSales}  •  Цена {Rubles(state.KioskUnitPrice)}",58);
                        Row("Заказать 10 единиц",()=>Apply(game.Session.Businesses.Restock(10)));
                        Row("Установить цену 90 ₽",()=>Apply(game.Session.Businesses.SetUnitPrice(90)));
                        Row("Установить цену 150 ₽",()=>Apply(game.Session.Businesses.SetUnitPrice(150)));
                        Info("Для продажи отнесите заказ к окну выдачи киоска.",70);
                    }
                    break;
                case "network":
                    Header("Сетевой режим","Локальный сервер LC RP");
                    NetworkScreen();
                    break;
                case "pause":
                    Header("Пауза","Настройки игры");
                    pauseHint=Info("",49);
                    for(int i=0;i<Presets.Length;i++)
                    {
                        int next=i;
                        Row(Presets[i]+(i==game.Quality?"  ·  текущий":""),()=>
                        {
                            game.ApplyQuality(next); RefreshModal();
                        });
                    }
                    Row("Сменить погоду",game.ChangeWeather);
                    Row("Сохранить и продолжить",()=>{game.SaveProgress(); Close();});
                    break;
                default:
                    Header("LIFE CRAFT RP 2.0","Омск");
                    break;
            }
        }

        private void Apply(GameResult result)
        {
            game.Show(result);
            RefreshModal();
        }

        private void NetworkScreen()
        {
            status=Info(game.Network.Status,52);
            if(game.Online)
            {
                chat=Info("",155);
                RefreshChat();
                InputField compose=TextField("Сообщение",string.Empty,null);
                compose.characterLimit=180;
                void Send()
                {
                    game.Network.SendChat(compose.text);
                    compose.text=string.Empty;
                }
                Row("Отправить",Send);
                Row("Отключиться",game.Network.LogOut);
                return;
            }
            TextField("Адрес сервера",host,value=>host=value);
            TextField("Логин: A–Z, 0–9, _",user,value=>user=value);
            TextField("Пароль",password,value=>password=value,true);
            Row("Войти",()=>Connect(false));
            Row("Создать аккаунт",()=>Connect(true));
            Info("Сетевой профиль и рубли хранятся отдельно от одиночной игры.",62);
        }

        private void Connect(bool register)
        {
            game.Connect(host,user,password,register);
            password=string.Empty;
        }

        private void Header(string heading,string caption)
        {
            title.text=heading;
            subtitle.text=caption;
        }

        private void UpdateMinimap(Vector3 world)
        {
            const float cityWidth = .038f, cityHeight = .030f;
            float width=cityWidth*OmskWorld.WorldBounds.width;
            float height=cityHeight*OmskWorld.WorldBounds.height;
            if (starterMap != null &&
                Mathf.Abs(world.x)+width*.5f<=StarterMapHalfExtent &&
                Mathf.Abs(world.z)+height*.5f<=StarterMapHalfExtent)
            {
                const float side=StarterMapHalfExtent*2f;
                var center=new Vector2((world.x+StarterMapHalfExtent)/side,
                    (world.z+StarterMapHalfExtent)/side);
                smallMap.texture=starterMap;
                smallMap.uvRect=ZoomRect(center,width/side,height/side);
            }
            else
            {
                smallMap.texture=mapTexture;
                smallMap.uvRect=ZoomRect(OmskWorld.MapPosition(world),cityWidth,cityHeight);
            }
        }

        private void UpdateBigMap()
        {
            Vector2 point=OmskWorld.MapPosition(game.Player.transform.position);
            Vector2 focus=phoneRoute=="warehouse" && mapZoom ?
                OmskWorld.MapPosition(new Vector3(-27f,0f,2f)) : point;
            Rect uv=mapZoom?ZoomRect(focus,.12f,.11f):new Rect(0f,0f,1f,1f);
            bigMap.uvRect=uv;
            Vector2 normalized=new Vector2((point.x-uv.x)/uv.width,(point.y-uv.y)/uv.height);
            bigMapDot.anchorMin=bigMapDot.anchorMax=normalized;
            bigMapDot.anchoredPosition=Vector2.zero;
            bigMapDot.gameObject.SetActive(new Rect(0f,0f,1f,1f).Contains(normalized));
            if(!mapZoom)
                phoneRoute=null;
        }

        private static Rect ZoomRect(Vector2 p,float w,float h) =>
            new Rect(Mathf.Clamp(p.x-w/2,0,1-w),Mathf.Clamp(p.y-h/2,0,1-h),w,h);

        private Text Info(string caption,float height)
        {
            Text text=Label(caption,rows,18,Dim,FontStyle.Normal,TextAnchor.MiddleLeft);
            text.gameObject.AddComponent<LayoutElement>().preferredHeight=height;
            return text;
        }

        private void Row(string caption,Action action)
        {
            var rect=Button(caption,rows,caption,action,false);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight=52;
        }

        private InputField TextField(string placeholder,string value,Action<string> change,bool secret=false)
        {
            var rect=ImagePanel(placeholder,rows,Line);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight=51;
            var field=rect.gameObject.AddComponent<InputField>();
            Text text=Label("",rect,18,Paper);
            Text hint=Label(placeholder,rect,17,Dim);
            Stretch(text.rectTransform,13,4,13,4); Stretch(hint.rectTransform,13,4,13,4);
            field.textComponent=text;
            field.placeholder=hint;
            field.targetGraphic=rect.GetComponent<Image>();
            field.contentType=secret?InputField.ContentType.Password:InputField.ContentType.Standard;
            field.text=value;
            field.characterLimit=180;
            if(change!=null)
                field.onValueChanged.AddListener(changed=>change(changed));
            return field;
        }

        private RectTransform ScrollRows(Transform parent)
        {
            var view=Create("Прокрутка",parent);
            Stretch(view,0,0,0,0);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color=new Color(0,0,0,.001f);
            var frame=view.gameObject.AddComponent<ScrollRect>();
            frame.horizontal=false; frame.viewport=view; frame.scrollSensitivity=23f;
            var content=Create("Действия",view);
            content.anchorMin=new Vector2(0f,1f);
            content.anchorMax=new Vector2(1f,1f);
            content.pivot=new Vector2(.5f,1f);
            content.offsetMin=new Vector2(0f,0f);
            content.offsetMax=Vector2.zero;
            VerticalLayoutGroup group=content.gameObject.AddComponent<VerticalLayoutGroup>();
            group.childForceExpandHeight=false;
            group.childForceExpandWidth=true;
            group.spacing=8;
            ContentSizeFitter fitter=content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            frame.content=content;
            return content;
        }

        private Image Gauge(RectTransform parent,float x,float y,Color color)
        {
            RectTransform back=ImagePanel("Показатель",parent,Line);
            Place(back,0,1,x,y,82,7);
            RectTransform fill=ImagePanel("Значение",back,color);
            Stretch(fill,0,0,0,0);
            Image image=fill.GetComponent<Image>();
            image.type=Image.Type.Filled;
            image.fillMethod=Image.FillMethod.Horizontal;
            // A sprite is required for an Image with Fill mode.
            Texture2D texture=Texture2D.whiteTexture;
            image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),
                                       new Vector2(.5f,.5f));
            return image;
        }

        private RectTransform Button(string name,Transform parent,string caption,Action action,bool primary)
        {
            RectTransform rect=ImagePanel(name,parent,primary?Mint:Line);
            Image image=rect.GetComponent<Image>();
            image.raycastTarget=true;
            var button=rect.gameObject.AddComponent<Button>();
            button.targetGraphic=image;
            ColorBlock colors=button.colors;
            colors.normalColor=Color.white;
            colors.highlightedColor=new Color(.79f,.96f,.93f);
            colors.pressedColor=new Color(.70f,.81f,.79f);
            button.colors=colors;
            button.onClick.AddListener(()=>action());
            Text label=Label(caption,rect,18,primary?Panel:Paper,FontStyle.Bold,TextAnchor.MiddleCenter);
            Stretch(label.rectTransform,11,2,11,2);
            return rect;
        }

        private RawImage CreateRaw(string name,Transform parent, Texture texture)
        {
            var obj=Create(name,parent).gameObject.AddComponent<RawImage>();
            obj.texture=texture; obj.raycastTarget=false;
            obj.color=texture!=null ? Color.white : Line;
            return obj;
        }

        private Text Label(string caption,Transform parent,int size,Color color,
                           FontStyle style=FontStyle.Normal,TextAnchor alignment=TextAnchor.MiddleLeft)
        {
            var text=Create(caption,parent).gameObject.AddComponent<Text>();
            text.font=font; text.text=caption; text.fontSize=size; text.fontStyle=style;
            text.color=color; text.alignment=alignment; text.raycastTarget=false;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;
            text.verticalOverflow=VerticalWrapMode.Truncate;
            return text;
        }

        private static RectTransform ImagePanel(string name,Transform parent,Color color)
        {
            RectTransform rect=Create(name,parent);
            Image image=rect.gameObject.AddComponent<Image>();
            image.color=color;
            image.raycastTarget=false;
            return rect;
        }

        private static RectTransform Create(string name,Transform parent)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent,false);
            return rect;
        }

        private static void Center(RectTransform rect,float width,float height)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            rect.anchoredPosition=Vector2.zero;
            rect.sizeDelta=new Vector2(width,height);
        }

        private static void Place(RectTransform rect,float anchorX,float anchorY,float x,float y,
                                  float width,float height)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(anchorX,anchorY);
            rect.sizeDelta=new Vector2(width,height);
            rect.anchoredPosition=new Vector2(x,y);
        }

        private static void Stretch(RectTransform rect,float left,float bottom,float right,float top)
        {
            rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one;
            rect.offsetMin=new Vector2(left,bottom);
            rect.offsetMax=new Vector2(-right,-top);
        }

        private static string Rubles(int amount)=>amount.ToString("N0",Ru)+" ₽";
    }
}
