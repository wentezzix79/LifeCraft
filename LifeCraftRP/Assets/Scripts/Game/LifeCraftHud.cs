using System;
using System.Globalization;
using LifeCraft.World;
using LifeCraftRP.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LifeCraftRP
{
    internal sealed class LifeCraftHud
    {
        private static readonly Color Rim = new Color(.39f,.28f,.55f,.82f);
        private static readonly Color Panel = new Color(.077f,.052f,.114f,.94f);
        private static readonly Color ButtonColor = new Color(.142f,.100f,.208f,.97f);
        private static readonly Color Inset = new Color(.225f,.158f,.326f,.91f);
        private static readonly Color White = new Color(.977f,.956f,1f);
        private static readonly Color Dim = new Color(.78f,.716f,.861f);
        private static readonly Color Violet = new Color(.73f,.54f,1f);
        private static readonly Color Green = new Color(.49f,.91f,.67f);
        private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

        private readonly GameManager game;
        private readonly Font font;
        private readonly RectTransform root,top,brand,navigation,keyboardHint;
        private readonly RectTransform profile,questPanel,miniPanel,vitals;
        private readonly RectTransform headingArrow;
        private readonly Button[] actions = new Button[6];
        private readonly Text[] actionLabels = new Text[6];
        private readonly RectTransform[] actionIcons = new RectTransform[6];
        private readonly Text character,session,wallet,progress,coins,quest,location,clock,routeStatus;
        private readonly Image xp;
        private readonly Meter health,armor,stamina;
        private bool cursorOverride;
        private bool compact;
        private float nextRead;
        private float nextMapRead;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        private static Sprite rounded;
        private static Sprite solid;
        private static readonly Func<PlayerState,Profile> ReadProfile = p =>
            new Profile(p.Cash,p.Coins,p.Experience,p.Level);

        internal RawImage Minimap { get; }
        internal bool Visible => root.gameObject.activeSelf;
        private bool Mobile => Application.isMobilePlatform;

        internal LifeCraftHud(GameManager game,RectTransform parent,Font font)
        {
            this.game=game;
            this.font=font;
            root=Rect("LIFE CRAFT · HUD",parent);
            Stretch(root);
            top=Card("Навигация",root,.94f);
            brand=Rect("Логотип",top);
            var mark=Text("LIFE CRAFT",brand,22,White,FontStyle.BoldAndItalic);
            Place(mark.rectTransform,0f,.5f,0,7,145,27);
            var badge=Shape("Название версии",brand,Inset);
            Place(badge,0f,.5f,1,-12,58,17);
            var release=Text("RP 2.0",badge,10,Violet,FontStyle.Bold,TextAnchor.MiddleCenter);
            Stretch(release.rectTransform);

            navigation=Rect("Игровые разделы",top);
            string[] titles={"Карта","Инвентарь","Телефон","Работа","Бизнес","Меню"};
            string[] sections={"map","inventory","phone","job","business-status","menu"};
            for(int i=0;i<sections.Length;i++)
            {
                string section=sections[i];
                var element=Shape(titles[i],navigation,ButtonColor);
                actions[i]=Button(element,()=>game.Toggle(section));
                actionIcons[i]=Icon(element,(HudGlyph)i,Violet);
                actionLabels[i]=Text(titles[i],element,15,White,FontStyle.Bold,TextAnchor.MiddleCenter);
            }
            keyboardHint=Rect("Управление HUD",top);
            var hotkeys=Text("Alt · курсор\nF5 · скрыть HUD",keyboardHint,12,Dim,FontStyle.Normal,TextAnchor.MiddleRight);
            Stretch(hotkeys.rectTransform);

            profile=Card("Профиль персонажа",root);
            var identity=Shape("Эмблема персонажа",profile,Inset);
            Place(identity,0,1,13,-13,38,38);
            Place(Icon(identity,HudGlyph.Profile,Violet),.5f,.5f,0,0,21,21);
            character=Text("Персонаж",profile,17,White,FontStyle.Bold);
            TopBand(character.rectTransform,62,10,11,23);
            session=Text("",profile,12,Dim);
            TopBand(session.rectTransform,62,10,34,21);

            var moneyFrame=Shape("Наличные",profile,ButtonColor);
            moneyFrame.anchorMin=new Vector2(0,1);
            moneyFrame.anchorMax=new Vector2(.59f,1);
            moneyFrame.offsetMin=new Vector2(12,-100);
            moneyFrame.offsetMax=new Vector2(-4,-66);
            Place(Icon(moneyFrame,HudGlyph.Wallet,Violet),0,.5f,9,0,17,17);
            wallet=Text("",moneyFrame,15,White,FontStyle.Bold);
            wallet.resizeTextForBestFit=true;
            wallet.resizeTextMinSize=10;
            wallet.resizeTextMaxSize=15;
            Stretch(wallet.rectTransform,34,0,3,0);

            var coinFrame=Shape("LC Coins",profile,ButtonColor);
            coinFrame.anchorMin=new Vector2(.59f,1);
            coinFrame.anchorMax=new Vector2(1,1);
            coinFrame.offsetMin=new Vector2(4,-100);
            coinFrame.offsetMax=new Vector2(-12,-66);
            Place(Icon(coinFrame,HudGlyph.Coin,new Color(1f,.79f,.43f)),0,.5f,8,0,17,17);
            coins=Text("",coinFrame,13,White,FontStyle.Bold);
            coins.resizeTextForBestFit=true;
            coins.resizeTextMinSize=9;
            coins.resizeTextMaxSize=13;
            Stretch(coins.rectTransform,32,0,4,0);

            progress=Text("",profile,12,Dim,FontStyle.Bold);
            TopBand(progress.rectTransform,13,13,109,22);
            var xpBase=Shape("Полоса опыта",profile,Inset);
            TopBand(xpBase,13,13,134,6);
            xp=Fill(xpBase,Violet);

            questPanel=Card("Активная задача",root,.90f);
            var marker=Shape("Указатель задания",questPanel,Inset);
            Place(marker,0,1,13,-29,31,31);
            Place(Icon(marker,HudGlyph.Job,Violet),.5f,.5f,0,0,16,16);
            var taskTitle=Text("ТЕКУЩЕЕ ЗАДАНИЕ",questPanel,11,Violet,FontStyle.Bold);
            Place(taskTitle.rectTransform,0,1,55,-31,225,21);
            quest=Text("",questPanel,14,White,FontStyle.Normal,TextAnchor.UpperLeft);
            Stretch(quest.rectTransform,15,8,14,51);

            miniPanel=Card("Мини-карта Омска",root);
            var mask=Shape("Рамка мини-карты",miniPanel,White);
            mask.anchorMin=Vector2.zero;
            mask.anchorMax=Vector2.one;
            mask.offsetMin=new Vector2(8,63);
            mask.offsetMax=new Vector2(-8,-8);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            Minimap=Rect("Поверхность карты",mask).gameObject.AddComponent<RawImage>();
            Stretch(Minimap.rectTransform);
            Minimap.color=new Color(.62f,.58f,.72f,1f);
            Minimap.raycastTarget=false;
            var mapHit=Shape("Открыть карту",mask,new Color(.14f,.075f,.27f,.12f));
            Stretch(mapHit);
            Button(mapHit,()=>game.Toggle("map"),true);
            headingArrow=Icon(mask,HudGlyph.Arrow,White);
            Place(headingArrow,.5f,.5f,0,0,25,25);

            var north=Text("С",miniPanel,12,White,FontStyle.Bold,TextAnchor.MiddleCenter);
            Place(north.rectTransform,.5f,1,0,-19,18,17);
            var hint=Text(Mobile?"Карта":"M · Карта",miniPanel,11,White,FontStyle.Bold,TextAnchor.MiddleCenter);
            Place(hint.rectTransform,1,1,-12,-22,67,18);

            Place(Icon(miniPanel,HudGlyph.Pin,Violet),0,0,13,45,15,15);
            location=Text("",miniPanel,12,White,FontStyle.Bold);
            Place(location.rectTransform,0,0,34,45,182,18);
            clock=Text("",miniPanel,12,Dim);
            Place(clock.rectTransform,0,0,13,25,198,18);
            routeStatus=Text("",miniPanel,10,Violet,FontStyle.Bold);
            Place(routeStatus.rectTransform,0,0,13,10,198,17);

            vitals=Rect("Показатели персонажа",root);
            health=new Meter(this,vitals,0,HudGlyph.Health,"Здоровье",new Color(.99f,.39f,.49f));
            armor=new Meter(this,vitals,1,HudGlyph.Armor,"Броня",new Color(.56f,.73f,1f));
            stamina=new Meter(this,vitals,2,HudGlyph.Energy,"Выносливость",new Color(1f,.77f,.37f));
            Reflow();
        }

        internal void SetRouteStatus(string summary)
        {
            Set(routeStatus,summary??string.Empty);
        }

        internal void Refresh(bool menuOpen,bool typing)
        {
            if(!Mobile && !typing && Input.GetKeyDown(KeyCode.F5))
                root.gameObject.SetActive(!Visible);

            bool unlock=!Mobile && Visible && !menuOpen && Application.isFocused &&
                        Input.GetKey(KeyCode.LeftAlt);
            if(unlock && Cursor.lockState!=CursorLockMode.None)
            {
                Cursor.lockState=CursorLockMode.None;
                Cursor.visible=true;
            }
            else if(!unlock && cursorOverride && !menuOpen && Application.isFocused)
            {
                Cursor.lockState=CursorLockMode.Locked;
                Cursor.visible=false;
            }
            cursorOverride=unlock;
            if(!Visible)
                return;

            Reflow();
            var player=game.Player;
            float heading=player.IsDriving ? player.CurrentVehicle.transform.eulerAngles.y : player.transform.eulerAngles.y;
            headingArrow.localRotation=Quaternion.Euler(0f,0f,-heading);
            health.SetValue(player.Health01);
            armor.SetValue(player.Armor01);
            stamina.SetValue(player.Stamina01);

            float now=Time.unscaledTime;
            if(now>=nextRead)
            {
                nextRead=now+.15f;
                bool online=game.Online;
                Profile profileState=game.Session.Read(ReadProfile);
                Set(character,online && !string.IsNullOrWhiteSpace(game.Network.UserName)
                    ? game.Network.UserName : "Персонаж");
                Set(session,online ? "Сетевой режим" : $"Уровень {profileState.Level} · Одиночная игра");
                session.color=online?Green:Dim;
                int amount=online?game.Network.Cash:profileState.Cash;
                Set(wallet,amount.ToString("N0",Russian)+" ₽");
                coins.rectTransform.parent.gameObject.SetActive(!online);
                if(!online)
                    Set(coins,profileState.Coins.ToString("N0",Russian)+" LC");
                xp.rectTransform.parent.gameObject.SetActive(!online);
                int gained=Mathf.Max(0,profileState.Experience%100);
                Set(progress,online ? "Баланс синхронизирован" : $"{gained} / 100 XP");
                xp.fillAmount=gained*.01f;
                actions[1].interactable=!online;
                actions[4].interactable=!online;
                Set(clock,$"{game.Hour:00}:{game.Minute:00}   ·   {game.Weather}");
                string job=game.CurrentTask;
                bool hasTask=!string.IsNullOrWhiteSpace(job);
                questPanel.gameObject.SetActive(hasTask);
                if(hasTask)
                    Set(quest,job);
            }
            if(now>=nextMapRead)
            {
                nextMapRead=now+.45f;
                string district=OmskWorld.DistrictName(player.Position);
                Set(location,district=="Омск" ? district : "Омск · "+district);
            }
        }

        private void Reflow()
        {
            var size=new Vector2Int(Screen.width,Screen.height);
            Rect safe=Screen.safeArea;
            if(size.x<1 || size.y<1)
                return;
            if(safe.width<1 || safe.height<1)
                safe=new Rect(0,0,size.x,size.y);
            if(size==lastScreenSize && safe==lastSafeArea)
                return;
            lastScreenSize=size;
            lastSafeArea=safe;
            root.anchorMin=new Vector2(safe.xMin/size.x,safe.yMin/size.y);
            root.anchorMax=new Vector2(safe.xMax/size.x,safe.yMax/size.y);
            root.offsetMin=root.offsetMax=Vector2.zero;

            float aspect=safe.width/Mathf.Max(1f,safe.height);
            compact=Mobile || aspect<1.5f;
            const float gap=16;
            float left=compact?12:gap;
            top.anchorMin=new Vector2(0,1);
            top.anchorMax=new Vector2(1,1);
            top.pivot=new Vector2(.5f,1);
            top.offsetMin=new Vector2(left,-65);
            top.offsetMax=new Vector2(-left,-11);
            Place(brand,0,.5f,14,0,152,48);
            keyboardHint.gameObject.SetActive(!compact);
            Place(keyboardHint,1,.5f,-13,0,113,45);
            Stretch(navigation,compact ? 147 : 177,6,compact ? 7 : 140,6);

            int visible=compact?4:6;
            int column=0;
            for(int i=0;i<actions.Length;i++)
            {
                var action=(RectTransform)actions[i].transform;
                bool shown=!compact || (i!=3 && i!=4);
                action.gameObject.SetActive(shown);
                if(!shown)
                    continue;
                action.anchorMin=new Vector2(column/(float)visible,0);
                action.anchorMax=new Vector2((column+1)/(float)visible,1);
                action.offsetMin=new Vector2(3,0);
                action.offsetMax=new Vector2(-3,0);
                actionLabels[i].text=compact && i==1 ? "Вещи" :
                    i==1 ? "Инвентарь" : action.name;
                actionLabels[i].fontSize=compact?14:15;
                Stretch(actionLabels[i].rectTransform,compact?2:26,0,compact?2:4,0);
                actionIcons[i].gameObject.SetActive(!compact);
                Place(actionIcons[i],0,.5f,9,0,16,16);
                column++;
            }

            Place(profile,0,1,left,-77,compact?236:258,147);
            Place(questPanel,1,1,-left,-77,compact?265:318,103);
            Place(miniPanel,0,Mobile?1:0,left,Mobile?-236:17,compact?214:238,compact?169:205);
            Place(vitals,.5f,0,0,86,compact?367:529,53);
            health.Relayout(compact?117:171);
            armor.Relayout(compact?117:171);
            stamina.Relayout(compact?117:171);
        }

        private sealed class Meter
        {
            private readonly RectTransform box;
            private readonly Image fill;
            private readonly Text value,caption;
            private readonly int slot;
            private int last=-1;

            internal Meter(LifeCraftHud owner,RectTransform parent,int slot,HudGlyph glyph,string name,Color color)
            {
                this.slot=slot;
                box=Card(name,parent,.9f);
                Place(Icon(box,glyph,color),0,1,10,-17,16,16);
                caption=owner.Text(name,box,12,Dim,FontStyle.Bold);
                Stretch(caption.rectTransform,31,19,28,4);
                value=owner.Text("0",box,14,White,FontStyle.Bold,TextAnchor.MiddleRight);
                Place(value.rectTransform,1,1,-10,-17,34,19);
                var track=Shape("Шкала",box,Inset);
                track.anchorMin=new Vector2(0,0);
                track.anchorMax=new Vector2(1,0);
                track.offsetMin=new Vector2(10,9);
                track.offsetMax=new Vector2(-10,15);
                fill=Fill(track,color);
            }

            internal void Relayout(float width)
            {
                Place(box,0,0,slot*(width+8),0,width,53);
                caption.fontSize=width<150?10:12;
            }

            internal void SetValue(float fraction)
            {
                float normalized=Mathf.Clamp01(fraction);
                fill.fillAmount=normalized;
                int number=Mathf.RoundToInt(normalized*100);
                if(number==last)
                    return;
                last=number;
                value.text=number.ToString();
            }
        }

        private readonly struct Profile
        {
            internal readonly int Cash,Coins,Experience,Level;
            internal Profile(int cash,int coins,int experience,int level)
            {
                Cash=cash; Coins=coins; Experience=experience; Level=level;
            }
        }

        private static void Set(Text label,string value)
        {
            if(label.text!=value)
                label.text=value;
        }

        private Text Text(string value,RectTransform parent,int size,Color color,
            FontStyle style=FontStyle.Normal,TextAnchor alignment=TextAnchor.MiddleLeft)
        {
            var text=Rect(value,parent).gameObject.AddComponent<Text>();
            text.font=font;
            text.text=value;
            text.fontSize=size;
            text.fontStyle=style;
            text.color=color;
            text.alignment=alignment;
            text.raycastTarget=false;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;
            text.verticalOverflow=VerticalWrapMode.Truncate;
            return text;
        }

        private static RectTransform Card(string name,Transform parent,float opacity=1f)
        {
            var rim=Shape(name,parent,Rim);
            var fill=Shape("Тёмный фон",rim,new Color(Panel.r,Panel.g,Panel.b,Panel.a*opacity));
            Stretch(fill,1,1,1,1);
            return rim;
        }

        private static RectTransform Shape(string name,Transform parent,Color color)
        {
            var rt=Rect(name,parent);
            var image=rt.gameObject.AddComponent<Image>();
            image.sprite=RoundedSprite();
            image.type=Image.Type.Sliced;
            image.color=color;
            image.raycastTarget=false;
            return rt;
        }

        private static Button Button(RectTransform parent,Action action,bool subtle=false)
        {
            var image=parent.GetComponent<Image>();
            image.raycastTarget=true;
            var button=parent.gameObject.AddComponent<Button>();
            button.targetGraphic=image;
            var colors=button.colors;
            colors.highlightedColor=subtle?new Color(.89f,.77f,1f):new Color(1f,.86f,1f);
            colors.pressedColor=new Color(.77f,.62f,1f);
            colors.selectedColor=Color.white;
            colors.disabledColor=new Color(.38f,.35f,.44f,.52f);
            colors.fadeDuration=.10f;
            button.colors=colors;
            button.onClick.AddListener(()=>action());
            return button;
        }

        private static Image Fill(RectTransform parent,Color color)
        {
            var rt=Rect("Значение",parent);
            Stretch(rt);
            var fill=rt.gameObject.AddComponent<Image>();
            if(!solid)
                solid=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,Texture2D.whiteTexture.width,
                    Texture2D.whiteTexture.height),Vector2.one*.5f);
            fill.sprite=solid;
            fill.type=Image.Type.Filled;
            fill.fillMethod=Image.FillMethod.Horizontal;
            fill.color=color;
            fill.raycastTarget=false;
            return fill;
        }

        private static Sprite RoundedSprite()
        {
            if(rounded)
                return rounded;
            const int size=48;
            const float radius=11f;
            var image=new Texture2D(size,size,TextureFormat.RGBA32,false) { name="LC HUD rounded",wrapMode=TextureWrapMode.Clamp };
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float dx=Mathf.Max(Mathf.Abs(x+.5f-size*.5f)-(size*.5f-radius),0f);
                float dy=Mathf.Max(Mathf.Abs(y+.5f-size*.5f)-(size*.5f-radius),0f);
                float alpha=Mathf.Clamp01(radius+.45f-Mathf.Sqrt(dx*dx+dy*dy));
                pixels[y*size+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(alpha*255f));
            }
            image.SetPixels32(pixels);
            image.Apply(false,true);
            rounded=Sprite.Create(image,new Rect(0,0,size,size),Vector2.one*.5f,100f,0,
                SpriteMeshType.FullRect,new Vector4(12,12,12,12));
            return rounded;
        }

        private static RectTransform Icon(Transform parent,HudGlyph glyph,Color color)
        {
            var rt=Rect(glyph.ToString(),parent);
            var icon=rt.gameObject.AddComponent<LifeCraftHudGlyph>();
            icon.Kind=glyph;
            icon.color=color;
            icon.raycastTarget=false;
            return rt;
        }

        private static RectTransform Rect(string name,Transform parent)
        {
            var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent,false);
            return rt;
        }

        private static void Place(RectTransform rect,float ax,float ay,float x,float y,float w,float h)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(ax,ay);
            rect.sizeDelta=new Vector2(w,h);
            rect.anchoredPosition=new Vector2(x,y);
        }

        private static void Stretch(RectTransform rect,float left=0,float bottom=0,float right=0,float top=0)
        {
            rect.anchorMin=Vector2.zero;
            rect.anchorMax=Vector2.one;
            rect.offsetMin=new Vector2(left,bottom);
            rect.offsetMax=new Vector2(-right,-top);
        }

        private static void TopBand(RectTransform rect,float left,float right,float top,float height)
        {
            rect.anchorMin=new Vector2(0,1);
            rect.anchorMax=new Vector2(1,1);
            rect.offsetMin=new Vector2(left,-top-height);
            rect.offsetMax=new Vector2(-right,-top);
        }
    }

    internal enum HudGlyph
    {
        Map, Inventory, Phone, Job, Business, Menu, Profile, Wallet, Coin, Pin, Arrow, Health, Armor, Energy
    }

    internal sealed class LifeCraftHudGlyph : MaskableGraphic
    {
        internal HudGlyph Kind { get; set; }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            switch(Kind)
            {
                case HudGlyph.Map:
                    Chain(mesh,.13f,.19f,.13f,.74f,.39f,.86f,.64f,.75f,.87f,.86f,.87f,.27f,.64f,.14f,.39f,.25f,.13f,.19f);
                    Chain(mesh,.39f,.25f,.39f,.86f);
                    Chain(mesh,.64f,.14f,.64f,.75f);
                    break;
                case HudGlyph.Inventory:
                    Chain(mesh,.15f,.69f,.85f,.69f,.85f,.23f,.15f,.23f,.15f,.69f);
                    Chain(mesh,.36f,.69f,.36f,.81f,.64f,.81f,.64f,.69f);
                    Chain(mesh,.18f,.52f,.82f,.52f);
                    break;
                case HudGlyph.Phone:
                    Chain(mesh,.27f,.87f,.73f,.87f,.73f,.14f,.27f,.14f,.27f,.87f);
                    Chain(mesh,.42f,.28f,.58f,.28f);
                    break;
                case HudGlyph.Job:
                    Chain(mesh,.13f,.25f,.13f,.72f,.87f,.72f,.87f,.25f,.13f,.25f);
                    Chain(mesh,.34f,.72f,.34f,.84f,.66f,.84f,.66f,.72f);
                    Chain(mesh,.15f,.51f,.85f,.51f);
                    Chain(mesh,.44f,.45f,.56f,.45f);
                    break;
                case HudGlyph.Business:
                    Chain(mesh,.15f,.17f,.15f,.7f,.52f,.88f,.85f,.7f,.85f,.17f,.15f,.17f);
                    Chain(mesh,.41f,.18f,.41f,.4f,.61f,.4f,.61f,.18f);
                    Chain(mesh,.33f,.57f,.44f,.57f);
                    Chain(mesh,.58f,.57f,.69f,.57f);
                    break;
                case HudGlyph.Menu:
                    Chain(mesh,.17f,.73f,.83f,.73f);
                    Chain(mesh,.17f,.50f,.83f,.50f);
                    Chain(mesh,.17f,.27f,.83f,.27f);
                    break;
                case HudGlyph.Profile:
                    Disc(mesh,.5f,.68f,.19f);
                    Chain(mesh,.18f,.22f,.27f,.36f,.42f,.42f,.58f,.42f,.73f,.36f,.82f,.22f);
                    break;
                case HudGlyph.Wallet:
                    Chain(mesh,.14f,.24f,.14f,.75f,.84f,.75f,.84f,.24f,.14f,.24f);
                    Chain(mesh,.83f,.58f,.58f,.58f,.58f,.42f,.83f,.42f);
                    break;
                case HudGlyph.Coin:
                    Disc(mesh,.5f,.5f,.34f);
                    Chain(mesh,.5f,.28f,.5f,.72f);
                    break;
                case HudGlyph.Pin:
                    Chain(mesh,.50f,.10f,.21f,.55f,.24f,.72f,.37f,.86f,.63f,.86f,.76f,.72f,.79f,.55f,.50f,.10f);
                    Disc(mesh,.5f,.61f,.12f);
                    break;
                case HudGlyph.Arrow:
                    Triangle(mesh,new Vector2(.5f,.94f),new Vector2(.15f,.08f),new Vector2(.5f,.27f));
                    Triangle(mesh,new Vector2(.5f,.94f),new Vector2(.5f,.27f),new Vector2(.85f,.08f));
                    break;
                case HudGlyph.Health:
                    Chain(mesh,.50f,.19f,.17f,.52f,.15f,.67f,.28f,.79f,.41f,.77f,.50f,.66f,.59f,.77f,
                        .72f,.79f,.85f,.67f,.83f,.52f,.50f,.19f);
                    break;
                case HudGlyph.Armor:
                    Chain(mesh,.50f,.88f,.19f,.73f,.22f,.40f,.32f,.26f,.5f,.15f,.68f,.26f,.78f,.40f,.81f,.73f,.50f,.88f);
                    break;
                case HudGlyph.Energy:
                    Chain(mesh,.57f,.90f,.21f,.45f,.48f,.45f,.40f,.10f,.79f,.61f,.52f,.61f,.57f,.90f);
                    break;
            }
        }

        private void Chain(VertexHelper mesh,params float[] points)
        {
            for(int i=2;i<points.Length;i+=2)
            {
                var a=new Vector2(points[i-2],points[i-1]);
                var b=new Vector2(points[i],points[i+1]);
                Vector2 axis=(b-a).normalized;
                var normal=new Vector2(-axis.y,axis.x)*.041f;
                Triangle(mesh,a-normal,a+normal,b+normal);
                Triangle(mesh,a-normal,b+normal,b-normal);
            }
        }

        private void Disc(VertexHelper mesh,float x,float y,float radius)
        {
            const int count=14;
            for(int i=0;i<count;i++)
            {
                float a=2f*Mathf.PI*i/count;
                float b=2f*Mathf.PI*(i+1)/count;
                Vector2 center=new Vector2(x,y);
                Vector2 left=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                Vector2 right=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                Triangle(mesh,center+left*(radius-.055f),center+left*radius,center+right*radius);
                Triangle(mesh,center+left*(radius-.055f),center+right*radius,center+right*(radius-.055f));
            }
        }

        private void Triangle(VertexHelper mesh,Vector2 a,Vector2 b,Vector2 c)
        {
            int offset=mesh.currentVertCount;
            var rect=rectTransform.rect;
            Push(mesh,rect,a);
            Push(mesh,rect,b);
            Push(mesh,rect,c);
            mesh.AddTriangle(offset,offset+1,offset+2);
        }

        private void Push(VertexHelper mesh,Rect rect,Vector2 p)
        {
            var vertex=UIVertex.simpleVert;
            vertex.position=new Vector3(rect.xMin+p.x*rect.width,rect.yMin+p.y*rect.height,0);
            vertex.color=color;
            mesh.AddVert(vertex);
        }
    }
}
