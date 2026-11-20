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
        private static readonly Color Panel = new Color(.098f,.072f,.154f,.98f);
        private static readonly Color Line = new Color(.21f,.165f,.315f,.98f);
        private static readonly Color Paper = new Color(.95f,.965f,.95f,1);
        private static readonly Color Dim = new Color(.77f,.72f,.85f,1);
        private static readonly Color Mint = new Color(.75f,.565f,1f,1f);
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        private readonly GameManager game;
        private readonly Font font;
        private readonly RectTransform screen;
        private readonly RectTransform overlay;
        private readonly RectTransform modal;
        private readonly RectTransform body;
        private readonly Text interaction,toast,title,subtitle;
        private readonly LifeCraftHud hud;
        private readonly LifeCraftMapPage map;
        private readonly LifeCraftRouteGraphic minimapRoute;
        private string mapFocus;
        private RectTransform rows;
        private Text chat;
        private Text status;
        private Text pauseHint;
        private float toastUntil;
        private string host="http://127.0.0.1:8080";
        private string user="";
        private string password="";
        private static readonly string[] Presets = { "Very Low", "Low", "Medium", "High", "Ultra" };

        public UIManager(GameManager game)
        {
            this.game = game;
            font = Resources.Load<Font>("Art/DejaVuSans") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

            hud=new LifeCraftHud(game,screen,font);
            var mapMask=hud.Minimap.GetComponent<RectMask2D>();
            if(!mapMask)
                hud.Minimap.gameObject.AddComponent<RectMask2D>();
            minimapRoute=Create("Маршрут на миникарте",hud.Minimap.rectTransform)
                .gameObject.AddComponent<LifeCraftRouteGraphic>();
            minimapRoute.raycastTarget=false;
            minimapRoute.color=new Color(1f,.46f,.27f,1f);
            Stretch(minimapRoute.rectTransform,0,0,0,0);

            interaction=Label("",screen,19,Paper,FontStyle.Bold,TextAnchor.MiddleCenter);
            Place(interaction.rectTransform,.5f,0,0,29,650,54);
            interaction.rectTransform.pivot=new Vector2(.5f,0);
            interaction.raycastTarget=false;
            toast=Label("",screen,17,Paper,FontStyle.Bold,TextAnchor.MiddleCenter);
            Place(toast.rectTransform,.5f,0,0,151,840,63);
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
            map=new LifeCraftMapPage(game,screen,font,Close);
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
            overlay.gameObject.SetActive(mode!="map");
            RefreshModal();
        }

        public void Close()
        {
            Mode=null;
            map.Hide();
            overlay.gameObject.SetActive(false);
            game.Player.MenusOpen=false;
            game.Player.Animation.Phone(false);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        public void RefreshHud(InteractionSystem.Site near)
        {
            map.Tick();
            hud.SetRouteStatus(map.NavigationSummary);
            hud.Refresh(IsOpen,IsTyping);
            interaction.enabled=hud.Visible && !IsOpen;
            if (game.Vehicles.IsDriving)
                interaction.text=$"{game.Vehicles.Speed:0} км/ч    •    {game.Vehicles.Fuel:0.0} л";
            else if (game.BusPrompt is string busPrompt)
                interaction.text=Application.isMobilePlatform ? busPrompt : "[ E ]   "+busPrompt;
            else if(near!=null)
                interaction.text=Application.isMobilePlatform ? near.Label : "[ E ]   "+near.Label;
            else
                interaction.text=game.Vehicles.IsNear && !Application.isMobilePlatform ?
                    "[ F ]   Автомобиль" : string.Empty;
            toast.enabled=hud.Visible && Time.unscaledTime<toastUntil;
            if (hud.Visible && !IsOpen)
            {
                Rect view=MinimapView(game.Player.Position);
                LifeCraftMapViewport.DrawRaster(hud.Minimap,view);
                minimapRoute.SetRoute(map.Route,view);
            }
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
            if(Mode=="map")
                map.Notify(message);
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
            if (Mode=="map")
            {
                map.Show(mapFocus);
                mapFocus=null;
                return;
            }
            map.Hide();
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
                        mapFocus="warehouse"; Open("map");
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
                case "business-status":
                    Header("Бизнес","Киоск · LC Торговля");
                    Info(state.KioskOwned ?
                        $"Товаров {state.KioskStock}  •  Продаж {state.KioskSales}  •  Цена {Rubles(state.KioskUnitPrice)}" :
                        "Свободный киоск можно приобрести на месте.",72);
                    Row("Показать киоск на карте",()=>{ mapFocus="kiosk"; Open("map"); });
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

        private Rect MinimapView(Vector3 point)
        {
            const float width=1120f;
            Rect city=OmskWorld.WorldBounds;
            Rect panel=hud.Minimap.rectTransform.rect;
            float aspect=Mathf.Clamp(panel.width/Mathf.Max(1f,panel.height),.8f,3f);
            float height=width/aspect;
            return new Rect(Mathf.Clamp(point.x-width/2,city.xMin,city.xMax-width),
                Mathf.Clamp(point.z-height/2,city.yMin,city.yMax-height),width,height);
        }

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
