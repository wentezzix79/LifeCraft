using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeCraft.World
{
    public sealed class OmskBusStop
    {
        public string Id { get; }
        public string Name { get; }
        public Vector3 Position { get; }

        internal OmskBusStop(string id, string name, Vector3 position)
        {
            Id = id;
            Name = name;
            Position = position;
        }
    }

    public sealed class OmskBusRoute
    {
        public string Id { get; }
        public string Number { get; }
        public string Name { get; }
        public IReadOnlyList<Vector3> Path => path;
        public IReadOnlyList<OmskBusStop> Stops => stops;

        // lifecraft_server.py uses the same timetable and pose when validating passenger trips.
        private const double Speed = 10d;
        private const double MaximumRamp = 4d;
        private const double StopDwell = 12d;
        private const double TerminalDwell = 24d;
        private const double Layover = 48d;
        private const double Headway = 360d;
        private const float LaneOffset = 1.25f;
        private const float RoadHeight = .03f;

        private readonly Vector3[] path;
        private readonly OmskBusStop[] stops;
        private readonly double[] distances;
        private readonly int[] indices;
        private readonly double[] arrivals;
        private readonly double[] departures;
        private readonly Vector3[] stopHeadings;
        private readonly int clockOffset;
        internal OmskLiveBus[] Fleet { get; }
        internal double TripDuration { get; }
        internal double CycleDuration { get; }

        internal OmskBusRoute(string id, string number, string name, Vector3[] path, OmskBusStop[] stops, int[] indices)
        {
            Id = id;
            Number = number;
            Name = name;
            this.path = path;
            this.stops = stops;
            this.indices = indices;
            distances = new double[path.Length];
            for (int i = 1; i < path.Length; i++)
            {
                double dx = (double)path[i].x - path[i - 1].x;
                double dz = (double)path[i].z - path[i - 1].z;
                distances[i] = distances[i - 1] + Math.Sqrt(dx * dx + dz * dz);
            }

            arrivals = new double[stops.Length];
            departures = new double[stops.Length];
            stopHeadings = new Vector3[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                if (i > 0)
                {
                    double driveTime = (distances[indices[i]] - distances[indices[i - 1]]) / Speed;
                    arrivals[i] = departures[i - 1] + driveTime + Math.Min(MaximumRamp, driveTime);
                }
                departures[i] = arrivals[i] + (IsTerminal(i) ? TerminalDwell : StopDwell);
                stopHeadings[i] = StopHeading(indices[i]);
            }

            TripDuration = departures[departures.Length - 1];
            CycleDuration = TripDuration + Layover;
            clockOffset = StableHash(id);
            Fleet = new OmskLiveBus[Math.Max(1, (int)Math.Ceiling(CycleDuration / Headway))];
            for (int i = 0; i < Fleet.Length; i++)
                Fleet[i] = new OmskLiveBus(this, i);
        }

        internal bool IsTerminal(int call) => call == stops.Length - 1;

        internal double Phase(double unixSeconds, int slot)
        {
            double phase = (unixSeconds + clockOffset + slot * CycleDuration / Fleet.Length) % CycleDuration;
            return phase >= 0d ? phase : phase + CycleDuration;
        }

        internal float TimeToStop(int call, double phase)
        {
            double seconds = arrivals[call] - phase;
            if (seconds >= 0d)
                return (float)seconds;
            return phase < departures[call] ? 0f : (float)(seconds + CycleDuration);
        }

        internal void Sample(OmskLiveBus bus, double unixSeconds)
        {
            double time = Phase(unixSeconds, bus.Slot);
            if (time >= TripDuration)
            {
                bus.SetState(false, null, null, false, BusPosition(path[indices[stops.Length - 1]],
                    stopHeadings[stops.Length - 1]), stopHeadings[stops.Length - 1], 0f);
                return;
            }

            int call = AtOrBefore(arrivals, time);
            if (time < departures[call])
            {
                Vector3 heading = stopHeadings[call];
                string id = stops[call].Id;
                bus.SetState(true, id, id, IsTerminal(call), BusPosition(path[indices[call]], heading), heading, 0f);
                return;
            }

            int nextCall = call + 1;
            double elapsed = time - departures[call];
            double duration = arrivals[nextCall] - departures[call];
            double ramp = Math.Min(MaximumRamp, duration / 2d);
            double area = duration - ramp;
            double start = distances[indices[call]];
            double end = distances[indices[nextCall]];
            double length = end - start;
            double travel;
            double speed;
            if (elapsed <= ramp)
            {
                travel = .5d * elapsed * elapsed / ramp;
                speed = elapsed / ramp;
            }
            else if (elapsed < duration - ramp)
            {
                travel = elapsed - ramp / 2d;
                speed = 1d;
            }
            else
            {
                double remaining = duration - elapsed;
                travel = area - .5d * remaining * remaining / ramp;
                speed = remaining / ramp;
            }

            double distance = start + travel * length / area;
            Vector3 center = PointAt(distance);
            Vector3 tangent = PointAt(Math.Min(end, distance + 2d)) - PointAt(Math.Max(start, distance - 2d));
            Vector3 direction = tangent.sqrMagnitude > .000001f ? tangent.normalized : stopHeadings[call];
            bus.SetState(true, null, stops[nextCall].Id, false, BusPosition(center, direction), direction,
                (float)(speed * length / area));
        }

        private Vector3 PointAt(double distance)
        {
            int index = AtOrBefore(distances, distance);
            if (index == path.Length - 1)
                return path[index];
            double span = distances[index + 1] - distances[index];
            return Vector3.Lerp(path[index], path[index + 1], (float)((distance - distances[index]) / span));
        }

        private Vector3 StopHeading(int index)
        {
            if (index != indices[0])
            {
                for (int previous = index - 1; previous >= 0; previous--)
                {
                    Vector3 difference = path[index] - path[previous];
                    if (difference.sqrMagnitude > 0f)
                        return difference.normalized;
                }
            }
            for (int next = index + 1; next < path.Length; next++)
            {
                Vector3 difference = path[next] - path[index];
                if (difference.sqrMagnitude > 0f)
                    return difference.normalized;
            }
            return Vector3.forward;
        }

        private static Vector3 BusPosition(Vector3 center, Vector3 heading)
        {
            return new Vector3(center.x + heading.z * LaneOffset, RoadHeight, center.z - heading.x * LaneOffset);
        }

        private static int AtOrBefore(double[] sorted, double value)
        {
            int lower = 0;
            int upper = sorted.Length;
            while (lower + 1 < upper)
            {
                int middle = (lower + upper) / 2;
                if (sorted[middle] <= value)
                    lower = middle;
                else
                    upper = middle;
            }
            return lower;
        }

        private static int StableHash(string value)
        {
            int hash = 17;
            foreach (char character in value)
                hash = (hash * 31 + character) % 86400;
            return hash;
        }
    }

    public sealed class OmskLiveBus
    {
        public string Id { get; }
        public string RouteId => Route.Id;
        public string RouteNumber => Route.Number;
        public string Destination => Route.Stops[Route.Stops.Count - 1].Name;
        public Vector3 Position { get; private set; }
        public Vector3 Heading { get; private set; }
        public Vector3 Velocity { get; private set; }
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public string StopId { get; private set; }
        public string NextStopId { get; private set; }
        public bool Active { get; private set; }
        public bool DoorsOpen => Active && StopId != null;
        public bool TerminalStop { get; private set; }
        public Vector3 BoardPosition => Position + Rotation * OmskBusView.BoardingOffset;
        public Vector3 SeatPosition => GetSeatPosition(0);

        internal readonly OmskBusRoute Route;
        internal readonly int Slot;
        internal OmskBusView View;

        internal OmskLiveBus(OmskBusRoute route, int slot)
        {
            Route = route;
            Slot = slot;
            Id = route.Id + ":" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public Vector3 GetSeatPosition(int seatIndex)
        {
            IReadOnlyList<Vector3> seats = OmskBusView.SeatOffsets;
            return Position + Rotation * seats[Mathf.Clamp(seatIndex, 0, seats.Count - 1)];
        }

        internal void SetState(bool active, string stopId, string nextStopId, bool terminal, Vector3 position,
            Vector3 heading, float speed)
        {
            Active = active;
            StopId = stopId;
            NextStopId = nextStopId;
            TerminalStop = terminal;
            Position = position;
            if (Heading.x != heading.x || Heading.z != heading.z)
                Rotation = Quaternion.LookRotation(heading);
            Heading = heading;
            Velocity = heading * speed;
        }
    }

    public readonly struct OmskBusArrival
    {
        public OmskLiveBus Bus { get; }
        public float Seconds { get; }
        public bool IsTerminal { get; }

        internal OmskBusArrival(OmskLiveBus bus, float seconds, bool terminal)
        {
            Bus = bus;
            Seconds = seconds;
            IsTerminal = terminal;
        }
    }
}
