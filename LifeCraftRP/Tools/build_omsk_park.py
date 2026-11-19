#!/usr/bin/env python3
"""Extract the park's paths, shoreline and wooded areas from the bundled Omsk snapshot.

python Tools/build_omsk_park.py SourceData/omsk.geojson.gz Assets/Resources/Omsk/ParkLayout.json
"""
import argparse
import gzip
import json
import math
from pathlib import Path
import random

LON, LAT = 73.3935, 54.9726
EAST, NORTH = 63987.4, 111321.2
PARK = 'relation/2148145'
LAKES = ('way/714338540', 'relation/7088277')
BRIDGE = 'way/216615453'
# Mapped centrelines, with game-scale path widths where OSM supplies none.
MAIN_WAYS = {
    'way/397202228': (5.0, 0), 'way/397202229': (5.0, 0),
    'way/38534287': (6.6, 0), 'way/38534285': (5.8, 0),
    'way/160564557': (5.2, 0), 'way/714338502': (4, 0), 'way/714338503': (4.0, 0),
    'way/38534284': (3.6, 0), 'way/157285588': (3.6, 0),
    'way/157285595': (3.0, 0), 'way/714338539': (3.6, 1),
    'way/442636636': (2.8, 2), 'way/722734375': (3.7, 0),
}
# Physical entrances are where the mapped paths join the public street network.
ENTRANCE = (1263.4, -17.7)
GATE_BOUNDS = (1234, -41, 1347, 18)
PLAZAS = ((1313, -14, 27), (1423, 123, 14), (1978, 61, 16))
WOODS = ('relation/9926426', 'relation/9926427')


def metres(coordinate):
    lon, lat = coordinate[:2]
    return round((lon - LON) * EAST, 2), round((lat - LAT) * NORTH, 2)


def as_rings(geometry):
    coords = geometry['coordinates']
    if geometry['type'] == 'Polygon':
        coords = [coords]
    return [[list(map(metres, ring)) for ring in polygon] for polygon in coords]


def inside(point, ring):
    x, z = point
    hit = False
    for (a, b), (c, d) in zip(ring, ring[1:]):
        if (b > z) != (d > z) and x < a + (z - b) * (c - a) / (d - b):
            hit = not hit
    return hit


def in_polygon(point, rings):
    return inside(point, rings[0]) and not any(inside(point, hole) for hole in rings[1:])


def flat(points):
    return [value for p in points for value in p]


def bounds(ring):
    x, z = zip(*ring)
    return min(x), min(z), max(x), max(z)


def in_bounds(point, limits, padding=0):
    x, z = point
    left, back, right, front = limits
    return left - padding <= x <= right + padding and back - padding <= z <= front + padding


def distance_sq(point, a, b):
    dx, dz = b[0] - a[0], b[1] - a[1]
    scale = dx * dx + dz * dz
    t = ((point[0] - a[0]) * dx + (point[1] - a[1]) * dz) / scale if scale else 0
    t = min(1, max(0, t))
    return (point[0] - (a[0] + t * dx))**2 + (point[1] - (a[1] + t * dz))**2


def read_layout(source):
    with gzip.open(source, 'rt', encoding='utf8') as stream:
        features = json.load(stream)['features']
    lookup = {feature['id']: feature for feature in features}
    park = as_rings(lookup[PARK]['geometry'])
    extent = bounds([p for polygon in park for p in polygon[0]])
    water = [as_rings(lookup[key]['geometry'])[0] for key in LAKES]
    wood = [rings for key in WOODS for rings in as_rings(lookup[key]['geometry'])]
    routes, roads, structures, paths = [], [], [], []
    for feature in features:
        geometry = feature.get('geometry')
        if not geometry:
            continue
        key, tags = feature['id'], feature['properties']
        layer, kind = tags.get('_layer'), geometry['type']
        if layer == 'road' and kind == 'LineString':
            coords = geometry['coordinates']
            first, last = metres(coords[0]), metres(coords[-1])
            if not (in_bounds(first, extent, 140) or in_bounds(last, extent, 140)):
                continue
            points = []
            for position in map(metres, coords):
                if not points or position != points[-1]:
                    points.append(position)
            if len(points) < 2:
                continue
            type_tag = tags.get('highway')
            walk = type_tag in {'footway', 'path', 'cycleway', 'pedestrian', 'steps'}
            width, style = MAIN_WAYS.get(key, (2.1 if type_tag == 'cycleway' else 1.65,
                1 if tags.get('surface') == 'asphalt' or type_tag == 'path' else 0))
            if walk:
                paths.extend((a, b, width / 2 + 3.6) for a, b in zip(points, points[1:]))
            elif type_tag:
                roads.extend((a, b, 9 if type_tag in {'primary', 'secondary'} else 5)
                    for a, b in zip(points, points[1:]))
            if walk and key != BRIDGE and (key in MAIN_WAYS or all(in_bounds(p, extent) for p in points)
                    and any(in_polygon(p, poly) for p in points for poly in park)):
                routes.append({'id': key, 'xz': flat(points), 'width': width, 'style': style,
                    'lit': key in {'way/38534285', 'way/160564557', 'way/714338539', 'way/157285588', 'way/157285595'}})
        elif layer in {'building', 'land'} and kind in {'Polygon', 'MultiPolygon'}:
            coordinates = geometry['coordinates']
            first = coordinates[0][0] if kind == 'Polygon' else coordinates[0][0][0]
            if not in_bounds(metres(first), extent, 80):
                continue
            if layer == 'building':
                structures.extend((rings[0], bounds(rings[0])) for rings in as_rings(geometry))
            elif tags.get('natural') == 'wood' and key not in WOODS:
                wood.extend(rings for rings in as_rings(geometry) if any(
                    in_polygon(p, poly) for p in rings[0] for poly in park))
            elif tags.get('leisure') in ('pitch', 'playground', 'swimming_pool'):
                structures.extend((rings[0], bounds(rings[0])) for rings in as_rings(geometry))

    def clearance(point):
        if in_bounds(point, GATE_BOUNDS) or any((point[0]-x)**2+(point[1]-z)**2 < r*r for x,z,r in PLAZAS):
            return False
        if any(inside(point, rings[0]) or any(distance_sq(point, a, b) < 7**2
                for a,b in zip(rings[0],rings[0][1:])) for rings in water):
            return False
        if any(in_bounds(point, limits, 4) and (inside(point, ring) or any(
                distance_sq(point,a,b) < 4**2 for a,b in zip(ring,ring[1:]))) for ring,limits in structures):
            return False
        return not any(distance_sq(point,a,b) < radius*radius for a,b,radius in planes
            if min(a[0],b[0])-radius < point[0] < max(a[0],b[0])+radius
            and min(a[1],b[1])-radius < point[1] < max(a[1],b[1])+radius)

    trees, rng = [], random.Random(301949)
    planes = roads + paths
    woods = [(rings,bounds(rings[0])) for rings in wood]
    step = 31
    for east in range(math.floor(extent[0]), math.ceil(extent[2]), step):
        for north in range(math.floor(extent[1]), math.ceil(extent[3]), step):
            point = (round(east + rng.uniform(-9, 9), 1), round(north + rng.uniform(-9, 9), 1))
            if not any(in_polygon(point, poly) for poly in park):
                continue
            wooded = any(in_bounds(point, limit) and in_polygon(point, rings) for rings,limit in woods)
            if (not wooded and rng.random() > .30) or not clearance(point):
                continue
            species = rng.choices((0, 1, 2), (.58, .25, .17))[0]
            trees.append((*point, round(rng.uniform(7.1, 12.3), 2), species))

    return {
        'source': 'OpenStreetMap contributors · ODbL 1.0',
        'boundary': PARK, 'entrance': ENTRANCE,
        'routes': sorted(routes,key=lambda route: route['id']),
        'water': [{'id': key, 'xz': flat(rings[0])} for key,rings in zip(LAKES,water)],
        'bridge': flat(list(map(metres,lookup[BRIDGE]['geometry']['coordinates']))),
        'trees': flat(trees),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    layout = read_layout(args.source)
    args.output.write_text(json.dumps(layout, ensure_ascii=False, separators=(',', ':'))+'\n', encoding='utf8')
    print(f'{len(layout["routes"])} routes, {len(layout["trees"])//4} tree locations, both shorelines and the mapped footbridge.')


if __name__ == '__main__':
    main()
