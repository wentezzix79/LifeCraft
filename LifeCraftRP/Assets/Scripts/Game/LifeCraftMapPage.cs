using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using LifeCraft.World;
using UnityEngine;
using UnityEngine.UI;

namespace LifeCraftRP
{
    internal sealed class LifeCraftMapPage
    {
        private const string FavoritesKey = "LifeCraft.Map.Favorites";
        private static readonly Color Shell = new Color(.09f,.071f,.132f);
        private static readonly Color Card = new Color(.145f,.117f,.206f);
        private static readonly Color Line = new Color(.21f,.17f,.30f);
        private static readonly Color Active = new Color(.39f,.245f,.69f);
        private static readonly Color Ink = new Color(.97f,.95f,1f);
        private static readonly Color Muted = new Color(.74f,.70f,.82f);
        private static readonly Color RouteColor = new Color(1f,.47f,.286f);
        private static readonly Color MapInk = new Color(.12f,.084f,.19f);
        private static readonly string[] Layers = {"city","jobs","pickup","services","transit","district"};
        private static readonly string[] LayerNames = {"Места","Работы","ПВЗ","Услуги","Остановки","Районы"};

        private readonly GameManager game;
        private readonly Font font;
        private readonly Action close;
        private readonly RectTransform root,safeArea;
        private readonly LifeCraftMapViewport viewport;
        private readonly InputField search;
        private readonly Text empty;
        private readonly Text selectionTitle, selectionDetail, modeLabel, routeLabel;
        private readonly Text listLabel,listSwitchLabel,favoriteLabel,routeHint,scale;
        private readonly RectTransform visitButton,favoriteButton,routeButton,modeButton,cancelButton;
        private readonly ScrollRect results;
        private readonly RectTransform resultContent;
        private readonly RectTransform playerMarker,scaleBar;
        private readonly Text[] filters = new Text[Layers.Length];
        private readonly List<OmskMapCatalog.Point> points = new List<OmskMapCatalog.Point>();
        private readonly Dictionary<string,OmskLandmarks.Site> landmarks = new Dictionary<string,OmskLandmarks.Site>();
        private readonly Dictionary<OmskMapCatalog.Point,Pin> markerByPoint = new Dictionary<OmskMapCatalog.Point,Pin>();
        private readonly List<Pin> markerPool = new List<Pin>();
        private readonly List<OmskMapCatalog.Point> resultsBuffer = new List<OmskMapCatalog.Point>();
        private readonly List<OmskMapCatalog.Point> visiblePoints = new List<OmskMapCatalog.Point>();
        private readonly List<Rect> occupiedLabels = new List<Rect>();
        private readonly HashSet<string> favorites = new HashSet<string>();
        private readonly HashSet<string> visibleLayers = new HashSet<string>(Layers);
        private readonly List<GameObject> resultRows = new List<GameObject>();

        private OmskMapCatalog.Point selection;
        private OmskMapCatalog.Point destination;
        private OmskMapCatalog.Point droppedPin;
        private Vector2[] route = Array.Empty<Vector2>();
        private float[] remaining = Array.Empty<float>();
        private bool favoritesOnly,driving,initialized,planning,routeDriving;
        private float refreshAt,messageUntil;
        private Rect lastSafeArea;
        private Vector2Int lastResolution;
        private string routeMessage = string.Empty;
        private static Sprite rounded;
        private static Sprite arrow;

        private sealed class Pin
        {
            internal RectTransform Rect;
            internal Text Text;
            internal OmskMapCatalog.Point Point;
        }

        internal LifeCraftMapPage(GameManager game,RectTransform screen,Font font,Action close)
        {
            this.game=game;
            this.close=close;
            this.font=font;
            points.AddRange(OmskMapCatalog.Load());
            foreach (var site in game.World.Landmarks)
            {
                landmarks[site.id]=site;
                points.Add(new OmskMapCatalog.Point
                {
                    id=site.id,name=site.name,detail=site.address,category="city",x=site.Center.x,z=site.Center.z
                });
            }
            foreach (string key in PlayerPrefs.GetString(FavoritesKey,string.Empty).Split('|'))
                if (!string.IsNullOrEmpty(key))
                    favorites.Add(key);

            root=Panel(screen,"Атлас Омска",Shell,true,false);
            Stretch(root,0,0,0,0);
            safeArea=Rect(root,"Область карты");
            Stretch(safeArea,0,0,0,0);
            float sideWidth=Application.isMobilePlatform ? 305f : 348f;
            RectTransform header=Panel(safeArea,"Панель поиска",new Color(.105f,.078f,.166f),true,false);
            Stretch(header,0,0,0,0);
            header.anchorMin=new Vector2(0,1);
            header.offsetMin=new Vector2(0,-86);
            Text title=Label(header,"АТЛАС ОМСКА",28,Ink,FontStyle.Bold);
            Place(title.rectTransform,0,1,24,-8,270,39);
            Place(Label(header,"LIFE CRAFT RP 2.0",14,new Color(.788f,.64f,.98f),FontStyle.Bold)
                .rectTransform,0,1,26,-48,215,26);

            RectTransform query=Panel(header,"Поиск",Card,true);
            query.anchorMin=new Vector2(.25f,.5f);
            query.anchorMax=new Vector2(.76f,.5f);
            query.pivot=new Vector2(.5f,.5f);
            query.offsetMin=new Vector2(8,-24);
            query.offsetMax=new Vector2(-8,24);
            search=query.gameObject.AddComponent<InputField>();
            search.targetGraphic=query.GetComponent<Image>();
            search.textComponent=Label(query,"",18,Ink);
            var ghost=Label(query,"Найти место или адрес",18,Muted);
            search.placeholder=ghost;
            Stretch(search.textComponent.rectTransform,17,4,14,4);
            Stretch(ghost.rectTransform,17,4,14,4);
            search.characterLimit=80;
            search.onValueChanged.AddListener(_=>RefreshResults());
            search.onEndEdit.AddListener(_=>
            {
                if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                    resultsBuffer.Count>0)
                    Select(resultsBuffer[0],true);
            });
            Place(Button(header,"Игра  ×",close,out _),1,1,-24,-19,113,48);

            RectTransform area=Panel(safeArea,"Картографический экран",Card,true);
            Stretch(area,24,62,sideWidth+39,102);
            viewport=LifeCraftMapViewport.Create(area);
            viewport.Changed+=UpdateMapMarkers;
            viewport.MapClicked+=PlaceDroppedPin;
            playerMarker=Panel(viewport.MarkerLayer,"Игрок",new Color(.37f,.13f,.70f),false);
            playerMarker.sizeDelta=new Vector2(24,31);
            playerMarker.GetComponent<Image>().sprite=ArrowSprite();
            playerMarker.GetComponent<Image>().type=Image.Type.Simple;

            Place(Button(area,"Обзор",()=>{ viewport.Fit(); },out _),0,1,14,-15,105,44);
            Place(Button(area,"К игроку",()=>viewport.Center(game.Player.Position,2400),out _),0,1,132,-15,112,44);
            Place(Button(area,"+",()=>viewport.Zoom(1.32f),out _),1,0,-12,64,51,48);
            Place(Button(area,"−",()=>viewport.Zoom(1f/1.32f),out _),1,0,-12,12,51,48);
            var scaleBox=Panel(area,"Масштаб карты",Shell,false);
            Place(scaleBox,0,0,13,12,140,32);
            scale=Label(scaleBox,"",14,Ink,FontStyle.Bold,TextAnchor.MiddleCenter);
            Stretch(scale.rectTransform,3,7,3,2);
            scaleBar=Panel(scaleBox,"Линейка масштаба",Ink,false,false);
            Place(scaleBar,.5f,0,0,5,90,2);

            var sidebar=Panel(safeArea,"Панель мест",Card,true);
            sidebar.anchorMin=new Vector2(1,0);
            sidebar.anchorMax=new Vector2(1,1);
            sidebar.pivot=new Vector2(1,.5f);
            sidebar.offsetMin=new Vector2(-sideWidth-24,62);
            sidebar.offsetMax=new Vector2(-24,-102);
            Place(Label(sidebar,"Слои",20,Ink,FontStyle.Bold).rectTransform,0,1,15,-10,220,31);
            for (int i=0;i<Layers.Length;i++)
            {
                string layer=Layers[i];
                var button=Button(sidebar,LayerNames[i],()=>ToggleLayer(layer),out filters[i]);
                Place(button,0,1,15+(i%2)*(sideWidth-22)*.5f,-48-(i/2)*39,(sideWidth-45)*.5f,34);
                filters[i].fontSize=15;
            }

            listLabel=Label(sidebar,"Места рядом",18,Ink,FontStyle.Bold);
            Place(listLabel.rectTransform,0,1,15,-172,sideWidth-165,34);
            var switchList=Button(sidebar,"Избранное",()=>
            {
                favoritesOnly=!favoritesOnly;
                RefreshResults();
            },out listSwitchLabel);
            listSwitchLabel.fontSize=14;
            Place(switchList,1,1,-15,-172,117,34);

            RectTransform list=Panel(sidebar,"Результаты",Shell,true);
            Stretch(list,12,192,12,215);
            list.gameObject.AddComponent<RectMask2D>();
            results=list.gameObject.AddComponent<ScrollRect>();
            results.horizontal=false;
            results.vertical=true;
            results.movementType=ScrollRect.MovementType.Clamped;
            results.scrollSensitivity=25;
            results.viewport=list;
            resultContent=Rect(list,"Строки");
            resultContent.anchorMin=new Vector2(0,1);
            resultContent.anchorMax=new Vector2(1,1);
            resultContent.pivot=new Vector2(.5f,1);
            resultContent.sizeDelta=Vector2.zero;
            results.content=resultContent;
            empty=Label(list,"",15,Muted);
            Place(empty.rectTransform,0,1,14,-9,sideWidth-56,50);

            var detail=Panel(sidebar,"Выбранное место",Shell,true);
            Place(detail,0,0,12,12,sideWidth-24,166);
            selectionTitle=Label(detail,"",19,Ink,FontStyle.Bold);
            Place(selectionTitle.rectTransform,0,1,11,-7,sideWidth-48,30);
            selectionDetail=Label(detail,"",14,Muted);
            Place(selectionDetail.rectTransform,0,1,11,-40,sideWidth-48,35);
            favoriteButton=Button(detail,"В избранное",ToggleFavorite,out favoriteLabel);
            favoriteLabel.fontSize=13;
            Place(favoriteButton,0,0,8,9,sideWidth*.46f-9,34);
            visitButton=Button(detail,"Посетить",Visit,out _);
            Place(visitButton,1,0,-8,9,sideWidth*.46f-9,34);
            modeButton=Button(detail,"Пешком",ToggleMode,out modeLabel);
            Place(modeButton,0,0,8,51,sideWidth*.40f-6,38);
            routeButton=Button(detail,"Маршрут",Plan,out routeLabel,true);
            Place(routeButton,1,0,-8,51,sideWidth*.54f-8,38);

            routeHint=Label(safeArea,"",16,Ink,FontStyle.Bold);
            Place(routeHint.rectTransform,0,0,24,12,680,40);
            cancelButton=Button(safeArea,"Сбросить маршрут",ClearRoute,out _);
            Place(cancelButton,1,0,-24,11,205,39);
            Text attribution=Label(safeArea,OmskWorld.Attribution,12,Muted,FontStyle.Normal,TextAnchor.MiddleRight);
            Place(attribution.rectTransform,1,0,-245,13,300,31);
            UpdateFilterText();
            UpdateSelection();
            UpdateRouteHint();
            root.gameObject.SetActive(false);
        }

        internal Vector2[] Route => route;
        internal string NavigationSummary => routeMessage;

        internal void Show(string focusId)
        {
            root.gameObject.SetActive(true);
            FitSafeArea();
            if (!initialized)
            {
                initialized=true;
                Canvas.ForceUpdateCanvases();
                viewport.Center(game.Player.Position,15400f);
            }
            if (!string.IsNullOrEmpty(focusId))
            {
                var point=points.Find(p=>p.id==focusId);
                if (point!=null)
                {
                    visibleLayers.Add(point.category);
                    UpdateFilterText();
                    Select(point,true);
                }
            }
            RefreshResults();
            UpdateMapMarkers();
        }

        internal void Hide() => root.gameObject.SetActive(false);

        internal void Notify(string message)
        {
            if (route.Length==0)
                SetMessage(message,5f);
        }

        internal void Tick()
        {
            if (Time.unscaledTime < refreshAt)
                return;
            refreshAt=Time.unscaledTime+.23f;
            if (root.gameObject.activeSelf)
            {
                FitSafeArea();
                UpdatePlayerMarker();
            }
            if (destination!=null && route.Length>0)
            {
                Vector3 delta=DestinationPosition(destination)-game.Player.Position;
                delta.y=0;
                if (delta.sqrMagnitude<27f*27f)
                {
                    string name = destination.name;
                    ClearRoute();
                    SetMessage("Вы у цели · "+name,8f);
                }
                else
                    UpdateProgress();
            }
            else if (messageUntil>0f && Time.unscaledTime>=messageUntil)
                SetMessage(string.Empty);
        }

        private void FitSafeArea()
        {
            Rect bounds=Screen.safeArea;
            var resolution=new Vector2Int(Screen.width,Screen.height);
            if (resolution.x<=0 || resolution.y<=0 ||
                (lastSafeArea==bounds && lastResolution==resolution))
                return;
            lastSafeArea=bounds;
            lastResolution=resolution;
            safeArea.anchorMin=new Vector2(bounds.xMin/resolution.x,bounds.yMin/resolution.y);
            safeArea.anchorMax=new Vector2(bounds.xMax/resolution.x,bounds.yMax/resolution.y);
            safeArea.offsetMin=safeArea.offsetMax=Vector2.zero;
        }

        private void ToggleLayer(string layer)
        {
            if (!visibleLayers.Remove(layer))
                visibleLayers.Add(layer);
            UpdateFilterText();
            viewport.SetDistricts(visibleLayers.Contains("district"));
            RefreshResults();
            UpdateMapMarkers();
        }

        private void UpdateFilterText()
        {
            for (int i=0;i<Layers.Length;i++)
            {
                bool on=visibleLayers.Contains(Layers[i]);
                filters[i].text=(on?"●  ":"○  ")+LayerNames[i];
                filters[i].color=on?Ink:Muted;
                filters[i].transform.parent.GetComponent<Image>().color=on?Active:Line;
            }
        }

        private void RefreshResults()
        {
            string query=search.text.Trim();
            resultsBuffer.Clear();
            foreach (var point in points)
            {
                if (!visibleLayers.Contains(point.category) || (favoritesOnly && !favorites.Contains(point.id)))
                    continue;
                if (query.Length==0 && point.category=="transit" && !favoritesOnly)
                    continue;
                if (query.Length==0 || point.Matches(query))
                    resultsBuffer.Add(point);
            }
            Vector3 from=game.Player.Position;
            resultsBuffer.Sort((a,b)=>(a.Position-from).sqrMagnitude.CompareTo((b.Position-from).sqrMagnitude));
            listLabel.text=favoritesOnly?"Избранные места":query.Length==0?"Места рядом":"Найдено "+resultsBuffer.Count;
            listSwitchLabel.text=favoritesOnly?"Все места":"Избранное";
            foreach (GameObject old in resultRows)
            {
                old.SetActive(false);
                UnityEngine.Object.Destroy(old);
            }
            resultRows.Clear();
            int count=Mathf.Min(28,resultsBuffer.Count);
            resultContent.sizeDelta=new Vector2(0,count*46+10);
            var bounds=results.viewport.rect;
            if (bounds.height>0 && resultContent.rect.height<bounds.height)
                resultContent.sizeDelta=new Vector2(0,bounds.height);
            for (int i=0;i<count;i++)
            {
                OmskMapCatalog.Point point=resultsBuffer[i];
                var item=Button(resultContent,point.name,()=>Select(point,true),out var caption);
                caption.alignment=TextAnchor.MiddleLeft;
                caption.fontSize=14;
                Stretch(caption.rectTransform,33,2,7,2);
                var badge=Panel(item,point.category,PinColor(point.category),false);
                Place(badge,0,.5f,7,0,19,20);
                var badgeText=Label(badge,Symbol(point.category),11,point.category=="district"?MapInk:Ink,
                    FontStyle.Bold,TextAnchor.MiddleCenter);
                Stretch(badgeText.rectTransform,0,0,0,0);
                item.anchorMin=new Vector2(0,1);
                item.anchorMax=new Vector2(1,1);
                item.pivot=new Vector2(.5f,1);
                item.offsetMin=new Vector2(6,-(i+1)*46+2);
                item.offsetMax=new Vector2(-6,-i*46-2);
                resultRows.Add(item.gameObject);
            }
            results.verticalNormalizedPosition=1;
            empty.text=count==0?"Подходящих мест нет.":string.Empty;
        }

        private void Select(OmskMapCatalog.Point point,bool focus)
        {
            selection=point;
            if (focus)
                viewport.Center(point.Position,Mathf.Min(viewport.View.width,2100f));
            UpdateSelection();
            UpdateMapMarkers();
        }

        private void PlaceDroppedPin(Vector3 position)
        {
            droppedPin=new OmskMapCatalog.Point
            {
                id="map-pin",name="Метка на карте",detail="Выбранная точка",category="city",
                x=position.x,z=position.z
            };
            Select(droppedPin,false);
        }

        private void UpdateSelection()
        {
            selectionTitle.text=selection?.name ?? "Выберите место";
            selectionDetail.text=selection?.detail ?? "Нажмите на метку или найдите адрес.";
            favoriteLabel.text=selection!=null && favorites.Contains(selection.id) ? "Убрать из избранного" : "В избранное";
            bool has=selection!=null;
            favoriteButton.gameObject.SetActive(has && selection!=droppedPin);
            visitButton.gameObject.SetActive(has && landmarks.ContainsKey(selection.id) && !game.Online);
            routeButton.gameObject.SetActive(has);
            modeButton.gameObject.SetActive(has);
            modeLabel.text=driving?"На машине":"Пешком";
            routeButton.GetComponent<Button>().interactable=!planning;
            routeLabel.text=planning?"Поиск…":selection!=null && selection==destination && route.Length>0
                ? "Перестроить" : "Маршрут";
        }

        private void ToggleFavorite()
        {
            if (selection==null || selection==droppedPin)
                return;
            if (!favorites.Remove(selection.id))
                favorites.Add(selection.id);
            PlayerPrefs.SetString(FavoritesKey,string.Join("|",favorites));
            PlayerPrefs.Save();
            RefreshResults();
            UpdateSelection();
        }

        private void Visit()
        {
            if (selection!=null && landmarks.TryGetValue(selection.id,out var site) && game.VisitOmskLandmark(site))
            {
                ClearRoute();
                close();
            }
        }

        private void ToggleMode()
        {
            driving=!driving;
            UpdateSelection();
        }

        private Vector3 DestinationPosition(OmskMapCatalog.Point point)
            => landmarks.TryGetValue(point.id,out var site) ? site.Arrival : point.Position;

        private void Plan()
        {
            if (selection==null || planning)
                return;
            planning=true;
            UpdateSelection();
            game.StartCoroutine(PlanRoute(selection,game.Player.Position,driving));
        }

        private IEnumerator PlanRoute(OmskMapCatalog.Point target,Vector3 origin,bool drive)
        {
            yield return null;
            planning=false;
            if (OmskNavigation.TryFind(origin,DestinationPosition(target),drive,
                out Vector2[] path,out float length) && path.Length>=2)
            {
                route=path;
                remaining=new float[path.Length];
                for (int i=path.Length-2;i>=0;i--)
                    remaining[i]=remaining[i+1]+Vector2.Distance(path[i],path[i+1]);
                destination=target;
                routeDriving=drive;
                float minX=path[0].x,maxX=minX,minZ=path[0].y,maxZ=minZ;
                foreach (var p in path)
                {
                    minX=Mathf.Min(minX,p.x); maxX=Mathf.Max(maxX,p.x);
                    minZ=Mathf.Min(minZ,p.y); maxZ=Mathf.Max(maxZ,p.y);
                }
                float aspect=viewport.View.width/Mathf.Max(1f,viewport.View.height);
                float width=Mathf.Max(450f,Mathf.Max(maxX-minX,(maxZ-minZ)*aspect)*1.27f);
                viewport.Center(new Vector3((minX+maxX)*.5f,0,(minZ+maxZ)*.5f),width);
                viewport.SetRoute(route);
                SetMessage((drive?"На машине":"Пешком")+" · "+Distance(length)+" · "+target.name);
            }
            else
            {
                ClearRoute();
                SetMessage("Маршрут по улицам не найден.",8f);
            }
            UpdateSelection();
            UpdateRouteHint();
            UpdateMapMarkers();
        }

        private void ClearRoute()
        {
            route=Array.Empty<Vector2>();
            remaining=Array.Empty<float>();
            destination=null;
            viewport.SetRoute(route);
            SetMessage(string.Empty);
            UpdateSelection();
        }

        private void UpdateProgress()
        {
            Vector3 position=game.Player.Position;
            var here=new Vector2(position.x,position.z);
            float minDistance=float.PositiveInfinity,left=remaining[0];
            for (int i=0;i+1<route.Length;i++)
            {
                Vector2 a=route[i], segment=route[i+1]-a;
                float length=segment.sqrMagnitude;
                if (length<.001f)
                    continue;
                float t=Mathf.Clamp01(Vector2.Dot(here-a,segment)/length);
                float distance=(here-a-segment*t).sqrMagnitude;
                if (distance>=minDistance)
                    continue;
                minDistance=distance;
                left=remaining[i+1]+Mathf.Sqrt(length)*(1f-t);
            }
            float limit=routeDriving?115f:74f;
            SetMessage(minDistance>limit*limit ? "Вне маршрута · перестройте его на карте" :
                "Осталось "+Distance(left)+" · "+destination.name);
        }

        private void SetMessage(string value,float duration=0f)
        {
            messageUntil=duration>0f ? Time.unscaledTime+duration : 0f;
            if (routeMessage==value)
                return;
            routeMessage=value;
            UpdateRouteHint();
        }

        private void UpdateRouteHint()
        {
            routeHint.text=string.IsNullOrEmpty(routeMessage)
                ? Application.isMobilePlatform ? "Сдвиньте карту пальцем." : "Перетащите карту. Колесо меняет масштаб."
                : routeMessage;
            routeHint.color=route.Length>0 ? RouteColor : Muted;
            cancelButton.gameObject.SetActive(route.Length>0);
        }

        private void UpdateMapMarkers()
        {
            if (viewport==null || playerMarker==null || scale==null)
                return;
            visiblePoints.Clear();
            foreach (var point in points)
            {
                if (!visibleLayers.Contains(point.category) || !viewport.Project(point.Position,out _))
                    continue;
                if (point.category=="transit" && viewport.View.width>5000f && point!=selection)
                    continue;
                if (point.category=="pickup" && viewport.View.width>10500f && point!=selection)
                    continue;
                visiblePoints.Add(point);
            }
            if (droppedPin!=null && viewport.Project(droppedPin.Position,out _))
                visiblePoints.Add(droppedPin);
            Vector3 center=new Vector3(viewport.View.center.x,0,viewport.View.center.y);
            visiblePoints.Sort((a,b)=>
            {
                int rank=Rank(a).CompareTo(Rank(b));
                return rank!=0?rank:(a.Position-center).sqrMagnitude.CompareTo((b.Position-center).sqrMagnitude);
            });
            if (visiblePoints.Count>100)
                visiblePoints.RemoveRange(100,visiblePoints.Count-100);
            foreach (Pin pin in markerPool)
            {
                if (pin.Point==null || visiblePoints.Contains(pin.Point))
                    continue;
                markerByPoint.Remove(pin.Point);
                pin.Point=null;
                pin.Rect.gameObject.SetActive(false);
            }
            occupiedLabels.Clear();
            Vector2 markerSize=viewport.MarkerLayer.rect.size;
            foreach (var point in visiblePoints)
            {
                if (!markerByPoint.TryGetValue(point,out Pin pin))
                {
                    pin=markerPool.Find(p=>p.Point==null) ?? CreatePin();
                    pin.Point=point;
                    markerByPoint.Add(point,pin);
                    pin.Rect.gameObject.SetActive(true);
                }
                viewport.Project(point.Position,out var normalized);
                pin.Rect.anchorMin=pin.Rect.anchorMax=normalized;
                pin.Rect.anchoredPosition=Vector2.zero;
                bool isLabel=point.category=="district" || point==selection ||
                    (point.category=="jobs" || landmarks.ContainsKey(point.id)) && viewport.View.width<22000f;
                float size=isLabel ? Mathf.Clamp(point.name.Length*8.3f+16f,58f,225f) : 24f;
                if (isLabel)
                {
                    Vector2 px=Vector2.Scale(normalized,markerSize);
                    Rect textBounds=new Rect(px.x-size*.5f-4f,px.y-17f,size+8f,34f);
                    if (point!=selection && occupiedLabels.Exists(other=>textBounds.Overlaps(other)))
                    {
                        isLabel=false;
                        size=24f;
                    }
                    else
                        occupiedLabels.Add(textBounds);
                }
                pin.Rect.sizeDelta=new Vector2(size,isLabel?31:24);
                pin.Text.text=isLabel ? point.name : Symbol(point.category);
                pin.Text.fontSize=isLabel?14:12;
                pin.Text.color=point.category=="district" && point!=selection?MapInk:Color.white;
                pin.Rect.GetComponent<Image>().color=point==selection?Active:PinColor(point.category);
                if (point==selection)
                    pin.Rect.SetAsLastSibling();
            }
            float metres=viewport.View.width*112f/Mathf.Max(1f,markerSize.x);
            float unit=Mathf.Pow(10f,Mathf.Floor(Mathf.Log10(Mathf.Max(1f,metres))));
            float value=metres>=5f*unit?5f*unit:metres>=2f*unit?2f*unit:unit;
            scale.text=Distance(value);
            scaleBar.sizeDelta=new Vector2(Mathf.Min(112f,value/viewport.View.width*markerSize.x),2);
            playerMarker.SetAsLastSibling();
            UpdatePlayerMarker();
        }

        private int Rank(OmskMapCatalog.Point point) => point==selection ? 0 :
            point.category=="jobs" || landmarks.ContainsKey(point.id) ? 1 : point.category=="transit" ? 4 :
            point.category=="pickup" ? 3 : 2;

        private Pin CreatePin()
        {
            var pin=new Pin();
            pin.Rect=Button(viewport.MarkerLayer,"",()=>
            {
                if (pin.Point!=null)
                    Select(pin.Point,false);
            },out var label);
            pin.Text=label;
            pin.Rect.pivot=new Vector2(.5f,.5f);
            markerPool.Add(pin);
            return pin;
        }

        private void UpdatePlayerMarker()
        {
            bool show=viewport.Project(game.Player.Position,out var p);
            playerMarker.gameObject.SetActive(show);
            if (show)
            {
                playerMarker.anchorMin=playerMarker.anchorMax=p;
                playerMarker.anchoredPosition=Vector2.zero;
                playerMarker.localRotation=Quaternion.Euler(0,0,-game.Player.transform.eulerAngles.y);
            }
        }

        private static string Symbol(string category)
        {
            switch (category)
            {
                case "pickup": return "П";
                case "jobs": return "Р";
                case "transit": return "Т";
                case "services": return "С";
                default: return "•";
            }
        }

        private static Color PinColor(string category)
        {
            switch (category)
            {
                case "jobs": return new Color(.79f,.34f,.12f);
                case "pickup": return new Color(.42f,.234f,.77f);
                case "transit": return new Color(.12f,.40f,.70f);
                case "services": return new Color(.116f,.47f,.44f);
                case "district": return new Color(.97f,.96f,1f,.90f);
                default: return new Color(.24f,.14f,.41f);
            }
        }

        private static string Distance(float m)
        {
            return m>=1000f ? (m/1000f).ToString("0.#",CultureInfo.GetCultureInfo("ru-RU"))+" км" :
                Mathf.RoundToInt(m)+" м";
        }

        private RectTransform Button(RectTransform parent,string title,Action click,out Text label,bool primary=false)
        {
            var rect=Panel(parent,title,primary?Active:Line,true);
            var button=rect.gameObject.AddComponent<Button>();
            button.targetGraphic=rect.GetComponent<Image>();
            ColorBlock colors=button.colors;
            colors.highlightedColor=new Color(1f,.92f,1f);
            colors.pressedColor=new Color(.82f,.74f,.98f);
            button.colors=colors;
            button.onClick.AddListener(()=>click());
            label=Label(rect,title,16,Ink,FontStyle.Bold,TextAnchor.MiddleCenter);
            Stretch(label.rectTransform,2,1,2,1);
            return rect;
        }

        private Text Label(RectTransform parent,string value,int size,Color color,
            FontStyle style=FontStyle.Normal,TextAnchor align=TextAnchor.MiddleLeft)
        {
            Text t=Rect(parent,value).gameObject.AddComponent<Text>();
            t.text=value;
            t.font=font;
            t.fontSize=size;
            t.color=color;
            t.fontStyle=style;
            t.alignment=align;
            t.horizontalOverflow=HorizontalWrapMode.Wrap;
            t.verticalOverflow=VerticalWrapMode.Truncate;
            t.raycastTarget=false;
            return t;
        }

        private static RectTransform Panel(RectTransform parent,string name,Color color,bool raycast,bool curved=true)
        {
            RectTransform rect=Rect(parent,name);
            var image=rect.gameObject.AddComponent<Image>();
            image.color=color;
            image.raycastTarget=raycast;
            if (curved)
            {
                image.sprite=RoundedSprite();
                image.type=Image.Type.Sliced;
            }
            return rect;
        }

        private static RectTransform Rect(RectTransform parent,string name)
        {
            var child=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            child.SetParent(parent,false);
            return child;
        }

        private static void Stretch(RectTransform rect,float left,float bottom,float right,float top)
        {
            rect.anchorMin=Vector2.zero;
            rect.anchorMax=Vector2.one;
            rect.offsetMin=new Vector2(left,bottom);
            rect.offsetMax=new Vector2(-right,-top);
        }

        private static void Place(RectTransform rect,float ax,float ay,float x,float y,float w,float h)
        {
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(ax,ay);
            rect.anchoredPosition=new Vector2(x,y);
            rect.sizeDelta=new Vector2(w,h);
        }

        private static Sprite RoundedSprite()
        {
            if (rounded) return rounded;
            const int size=36;
            const int radius=11;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            texture.wrapMode=TextureWrapMode.Clamp;
            var pixels=new Color32[size*size];
            for (int y=0;y<size;y++)
            for (int x=0;x<size;x++)
            {
                float dx=x-Mathf.Clamp(x,radius,size-1-radius);
                float dy=y-Mathf.Clamp(y,radius,size-1-radius);
                byte alpha=(byte)(Mathf.Clamp01(radius+.5f-Mathf.Sqrt(dx*dx+dy*dy))*255f);
                pixels[y*size+x]=new Color32(255,255,255,alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false,true);
            rounded=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),100,0,
                SpriteMeshType.FullRect,new Vector4(12,12,12,12));
            return rounded;
        }

        private static Sprite ArrowSprite()
        {
            if (arrow) return arrow;
            const int size=40;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            texture.wrapMode=TextureWrapMode.Clamp;
            var pixels=new Color32[size*size];
            for (int y=3;y<37;y++)
            for (int x=0;x<size;x++)
            {
                float d=Mathf.Abs(x-19.5f);
                if (d<=(37-y)*.47f && y>=11f-d*.62f)
                    pixels[y*size+x]=new Color32(255,255,255,255);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false,true);
            arrow=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f));
            return arrow;
        }
    }
}
