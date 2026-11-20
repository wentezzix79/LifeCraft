#!/usr/bin/env python3
"""Build the Unity and server bus catalog from a regional OpenStreetMap PBF.

Requires Python 3.10+ and pyosmium. Routing uses ordered relation road members.
Disconnections are joined along the actual one-way-aware road graph when
possible. Unusable shapes are skipped to avoid jumps between city streets.
"""

import argparse
import json
import re
from collections import Counter, defaultdict
from heapq import heappop, heappush
from itertools import pairwise
from math import hypot
from pathlib import Path

import osmium


def point(lon, lat):
    return (round((lon - 73.3935) * 63987.4, 2), round((lat - 54.9726) * 111321.2, 2))


def source_bounds(x, z):
    return -15580 <= x <= 15780 and -17000 <= z <= 21970


def dist(a, b):
    return hypot(a[0] - b[0], a[1] - b[1])


class Relations(osmium.SimpleHandler):
    def __init__(self):
        super().__init__()
        self.items = []

    def relation(self, row):
        if (
            row.tags.get("type") != "route"
            or row.tags.get("route") not in ("bus", "share_taxi")
            or row.tags.get("network") not in (None, "Омский автобус")
        ):
            return
        if not row.tags.get("ref"):
            return
        self.items.append(
            (
                row.id,
                row.tags["ref"],
                row.tags.get("name"),
                [(m.type, m.ref, m.role) for m in row.members],
            )
        )


class Roads(osmium.SimpleHandler):
    def __init__(self, routes):
        super().__init__()
        self.wanted = {
            n for _, _, _, members in routes for t, n, _ in members if t == "w"
        }
        self.wanted_nodes = {
            n for _, _, _, members in routes for t, n, _ in members if t == "n"
        }
        self.ways = {}
        self.nodes = {}
        self.stops = {}
        self.graph = defaultdict(dict)

    def node(self, row):
        if row.id in self.wanted_nodes:
            p = point(row.location.lon, row.location.lat)
            self.nodes[row.id] = p
            self.stops[("n", row.id)] = (
                row.tags.get("name:ru")
                or row.tags.get("name")
                or row.tags.get("local_ref"),
                p,
            )

    def way(self, row):
        is_road = row.tags.get("highway") in (
            "motorway",
            "motorway_link",
            "trunk",
            "trunk_link",
            "primary",
            "primary_link",
            "secondary",
            "secondary_link",
            "tertiary",
            "tertiary_link",
            "residential",
            "living_street",
            "unclassified",
            "service",
            "busway",
        )
        if not is_road and row.id not in self.wanted:
            return
        refs = []
        coords = []
        for n in row.nodes:
            if not n.location.valid():
                return
            refs.append(n.ref)
            coords.append(point(n.lon, n.lat))
        if not refs or not any(
            -17800 < x < 17700 and -19100 < z < 24000 for x, z in coords
        ):
            return
        self.nodes.update(zip(refs, coords))
        if row.id in self.wanted:
            self.ways[row.id] = (refs, is_road)
            name = row.tags.get("name:ru") or row.tags.get("name")
            if name:
                self.stops[("w", row.id)] = (
                    name,
                    (
                        sum(x for x, _ in coords) / len(coords),
                        sum(z for _, z in coords) / len(coords),
                    ),
                )
        if not is_road:
            return
        direction = row.tags.get("oneway") or (
            "yes" if row.tags.get("junction") == "roundabout" else ""
        )
        for (a, b), (p, q) in zip(pairwise(refs), pairwise(coords)):
            length = dist(p, q)
            if direction != "-1":
                self.graph[a][b] = length
            if direction not in ("yes", "true", "1"):
                self.graph[b][a] = length


def connect(a, b, net):
    if a == b:
        return [a]
    if net.nodes[a] == net.nodes[b]:
        return [a, b]
    line = dist(net.nodes[a], net.nodes[b])
    if line > 800:
        return None
    budget = min(1250.0, max(110.0, line * 3.2 + 35))
    queue = [(0.0, 0.0, a)]
    costs = {a: 0.0}
    prior = {}
    while queue:
        _, cost, k = heappop(queue)
        if cost > costs[k]:
            continue
        if k == b:
            path = [k]
            while k != a:
                k = prior[k]
                path.append(k)
            return list(reversed(path))
        for other, length in net.graph.get(k, {}).items():
            elapsed = cost + length
            score = elapsed + dist(net.nodes[other], net.nodes[b])
            if score > budget or elapsed >= costs.get(other, float("inf")):
                continue
            costs[other] = elapsed
            prior[other] = k
            heappush(queue, (score, elapsed, other))
    return None


def ordered_ways(segments, start, finish, nodes):
    costs = [dist(nodes[segments[0][0]], start), dist(nodes[segments[0][-1]], start)]
    backs = []
    for prev, way in pairwise(segments):
        cost = []
        parents = []
        for entry in (way[0], way[-1]):
            s = [
                costs[0] + dist(nodes[prev[-1]], nodes[entry]),
                costs[1] + dist(nodes[prev[0]], nodes[entry]),
            ]
            parent = 0 if s[0] <= s[1] else 1
            cost.append(s[parent])
            parents.append(parent)
        costs = cost
        backs.append(parents)
    which = (
        0
        if costs[0] + dist(nodes[segments[-1][-1]], finish)
        <= costs[1] + dist(nodes[segments[-1][0]], finish)
        else 1
    )
    order = [which]
    for choices in reversed(backs):
        which = choices[which]
        order.append(which)
    return [list(reversed(w)) if d else w for w, d in zip(segments, reversed(order))]


def projection(s, coordinates):
    out = []
    for i, (a, b) in enumerate(pairwise(coordinates)):
        vx, vz = b[0] - a[0], b[1] - a[1]
        if vx == 0 and vz == 0:
            continue
        t = max(
            0, min(1, ((s[0] - a[0]) * vx + (s[1] - a[1]) * vz) / (vx * vx + vz * vz))
        )
        f = (round(a[0] + t * vx, 2), round(a[1] + t * vz, 2))
        d = dist(s, f)
        if d < 160:
            out.append((i + t, d, f))
    return out


def line(row, net):
    id, number, name, members = row
    calls = []
    for mode, n, role in members:
        if role.startswith("platform") or role.startswith("stop"):
            if (mode, n) in net.stops:
                label, p = net.stops[(mode, n)]
                if (
                    label
                    and source_bounds(*p)
                    and (
                        not calls or calls[-1][1] != label or dist(calls[-1][2], p) > 35
                    )
                ):
                    calls.append((mode + str(n), label, p))
    segments = [
        net.ways[n][0]
        for t, n, r in members
        if t == "w"
        and n in net.ways
        and net.ways[n][1]
        and not r.startswith(("platform", "stop"))
    ]
    if len(calls) < 2 or not segments:
        return None, "no data"
    ways = ordered_ways(segments, calls[0][2], calls[-1][2], net.nodes)
    route = ways[0].copy()
    for way in ways[1:]:
        if route[-1] == way[0]:
            route.extend(way[1:])
            continue
        join = connect(route[-1], way[0], net)
        if join is None:
            return None, "unjoined road"
        route.extend(join[1:])
        route.extend(way[1:])
    coords = [net.nodes[k] for k in route]
    matched = []
    for entry in calls:
        samples = [
            p
            for p in projection(entry[2], coords)
            if not matched or p[0] >= matched[-1][0]
        ]
        # Prefer the closest road segment, choosing the earliest recurring segment on a true tie.
        if samples:
            pos, d, q = min(samples, key=lambda p: (p[1], p[0]))
            matched.append((pos, d, q, entry))
    if len(matched) < 2 or len(matched) < len(calls) * 0.8:
        return None, "stop order"
    track = []
    schedule = []
    start = matched[0][0]
    for pos, d, p, (sid, label, at) in matched:
        for k in range(int(start) + 1, int(pos) + 1):
            if not track or coords[k] != track[-1]:
                track.append(coords[k])
        if not track or p != track[-1]:
            track.append(p)
        schedule.append((sid, label, at, len(track) - 1))
        start = pos
    if len(track) < 2 or any(not source_bounds(*p) for p in track):
        return None, "outside map"
    return {
        "id": f"osm-{id}",
        "number": number,
        "name": f"{schedule[0][1]} → {schedule[-1][1]}",
        "waypoints": [dict(x=x, z=z) for x, z in track],
        "stops": [dict(id=sid, pathIndex=i) for sid, _, _, i in schedule],
    }, schedule


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("pbf", type=Path)
    p.add_argument("out", type=Path)
    args = p.parse_args()
    with osmium.io.Reader(str(args.pbf)) as source:
        snapshot = source.header().get("osmosis_replication_timestamp")
    r = Relations()
    r.apply_file(str(args.pbf))
    print("relations", len(r.items), flush=True)
    n = Roads(r.items)
    n.apply_file(str(args.pbf), locations=True, idx="flex_mem")
    print("network", len(n.ways), len(n.graph), flush=True)
    stops = {}
    routes = []
    errors = Counter()
    for row in r.items:
        result, sched = line(row, n)
        if result is None:
            errors[sched] += 1
            continue
        routes.append(result)
        for sid, name, (x, z), i in sched:
            stops[sid] = dict(id=sid, name=name, x=x, z=z)

    def order(r):
        match = re.match(r"\d+", r["number"])
        return (int(match[0]) if match else 9999, r["number"], r["name"], r["id"])

    routes.sort(key=order)
    Path(args.out).write_text(
        json.dumps(
            dict(
                attribution="© OpenStreetMap contributors",
                license="ODbL 1.0",
                snapshot=snapshot,
                stops=list(stops.values()),
                routes=routes,
            ),
            ensure_ascii=False,
            separators=(",", ":"),
        )
        + "\n",
        encoding="utf-8",
    )
    print(
        len(routes),
        "directions",
        len({r["number"] for r in routes}),
        "numbers",
        len(stops),
        "stops",
        errors,
        flush=True,
    )


if __name__ == "__main__":
    main()
