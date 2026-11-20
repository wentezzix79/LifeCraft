using System;
using System.Collections.Generic;
using LifeCraft.World;
using UnityEngine;
using UnityEngine.UI;

namespace LifeCraftRP
{
    internal sealed partial class LifeCraftMapPage
    {
        private const float BusStopZoom = 5000f;
        private static readonly Color BusInk = new Color(.63f,1f,.89f);
        private static readonly Color BusPin = new Color(.13f,.52f,.45f);
        private static readonly Color BusStopPin = new Color(.16f,.35f,.63f);

        private RectTransform busPane,busMarkerLayer,busStopLayer,busVehicleLayer;
        private RectTransform placesTab,busesTab,busRouteTab,busStopTab,allBusRoutes,locateBus;
        private Text busListTitle,busEmpty,busDetailTitle,busDetailHint,busArrivalEmpty,locateBusText;
        private ScrollRect busResults,busArrivals;
        private readonly List<GameObject> busResultRows = new List<GameObject>();
        private readonly List<OmskBusRoute> matchingBusRoutes = new List<OmskBusRoute>();
        private readonly List<OmskBusStop> matchingBusStops = new List<OmskBusStop>();
        private readonly List<OmskBusArrival> stopArrivals = new List<OmskBusArrival>();
        private readonly HashSet<string> arrivalKeys = new HashSet<string>();
        private readonly HashSet<int> occupiedBusCells = new HashSet<int>();
        private readonly List<BusMapMarker> busStopPins = new List<BusMapMarker>();
        private readonly Dictionary<string,BusMapMarker> busVehiclePins = new Dictionary<string,BusMapMarker>();
        private readonly List<ArrivalRow> arrivalRows = new List<ArrivalRow>();
        private OmskBusRoute selectedBusRoute;
        private OmskBusStop selectedBusStop;
        private OmskLiveBus selectedBus;
        private Rect lastPlaceView,lastBusView;
        private Vector2[] busPath = Array.Empty<Vector2>();
        private string placeQuery="",busQuery="";
        private bool busMode,browseStops=true;
        private int shownArrivals;

        private sealed class BusMapMarker
        {
            internal RectTransform Rect;
            internal Text Label;
            internal OmskBusStop Stop;
            internal OmskLiveBus Vehicle;
            internal bool Visible;
        }

        private sealed class ArrivalRow
        {
            internal RectTransform Rect;
            internal Text Text;
            internal OmskLiveBus Vehicle;
        }

        private void BuildBusPane(RectTransform sidebar,float sideWidth)
        {
            float tabWidth=(sideWidth-44)*.5f;
            placesTab=Button(sidebar,"Карта",()=>SetBusMode(false),out _,true);
            busesTab=Button(sidebar,"Автобусы",()=>SetBusMode(true),out _);
            Place(placesTab,0,1,15,-7,tabWidth,34);
            Place(busesTab,1,1,-15,-7,tabWidth,34);

            busPane=Rect(sidebar,"Автобусы Омска");
            Stretch(busPane,0,0,0,0);
            busRouteTab=Button(busPane,"Маршруты",()=>SetBusList(false),out _);
            busStopTab=Button(busPane,"Остановки",()=>SetBusList(true),out _,true);
            Place(busRouteTab,0,1,15,-48,tabWidth,34);
            Place(busStopTab,1,1,-15,-48,tabWidth,34);
            busListTitle=Label(busPane,"",16,Ink,FontStyle.Bold);
            Place(busListTitle.rectTransform,0,1,15,-90,sideWidth-30,26);

            busResults=CreateBusList(busPane,"Маршруты и остановки");
            Stretch(busResults.viewport,12,218,12,124);
            busEmpty=Label(busResults.viewport,"",14,Muted);
            Place(busEmpty.rectTransform,0,1,12,-6,sideWidth-48,51);

            var detail=Panel(busPane,"Прибытие автобусов",Shell,true);
            Place(detail,0,0,12,12,sideWidth-24,195);
            busDetailTitle=Label(detail,"",17,Ink,FontStyle.Bold);
            busDetailTitle.resizeTextForBestFit=true;
            busDetailTitle.resizeTextMinSize=13;
            busDetailTitle.resizeTextMaxSize=17;
            Place(busDetailTitle.rectTransform,0,1,11,-4,sideWidth-48,38);
            busDetailHint=Label(detail,"",13,BusInk);
            Place(busDetailHint.rectTransform,0,1,11,-41,sideWidth-48,30);
            busArrivals=CreateBusList(detail,"Ближайшие рейсы");
            Stretch(busArrivals.viewport,7,48,7,73);
            busArrivalEmpty=Label(busArrivals.viewport,"",13,Muted);
            Place(busArrivalEmpty.rectTransform,0,1,5,-4,sideWidth-55,52);
            allBusRoutes=Button(detail,"Все маршруты",ClearBusRoute,out var clearLabel);
            locateBus=Button(detail,"К остановке",LocateBusSelection,out locateBusText);
            clearLabel.fontSize=13;
            locateBusText.fontSize=13;
            Place(allBusRoutes,0,0,8,8,tabWidth-4,33);
            Place(locateBus,1,0,-8,8,tabWidth-4,33);

            busMarkerLayer=Rect(viewport.MarkerLayer,"Метки автобусов");
            Stretch(busMarkerLayer,0,0,0,0);
            busStopLayer=Rect(busMarkerLayer,"Остановки автобусов");
            Stretch(busStopLayer,0,0,0,0);
            busVehicleLayer=Rect(busMarkerLayer,"Автобусы в рейсе");
            Stretch(busVehicleLayer,0,0,0,0);
            busMarkerLayer.gameObject.SetActive(false);
            busPane.gameObject.SetActive(false);
        }

        private static ScrollRect CreateBusList(RectTransform parent,string label)
        {
            RectTransform list=Panel(parent,label,Shell,true);
            list.gameObject.AddComponent<RectMask2D>();
            var scroll=list.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal=false;
            scroll.movementType=ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity=25;
            scroll.viewport=list;
            RectTransform content=Rect(list,"Строки");
            content.anchorMin=new Vector2(0,1);
            content.anchorMax=new Vector2(1,1);
            content.pivot=new Vector2(.5f,1);
            content.sizeDelta=Vector2.zero;
            scroll.content=content;
            return scroll;
        }

        private void SetBusMode(bool show)
        {
            if (show==busMode)
                return;
            if (show)
            {
                placeQuery=search.text;
                lastPlaceView=viewport.View;
            }
            else
            {
                busQuery=search.text;
                lastBusView=viewport.View;
            }
            busMode=show;
            placesTab.GetComponent<Image>().color=show?Line:Active;
            busesTab.GetComponent<Image>().color=show?Active:Line;
            placePane.gameObject.SetActive(!show);
            placeMarkers.gameObject.SetActive(!show);
            busPane.gameObject.SetActive(show);
            busMarkerLayer.gameObject.SetActive(show);
            search.SetTextWithoutNotify(show?busQuery:placeQuery);
            UpdateBusSearchHint();
            if (show && selectedBusStop==null && game.Buses!=null)
            {
                OmskBusStop nearest=NearestBusStop(game.Buses.Stops,game.Player.Position,out float distance);
                if (distance<1200f*1200f)
                    selectedBusStop=nearest;
            }
            viewport.SetBusRoute(show,busPath);
            Rect saved=show?lastBusView:lastPlaceView;
            if (saved.width>0f)
                viewport.Center(new Vector3(saved.center.x,0f,saved.center.y),saved.width);
            else
                viewport.Center(game.Player.Position,3200f);
            RefreshResults();
            UpdateRouteHint();
            UpdateMapMarkers();
            if (show)
                UpdateBusArrivals();
        }

        private void SetBusList(bool stops)
        {
            if (browseStops==stops)
                return;
            browseStops=stops;
            busRouteTab.GetComponent<Image>().color=stops?Line:Active;
            busStopTab.GetComponent<Image>().color=stops?Active:Line;
            UpdateBusSearchHint();
            RefreshBusResults();
        }

        private void UpdateBusSearchHint()
        {
            searchHint.text=!busMode?"Найти место или адрес":
                browseStops?"Название остановки":"Номер или направление автобуса";
        }

        private void RefreshBusResults()
        {
            foreach (GameObject old in busResultRows)
            {
                old.SetActive(false);
                UnityEngine.Object.Destroy(old);
            }
            busResultRows.Clear();
            matchingBusRoutes.Clear();
            matchingBusStops.Clear();
            OmskBusService service=game.Buses;
            string query=BusSearchKey(search.text.Trim().TrimStart('№').TrimStart());
            if (service!=null && browseStops)
            {
                Vector3 from=game.Player.Position;
                IReadOnlyList<OmskBusStop> stops=selectedBusRoute?.Stops ?? service.Stops;
                var seen=new HashSet<string>();
                foreach (OmskBusStop stop in stops)
                    if (BusMatches(stop.Name,query) && seen.Add(stop.Id))
                        matchingBusStops.Add(stop);
                matchingBusStops.Sort((a,b)=>(a.Position-from).sqrMagnitude.CompareTo((b.Position-from).sqrMagnitude));
                int count=Mathf.Min(80,matchingBusStops.Count);
                busListTitle.text=count<matchingBusStops.Count
                    ? "Остановки · "+count+" из "+matchingBusStops.Count
                    : "Остановки · "+count;
                for (int i=0;i<count;i++)
                {
                    OmskBusStop stop=matchingBusStops[i];
                    AddBusResult(i,stop.Name,Distance(Vector3.Distance(from,stop.Position)),
                        ()=>SelectBusStop(stop,true),stop==selectedBusStop);
                }
            }
            else if (service!=null)
            {
                foreach (OmskBusRoute line in service.Routes)
                    if (BusMatches(line.Number,query) || BusMatches(line.Name,query))
                        matchingBusRoutes.Add(line);
                busListTitle.text="Направления · "+matchingBusRoutes.Count;
                for (int i=0;i<matchingBusRoutes.Count;i++)
                {
                    OmskBusRoute line=matchingBusRoutes[i];
                    AddBusResult(i,"Автобус № "+line.Number,line.Name,
                        ()=>SelectBusRoute(line),line==selectedBusRoute);
                }
            }
            else
                busListTitle.text=browseStops?"Остановки":"Маршруты";

            busResults.content.sizeDelta=new Vector2(0,Mathf.Max(busResults.viewport.rect.height,busResultRows.Count*52+4));
            busResults.verticalNormalizedPosition=1f;
            busEmpty.text=busResultRows.Count>0?string.Empty:service==null || service.Routes.Count==0
                ? "Данные маршрутов недоступны." : "Ничего не найдено.";
        }

        private void AddBusResult(int index,string title,string hint,Action onClick,bool selected)
        {
            RectTransform row=Button(busResults.content,title,onClick,out Text caption,selected);
            BusListRow(row,index,52);
            caption.fontSize=14;
            caption.alignment=TextAnchor.MiddleLeft;
            Stretch(caption.rectTransform,9,22,7,3);
            Text meta=Label(row,hint,12,Muted);
            Stretch(meta.rectTransform,9,3,7,25);
            busResultRows.Add(row.gameObject);
        }

        private static void BusListRow(RectTransform row,int index,float height)
        {
            row.anchorMin=new Vector2(0,1);
            row.anchorMax=new Vector2(1,1);
            row.pivot=new Vector2(.5f,1);
            row.offsetMin=new Vector2(4,-(index+1)*height+2);
            row.offsetMax=new Vector2(-4,-index*height-2);
        }

        private void SelectFirstBusResult()
        {
            if (browseStops && matchingBusStops.Count>0)
                SelectBusStop(matchingBusStops[0],true);
            else if (!browseStops && matchingBusRoutes.Count>0)
                SelectBusRoute(matchingBusRoutes[0]);
        }

        private void SelectBusRoute(OmskBusRoute line)
        {
            selectedBus=null;
            selectedBusStop=NearestBusStop(line.Stops,game.Player.Position,out _,true);
            SetBusPath(line);
            if (busPath.Length>1)
            {
                Rect bounds=new Rect(busPath[0],Vector2.zero);
                foreach (Vector2 p in busPath)
                    bounds=UnityEngine.Rect.MinMaxRect(Mathf.Min(bounds.xMin,p.x),Mathf.Min(bounds.yMin,p.y),
                        Mathf.Max(bounds.xMax,p.x),Mathf.Max(bounds.yMax,p.y));
                float aspect=viewport.View.width/Mathf.Max(1f,viewport.View.height);
                viewport.Center(new Vector3(bounds.center.x,0f,bounds.center.y),
                    Mathf.Max(750f,Mathf.Max(bounds.width,bounds.height*aspect)*1.18f));
            }
            RefreshBusSelection(true);
        }

        private void SelectBusStop(OmskBusStop stop,bool center)
        {
            selectedBus=null;
            selectedBusStop=stop;
            if (center)
                viewport.Center(stop.Position,Mathf.Min(1900f,viewport.View.width));
            RefreshBusSelection(false);
        }

        private void SelectBusVehicle(OmskLiveBus vehicle,bool chooseNextStop)
        {
            OmskBusService service=game.Buses;
            if (vehicle==null || service==null)
                return;
            OmskBusRoute line=null;
            foreach (OmskBusRoute routeOption in service.Routes)
                if (routeOption.Id==vehicle.RouteId)
                {
                    line=routeOption;
                    break;
                }
            selectedBus=vehicle;
            if (chooseNextStop && service.TryGetStop(vehicle.NextStopId,out OmskBusStop nextStop))
                selectedBusStop=nextStop;
            SetBusPath(line);
            if (vehicle.Active)
                viewport.Center(vehicle.Position,Mathf.Min(3000f,viewport.View.width));
            RefreshBusSelection(true);
        }

        private void SelectBusStopNear(Vector3 position)
        {
            if (game.Buses==null || selectedBusRoute==null && viewport.View.width>BusStopZoom)
                return;
            OmskBusStop hit=NearestBusStop(selectedBusRoute?.Stops ?? game.Buses.Stops,position,out float distance);
            float radius=viewport.View.width*20f/Mathf.Max(1f,viewport.MarkerLayer.rect.width);
            if (hit!=null && distance<radius*radius)
                SelectBusStop(hit,false);
        }

        private void ClearBusRoute()
        {
            selectedBus=null;
            SetBusPath(null);
            RefreshBusSelection(true);
        }

        private void LocateBusSelection()
        {
            if (selectedBus!=null && selectedBus.Active)
                viewport.Center(selectedBus.Position,Mathf.Min(3000f,viewport.View.width));
            else if (selectedBusStop!=null)
                viewport.Center(selectedBusStop.Position,Mathf.Min(1900f,viewport.View.width));
        }

        private void SetBusPath(OmskBusRoute line)
        {
            if (line==selectedBusRoute)
                return;
            selectedBusRoute=line;
            if (line==null)
                busPath=Array.Empty<Vector2>();
            else
            {
                busPath=new Vector2[line.Path.Count];
                for (int i=0;i<busPath.Length;i++)
                    busPath[i]=new Vector2(line.Path[i].x,line.Path[i].z);
            }
            viewport.SetBusRoute(true,busPath);
        }

        private void RefreshBusSelection(bool refreshList)
        {
            if (refreshList)
                RefreshBusResults();
            else if (browseStops)
                for (int i=0;i<busResultRows.Count;i++)
                    busResultRows[i].GetComponent<Image>().color=selectedBusStop==matchingBusStops[i]?Active:Line;
            busArrivals.verticalNormalizedPosition=1f;
            UpdateBusArrivals();
            UpdateMapMarkers();
        }

        private void UpdateBuses()
        {
            UpdateBusVehicleMarkers();
            UpdateBusArrivals();
        }

        private void UpdateBusArrivals()
        {
            OmskBusService service=game.Buses;
            busDetailTitle.text=selectedBusStop?.Name ?? "Выберите остановку";
            busDetailHint.text=selectedBusRoute!=null?"№ "+selectedBusRoute.Number+" · "+selectedBusRoute.Name:
                selectedBusStop!=null?"Ближайшие рейсы":"Время прибытия и положение автобусов";
            allBusRoutes.gameObject.SetActive(selectedBusRoute!=null);
            locateBus.gameObject.SetActive(selectedBusStop!=null || selectedBus!=null);
            locateBusText.text=selectedBus!=null && selectedBus.Active?"К автобусу":"К остановке";
            stopArrivals.Clear();
            if (service!=null && selectedBusStop!=null)
                service.GetArrivals(selectedBusStop.Id,stopArrivals);

            arrivalKeys.Clear();
            int count=0;
            foreach (OmskBusArrival arrival in stopArrivals)
            {
                OmskLiveBus bus=arrival.Bus;
                if (selectedBusRoute!=null && bus.RouteId!=selectedBusRoute.Id)
                    continue;
                if (!arrivalKeys.Add(selectedBusRoute==null?bus.RouteId:bus.Id))
                    continue;
                ArrivalRow row=count<arrivalRows.Count?arrivalRows[count]:CreateArrivalRow(count);
                string time=arrival.Seconds<=.5f?"на остановке":arrival.Seconds<60f
                    ? Mathf.CeilToInt(arrival.Seconds)+" с":Mathf.CeilToInt(arrival.Seconds/60f)+" мин";
                row.Vehicle=bus;
                row.Text.text="№ "+bus.RouteNumber+" · "+time+"  /  "+
                    (arrival.IsTerminal?"конечная, высадка":bus.Destination);
                row.Rect.GetComponent<Button>().interactable=bus.Active;
                row.Rect.gameObject.SetActive(true);
                count++;
            }
            for (int i=count;i<shownArrivals;i++)
                arrivalRows[i].Rect.gameObject.SetActive(false);
            if (count!=shownArrivals)
                busArrivals.content.sizeDelta=new Vector2(0,Mathf.Max(busArrivals.viewport.rect.height,count*32+2));
            shownArrivals=count;
            busArrivalEmpty.text=count>0?string.Empty:service==null || service.Routes.Count==0
                ? "Расписание недоступно." : selectedBusStop==null
                    ? "Нажмите на остановку на карте или в списке."
                    : "Нет ближайших отправлений. Выберите другую остановку.";
        }

        private ArrivalRow CreateArrivalRow(int index)
        {
            var row=new ArrivalRow();
            row.Rect=Button(busArrivals.content,"",()=>SelectBusVehicle(row.Vehicle,false),out row.Text);
            BusListRow(row.Rect,index,32);
            row.Text.fontSize=13;
            row.Text.alignment=TextAnchor.MiddleLeft;
            row.Text.horizontalOverflow=HorizontalWrapMode.Overflow;
            Stretch(row.Text.rectTransform,5,1,3,1);
            arrivalRows.Add(row);
            return row;
        }

        private void UpdateBusMarkers()
        {
            OmskBusService service=game.Buses;
            int count=0;
            if (service!=null)
            {
                IReadOnlyList<OmskBusStop> stops=selectedBusRoute?.Stops ?? service.Stops;
                if (selectedBusRoute!=null || viewport.View.width<=BusStopZoom)
                    foreach (OmskBusStop stop in stops)
                        if (stop!=selectedBusStop && viewport.Project(stop.Position,out Vector2 position))
                            SetStopMarker(count++,stop,position);
                if (selectedBusStop!=null && viewport.Project(selectedBusStop.Position,out Vector2 selectedPosition))
                    SetStopMarker(count++,selectedBusStop,selectedPosition);
            }
            for (int i=count;i<busStopPins.Count;i++)
                busStopPins[i].Rect.gameObject.SetActive(false);
            UpdateBusVehicleMarkers();
        }

        private void SetStopMarker(int index,OmskBusStop stop,Vector2 position)
        {
            if (index==busStopPins.Count)
            {
                var marker=new BusMapMarker();
                marker.Rect=Button(busStopLayer,"",()=>SelectBusStop(marker.Stop,false),out marker.Label);
                marker.Rect.pivot=new Vector2(.5f,.5f);
                busStopPins.Add(marker);
            }
            BusMapMarker pin=busStopPins[index];
            pin.Stop=stop;
            bool selected=stop==selectedBusStop;
            pin.Label.text=selected?stop.Name:"Т";
            pin.Label.fontSize=selected?13:11;
            pin.Rect.sizeDelta=new Vector2(selected?Mathf.Clamp(stop.Name.Length*7f+14f,50f,175f):23f,25f);
            pin.Rect.GetComponent<Image>().color=selected?Active:BusStopPin;
            MoveBusMarker(pin,position);
        }

        private void UpdateBusVehicleMarkers()
        {
            OmskBusService service=game.Buses;
            bool overview=selectedBusRoute==null && viewport.View.width>2200f;
            int columns=Mathf.Max(1,Mathf.FloorToInt(busVehicleLayer.rect.width/38f));
            int rows=Mathf.Max(1,Mathf.FloorToInt(busVehicleLayer.rect.height/30f));
            occupiedBusCells.Clear();
            foreach (BusMapMarker pin in busVehiclePins.Values)
                pin.Visible=false;
            if (service!=null)
                foreach (OmskLiveBus bus in service.Buses)
                {
                    if (!bus.Active || selectedBusRoute!=null && bus.RouteId!=selectedBusRoute.Id ||
                        !viewport.Project(bus.Position,out Vector2 location))
                        continue;
                    if (overview && bus!=selectedBus)
                    {
                        int column=Mathf.Min(columns-1,(int)(location.x*columns));
                        int row=Mathf.Min(rows-1,(int)(location.y*rows));
                        if (!occupiedBusCells.Add(row*columns+column))
                            continue;
                    }
                    if (!busVehiclePins.TryGetValue(bus.Id,out BusMapMarker pin))
                    {
                        pin=new BusMapMarker();
                        BusMapMarker captured=pin;
                        pin.Rect=Button(busVehicleLayer,"",()=>SelectBusVehicle(captured.Vehicle,true),out pin.Label);
                        pin.Rect.pivot=new Vector2(.5f,.5f);
                        pin.Label.fontSize=12;
                        busVehiclePins.Add(bus.Id,pin);
                    }
                    pin.Vehicle=bus;
                    pin.Visible=true;
                    pin.Label.text=bus.RouteNumber;
                    pin.Rect.sizeDelta=new Vector2(Mathf.Max(28,11+bus.RouteNumber.Length*8),24);
                    pin.Rect.GetComponent<Image>().color=bus==selectedBus?Active:BusPin;
                    MoveBusMarker(pin,location);
                    if (bus==selectedBus)
                        pin.Rect.SetAsLastSibling();
                }
            foreach (BusMapMarker pin in busVehiclePins.Values)
                if (!pin.Visible && pin.Rect.gameObject.activeSelf)
                    pin.Rect.gameObject.SetActive(false);
        }

        private static void MoveBusMarker(BusMapMarker pin,Vector2 position)
        {
            pin.Rect.anchorMin=pin.Rect.anchorMax=position;
            pin.Rect.anchoredPosition=Vector2.zero;
            if (!pin.Rect.gameObject.activeSelf)
                pin.Rect.gameObject.SetActive(true);
        }

        private static OmskBusStop NearestBusStop(IReadOnlyList<OmskBusStop> stops,Vector3 point,
            out float distance,bool departuresOnly=false)
        {
            OmskBusStop nearest=null;
            distance=float.PositiveInfinity;
            int count=departuresOnly?Mathf.Max(0,stops.Count-1):stops.Count;
            for (int i=0;i<count;i++)
            {
                OmskBusStop stop=stops[i];
                Vector3 delta=stop.Position-point;
                float squared=delta.x*delta.x+delta.z*delta.z;
                if (squared>=distance)
                    continue;
                nearest=stop;
                distance=squared;
            }
            return nearest;
        }

        private static string BusSearchKey(string input) => input.ToLowerInvariant().Replace('ё','е');

        private static bool BusMatches(string value,string query) =>
            query.Length==0 || !string.IsNullOrWhiteSpace(value) && BusSearchKey(value).Contains(query);
    }
}
