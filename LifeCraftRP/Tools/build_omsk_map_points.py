#!/usr/bin/env python3
"""Build the playable and OpenStreetMap reference points for the Unity atlas."""

import gzip
import json
import math
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RESOURCES = ROOT / "Assets" / "Resources" / "Omsk"
ORIGIN = (73.3935, 54.9726)
EAST_METRES = 63987.4
NORTH_METRES = 111321.2

# Entrance or outdoor interaction coordinates from SiteBuilder.
GAME_POINTS = (
    ("warehouse", "Склад LC Логистика", "Работа на складе · приём сотрудников", "jobs", -27, -1),
    ("courier", "Пункт курьеров", "Работа курьером · получить задание", "jobs", 40, -10),
    ("postamat", "Постамат", "Курьерская доставка · передать посылку", "services", -80.5, -24.7),
    ("shop", "Магазин у дома", "Игровой сервис · продукты", "services", 46, -8.5),
    ("bank", "Банкомат LC Банк", "Игровой сервис · наличные и баланс", "services", -73, -25.7),
    ("dealership", "Автосалон LC Авто", "Игровой сервис · покупка машины", "services", 51, -27),
    ("fuel", "АЗС", "Игровой сервис · заправка", "services", 77, -53.8),
    ("kiosk", "Киоск", "Игровой сервис · управление бизнесом и выдача заказов", "services", 71, -2.2),
    ("apartment", "Квартира 01", "Игровой сервис · договор аренды", "services", 58, 21.6),
)

# Representative neighbourhood positions already used by OmskWorld.
NEIGHBOURHOODS = (
    ("center", "Центр", 73.371529, 54.991375),
    ("station-area", "Привокзальный", 73.387, 54.9412),
    ("rabochie", "Рабочие", 73.419435, 54.942835),
    ("chkalovsk", "Чкаловский посёлок", 73.4731016, 54.9778085),
    ("staraya-moskovka", "Старая Московка", 73.4712567, 54.9247624),
    ("novaya-moskovka", "Новая Московка", 73.4503245, 54.9123408),
    ("left-bank", "Левобережье", 73.2898522, 54.9871166),
    ("neftyaniki", "Нефтяники", 73.2792131, 55.0328394),
    ("amur", "Амурский посёлок", 73.4122585, 55.0241154),
    ("kirovsk", "Кировск", 73.334493, 54.9433196),
)

CITY_PLACES = {
    "node/3885197834": ("Ж/д вокзал Омск-Пассажирский", "Железнодорожный вокзал", "city"),
    "node/1163266929": ("Омский автовокзал", "Междугородние автобусы", "city"),
    "way/34957269": ("Аэропорт Омск-Центральный", "Терминал А · Транссибирская улица, 30", "city"),
    "way/58369390": ("Успенский собор", "Тарская улица, 7", "city"),
    "node/1668433177": ("Омская крепость", "Историческое место", "city"),
    "way/52735094": ("Музей имени Врубеля", "Улица Ленина, 3", "city"),
    "way/134110567": ("Музей Достоевского", "Улица Победы, 4", "city"),
    "node/12157517793": ("Распределительный центр Ozon", "Улица Айвазовского, 33 · ориентир OSM", "pickup"),
}


def projected(lon, lat):
    return (lon - ORIGIN[0]) * EAST_METRES, (lat - ORIGIN[1]) * NORTH_METRES


def point(id_, name, detail, category, x, z):
    return dict(id=id_, name=name, detail=detail, category=category, x=round(x, 1), z=round(z, 1))


def ring_pairs(ring):
    coords = ring[:-1] if ring and ring[0] == ring[-1] else ring
    return zip(coords, coords[1:] + coords[:1])


def area_center(ring):
    moments = [(a, b, a[0] * b[1] - b[0] * a[1]) for a, b in ring_pairs(ring)]
    twice_area = sum(edge[2] for edge in moments)
    if abs(twice_area) < 1e-8:
        return 0, tuple(sum(vertex[axis] for vertex in ring) / len(ring) for axis in range(2))
    return abs(twice_area), tuple(
        sum((a[axis] + b[axis]) * cross for a, b, cross in moments) / (3 * twice_area)
        for axis in range(2)
    )


def inside(pos, ring):
    inside_ = False
    x, z = pos
    for a, b in ring_pairs(ring):
        if (a[1] > z) != (b[1] > z) and x < a[0] + (z - a[1]) * (b[0] - a[0]) / (b[1] - a[1]):
            inside_ = not inside_
    return inside_


def district_points():
    # The administrative file already contains the polygons in world coordinates.
    data = json.loads((RESOURCES / "districts.json").read_text(encoding="utf-8"))
    for index, district in enumerate(data["districts"]):
        shells = []
        for polygon in district["polygons"]:
            rings = [list(zip(r["xz"][::2], r["xz"][1::2])) for r in polygon["rings"]]
            if rings and len(rings[0]) >= 3:
                size, centre = area_center(rings[0])
                shells.append((size, centre, rings))
        if not shells:
            continue

        _, centre, rings = max(shells, key=lambda p: p[0])
        shell, *holes = rings

        def in_district(pos):
            return inside(pos, shell) and all(not inside(pos, hole) for hole in holes)

        if in_district(centre):
            x, z = centre
        else:
            west, east = min(p[0] for p in shell), max(p[0] for p in shell)
            south, north = min(p[1] for p in shell), max(p[1] for p in shell)
            candidates = ((west + (col + .5) * (east - west) / 20,
                           south + (row + .5) * (north - south) / 20)
                          for row in range(20) for col in range(20))
            x, z = min((p for p in candidates if in_district(p)),
                       key=lambda p: math.dist(p, centre), default=shell[0])
        name = district["name"]
        yield point(f"district-{index}", name.replace(" административный", ""), name, "district", x, z)


def city_point(feature):
    id_ = feature["id"]
    name, detail, category = CITY_PLACES[id_]
    geometry = feature["geometry"]
    if geometry["type"] == "Point":
        x, z = projected(*geometry["coordinates"][:2])
    else:
        outer = geometry["coordinates"][0]
        _, (x, z) = area_center([projected(*vertex[:2]) for vertex in outer])
    return point(id_, name, detail, category, x, z)


def separate(candidates, distance):
    cells = defaultdict(list)
    for selected in candidates:
        x, z = selected["x"], selected["z"]
        i, j = math.floor(x / distance), math.floor(z / distance)
        if any((u-x)**2 + (v-z)**2 < distance**2
               for dy in range(-1, 2) for dx in range(-1, 2)
               for u, v in cells.get((i+dx, j+dy), ())):
            continue
        cells[i, j].append((x, z))
        yield selected


def mapped_points(features):
    stops, pickups = [], []
    for feature in features:
        if feature["id"] in CITY_PLACES:
            yield city_point(feature)
            continue
        geometry = feature["geometry"]
        if geometry["type"] != "Point":
            continue
        props = feature["properties"]
        name = props.get("name:ru") or props.get("name")
        if not name:
            continue
        tram = props.get("railway") == "tram_stop"
        if tram or props.get("highway") == "bus_stop":
            x, z = projected(*geometry["coordinates"][:2])
            kind = "Трамвайная остановка" if tram else "Автобусная остановка"
            stops.append(point(feature["id"], name, kind, "transit", x, z))
        elif props.get("shop") == "outpost" and name.casefold() in {"ozon", "озон"}:
            x, z = projected(*geometry["coordinates"][:2])
            address = ", ".join(filter(None, (props.get("addr:street"), props.get("addr:housenumber"))))
            detail = f"{address} · ориентир OSM" if address else "Пункт выдачи · ориентир OSM"
            pickups.append(point(feature["id"], "ПВЗ Ozon", detail, "pickup", x, z))

    # Keep a named, legible citywide stop layer and prioritise inner city points.
    stops.sort(key=lambda p: (p["x"]**2 + p["z"]**2, p["id"]))
    yield from list(separate(stops, 20))[:1600]
    pickups.sort(key=lambda p: ("Пункт выдачи" in p["detail"], p["id"]))
    yield from separate(pickups, 20)


def main():
    with gzip.open(ROOT / "SourceData" / "omsk.geojson.gz", "rt", encoding="utf-8") as stream:
        features = json.load(stream)["features"]
    points = [point(*item) for item in GAME_POINTS]
    points.extend(district_points())
    points.extend(point(id_, name, "Микрорайон Омска", "district", *projected(lon, lat))
                  for id_, name, lon, lat in NEIGHBOURHOODS)
    points.extend(mapped_points(features))
    payload = {"attribution": "© OpenStreetMap contributors · ODbL", "points": points}
    (RESOURCES / "map-points.json").write_text(json.dumps(payload, ensure_ascii=False,
                                                          separators=(",", ":")) + "\n",
                                              encoding="utf-8")


if __name__ == "__main__":
    main()
