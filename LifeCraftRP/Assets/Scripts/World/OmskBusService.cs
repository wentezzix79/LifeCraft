using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeCraft.World
{
    [DefaultExecutionOrder(-130)]
    [DisallowMultipleComponent]
    public sealed class OmskBusService : MonoBehaviour
    {
        public IReadOnlyList<OmskBusStop> Stops => stops;
        public IReadOnlyList<OmskBusRoute> Routes => routes;
        public IReadOnlyList<OmskLiveBus> Buses => buses;

        [Serializable]
        private sealed class Catalog
        {
            public StopEntry[] stops;
            public RouteEntry[] routes;
        }

        [Serializable]
        private sealed class StopEntry
        {
            public string id;
            public string name;
            public float x;
            public float z;
        }

        [Serializable]
        private sealed class RouteEntry
        {
            public string id;
            public string number;
            public string name;
            public WaypointEntry[] waypoints;
            public RouteStopEntry[] stops;
        }

        [Serializable]
        private sealed class WaypointEntry
        {
            public float x;
            public float z;
        }

        [Serializable]
        private sealed class RouteStopEntry
        {
            public string id;
            public int pathIndex;
        }

        private readonly struct StopCall
        {
            public readonly OmskBusRoute Route;
            public readonly int Index;

            public StopCall(OmskBusRoute route, int index)
            {
                Route = route;
                Index = index;
            }
        }

        private readonly List<OmskBusStop> stops = new List<OmskBusStop>();
        private readonly List<OmskBusRoute> routes = new List<OmskBusRoute>();
        private readonly List<OmskLiveBus> buses = new List<OmskLiveBus>();
        private readonly Dictionary<string, OmskBusStop> stopsById = new Dictionary<string, OmskBusStop>();
        private readonly Dictionary<string, OmskLiveBus> busesById = new Dictionary<string, OmskLiveBus>();
        private readonly Dictionary<string, List<StopCall>> stopCalls = new Dictionary<string, List<StopCall>>();
        private readonly Stack<OmskBusView> pooled = new Stack<OmskBusView>();
        private readonly List<OmskLiveBus> rendered = new List<OmskLiveBus>();
        private readonly List<OmskLiveBus> nearby = new List<OmskLiveBus>();
        private readonly HashSet<OmskLiveBus> selected = new HashSet<OmskLiveBus>();
        private Transform observer;
        private Func<double> clock;
        private float refreshAt;
        private bool initialized;

        public void Initialize(Transform player, Func<double> timeSource = null)
        {
            observer = player;
            clock = timeSource ?? SystemTime;
            if (!initialized)
            {
                LoadCatalog();
                initialized = true;
            }
            UpdateFleet(clock());
        }

        public bool TryGetStop(string stopId, out OmskBusStop stop)
        {
            stop = null;
            return stopId != null && stopsById.TryGetValue(stopId, out stop);
        }

        public bool TryGetBus(string busId, out OmskLiveBus bus)
        {
            bus = null;
            return busId != null && busesById.TryGetValue(busId, out bus);
        }

        public bool TryGetBoardableBus(Vector3 position, float maxDistance, out OmskLiveBus selectedBus)
        {
            selectedBus = null;
            float nearest = maxDistance * maxDistance;
            foreach (OmskLiveBus bus in rendered)
            {
                if (!bus.DoorsOpen || bus.TerminalStop)
                    continue;
                float distance = (position - bus.BoardPosition).sqrMagnitude;
                if (distance <= nearest)
                {
                    nearest = distance;
                    selectedBus = bus;
                }
            }
            return selectedBus != null;
        }

        public void GetArrivals(string stopId, List<OmskBusArrival> output)
        {
            output.Clear();
            if (stopId == null || !stopCalls.TryGetValue(stopId, out List<StopCall> calls))
                return;
            double time = clock();
            foreach (StopCall call in calls)
            {
                foreach (OmskLiveBus bus in call.Route.Fleet)
                {
                    double phase = call.Route.Phase(time, bus.Slot);
                    output.Add(new OmskBusArrival(bus, call.Route.TimeToStop(call.Index, phase),
                        call.Route.IsTerminal(call.Index)));
                }
            }
            output.Sort(CompareArrivals);
        }

        private static int CompareArrivals(OmskBusArrival first, OmskBusArrival second)
        {
            int comparison = first.Seconds.CompareTo(second.Seconds);
            return comparison == 0 ? string.CompareOrdinal(first.Bus.Id, second.Bus.Id) : comparison;
        }

        private void Update()
        {
            if (!initialized)
                return;
            double time = clock();
            if (Time.unscaledTime >= refreshAt)
                UpdateFleet(time);
            else
            {
                for (int i = rendered.Count - 1; i >= 0; i--)
                {
                    OmskLiveBus bus = rendered[i];
                    bus.Route.Sample(bus, time);
                    if (bus.Active)
                        UpdateView(bus);
                    else
                        ReleaseView(i);
                }
            }
        }

        private void UpdateFleet(double time)
        {
            refreshAt = Time.unscaledTime + .15f;
            foreach (OmskLiveBus bus in buses)
                bus.Route.Sample(bus, time);
            if (observer == null)
                return;

            Vector3 position = observer.position;
            float distanceLimit = Application.isMobilePlatform ? 270f : 440f;
            nearby.Clear();
            foreach (OmskLiveBus bus in buses)
            {
                if (bus.Active && (bus.Position - position).sqrMagnitude <= distanceLimit * distanceLimit)
                    nearby.Add(bus);
            }
            nearby.Sort((a, b) => (a.Position - position).sqrMagnitude.CompareTo((b.Position - position).sqrMagnitude));

            int visible = Math.Min(nearby.Count, Application.isMobilePlatform ? 8 : 18);
            selected.Clear();
            for (int i = 0; i < visible; i++)
                selected.Add(nearby[i]);
            for (int i = rendered.Count - 1; i >= 0; i--)
            {
                if (!selected.Contains(rendered[i]))
                    ReleaseView(i);
            }
            for (int i = 0; i < visible; i++)
            {
                OmskLiveBus bus = nearby[i];
                if (bus.View == null)
                {
                    bus.View = pooled.Count > 0 ? pooled.Pop() :
                        OmskBusView.Create(transform, bus.RouteNumber, bus.Destination);
                    bus.View.SetRoute(bus.RouteNumber, bus.Destination);
                    UpdateView(bus);
                    bus.View.gameObject.SetActive(true);
                    rendered.Add(bus);
                }
                else
                    UpdateView(bus);
            }
        }

        private static void UpdateView(OmskLiveBus bus)
        {
            bus.View.transform.SetPositionAndRotation(bus.Position, bus.Rotation);
            bus.View.SetDoors(bus.DoorsOpen);
        }

        private void ReleaseView(int index)
        {
            OmskLiveBus bus = rendered[index];
            bus.View.gameObject.SetActive(false);
            pooled.Push(bus.View);
            bus.View = null;
            rendered.RemoveAt(index);
        }

        private void LoadCatalog()
        {
            TextAsset asset = Resources.Load<TextAsset>("Omsk/bus-routes");
            if (asset == null)
            {
                Debug.LogWarning("Каталог автобусных маршрутов Омска не найден.");
                return;
            }

            Catalog catalog;
            try
            {
                catalog = JsonUtility.FromJson<Catalog>(asset.text);
            }
            catch (ArgumentException error)
            {
                Debug.LogWarning("Ошибка чтения автобусных маршрутов Омска. " + error.Message);
                return;
            }
            finally
            {
                Resources.UnloadAsset(asset);
            }
            if (catalog?.stops == null || catalog.routes == null)
                return;

            foreach (StopEntry entry in catalog.stops)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || string.IsNullOrWhiteSpace(entry.name) ||
                    stopsById.ContainsKey(entry.id) || !ValidPoint(entry.x, entry.z))
                    continue;
                var stop = new OmskBusStop(entry.id, entry.name, new Vector3(entry.x, 0f, entry.z));
                stopsById.Add(stop.Id, stop);
                stops.Add(stop);
            }

            var ids = new HashSet<string>();
            foreach (RouteEntry entry in catalog.routes)
            {
                if (!TryBuildRoute(entry, out OmskBusRoute route) || !ids.Add(route.Id))
                    continue;
                routes.Add(route);
                for (int i = 0; i < route.Stops.Count; i++)
                {
                    string id = route.Stops[i].Id;
                    if (!stopCalls.TryGetValue(id, out List<StopCall> calls))
                    {
                        calls = new List<StopCall>();
                        stopCalls.Add(id, calls);
                    }
                    calls.Add(new StopCall(route, i));
                }
                foreach (OmskLiveBus bus in route.Fleet)
                {
                    busesById.Add(bus.Id, bus);
                    buses.Add(bus);
                }
            }
            if (routes.Count < catalog.routes.Length)
                Debug.LogWarning("Каталог автобусов Омска содержит некорректные направления. Загружено " +
                    routes.Count + " из " + catalog.routes.Length + ".");
        }

        private bool TryBuildRoute(RouteEntry entry, out OmskBusRoute route)
        {
            route = null;
            if (entry == null || string.IsNullOrWhiteSpace(entry.id) || string.IsNullOrWhiteSpace(entry.number) ||
                entry.waypoints == null || entry.waypoints.Length < 2 || entry.stops == null || entry.stops.Length < 2)
                return false;
            var path = new Vector3[entry.waypoints.Length];
            for (int i = 0; i < path.Length; i++)
            {
                WaypointEntry point = entry.waypoints[i];
                if (point == null || !ValidPoint(point.x, point.z))
                    return false;
                path[i] = new Vector3(point.x, 0f, point.z);
            }
            var appointments = new OmskBusStop[entry.stops.Length];
            var indices = new int[entry.stops.Length];
            int previous = 0;
            for (int i = 0; i < appointments.Length; i++)
            {
                RouteStopEntry call = entry.stops[i];
                if (call == null || call.pathIndex < previous || call.pathIndex >= path.Length ||
                    !TryGetStop(call.id, out appointments[i]))
                    return false;
                indices[i] = call.pathIndex;
                previous = call.pathIndex;
            }
            if (indices[0] == indices[indices.Length - 1])
                return false;
            string name = string.IsNullOrWhiteSpace(entry.name) ?
                appointments[0].Name + " — " + appointments[appointments.Length - 1].Name : entry.name;
            route = new OmskBusRoute(entry.id, entry.number, name, path, appointments, indices);
            return true;
        }

        private static bool ValidPoint(float x, float z)
        {
            return !float.IsNaN(x) && !float.IsInfinity(x) && !float.IsNaN(z) && !float.IsInfinity(z);
        }

        private static double SystemTime() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
    }
}
