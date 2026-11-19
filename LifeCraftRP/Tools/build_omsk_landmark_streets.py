#!/usr/bin/env python3
"""Extract road geometry for the streets around the two Omsk landmark malls."""

import gzip
import json
import math

from build_omsk_landmarks import ORIGIN, PROJECT, SCALE

# OSM way IDs for the cycle track built in 2025, neighbouring paths and the
# two carriageways carrying the boulevard's trolleybus contact network.
CYCLE = {1424853128, 1424853140, 1424853141, 1424853142}
BOULEVARD_PATHS = {1424853129, 1424853137, 1424853147, 1560797635,
                   1560797638, 1560797639, 1560797640, 1560797643, 1560797647}
CARRIAGEWAYS = {309693127, 763350913, 807945643, 1309911769, 1347086825, 1424172824}
MEGA_APPROACHES = {541543946, 541543947, 1210259746}
PARK_APPROACHES = {398120230, 398120227, 160564558}

def axes(coordinate):
    return tuple(round((coordinate[i] - ORIGIN[i]) * SCALE[i], 3) for i in range(2))


def line(feature, kind):
    coordinates = feature["geometry"]["coordinates"]
    projected = list(dict.fromkeys(axes(point) for point in coordinates))
    return {"id": feature["id"], "kind": kind, "xz": [a for p in projected for a in p]}


def cross(a, b, c):
    return (b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0])


def outline(points):
    ring = list(dict.fromkeys(axes(point) for point in points))
    if sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(ring, ring[1:]+ring[:1])) < 0:
        ring.reverse()
    return ring


def triangulate(ring):
    open_ring = list(range(len(ring)))
    triangles = []
    while len(open_ring) > 3:
        for k,j in enumerate(open_ring):
            i = open_ring[k-1]
            l = open_ring[(k+1)%len(open_ring)]
            a,b,c = ring[i],ring[j],ring[l]
            if cross(a,b,c) <= .00001 or any(
                min(cross(a,b,ring[n]),cross(b,c,ring[n]),cross(c,a,ring[n])) >= -.00001
                for n in open_ring if n not in (i,j,l)
            ):
                continue
            triangles.extend((i,l,j))
            del open_ring[k]
            break
        else:
            raise ValueError("Unable to triangulate an OSM parking outline")
    triangles.extend(reversed(open_ring))
    return triangles


def inside(point, ring):
    x,z = point
    result = False
    for a,b in zip(ring,ring[1:]+ring[:1]):
        if (a[1]>z) != (b[1]>z) and x < a[0]+(z-a[1])*(b[0]-a[0])/(b[1]-a[1]):
            result = not result
    return result


def parking(feature):
    geometry = feature["geometry"]
    assert geometry["type"] == "MultiPolygon" and len(geometry["coordinates"]) == 1
    shell, = geometry["coordinates"][0]
    ring = outline(shell)
    edge = max(zip(ring,ring[1:]+ring[:1]),key=lambda e: math.dist(*e))
    a,b = edge
    length = math.dist(a,b)
    dx,dz = (b[0]-a[0])/length,(b[1]-a[1])/length
    marks = []
    # Use the footprint's longest side for a reconstructed row, not a claim
    # about the surveyed position of every painted parking bay.
    for i in range(1,max(1,int((length-3)/2.65))):
        start = (a[0]+dx*i*2.65-dz*1.85, a[1]+dz*i*2.65+dx*1.85)
        end = (start[0]-dz*4.9,start[1]+dx*4.9)
        if inside(start,ring) and inside(end,ring):
            marks.extend(round(value,3) for value in start+end)
    return {"id": feature["id"], "xz": [v for p in ring for v in p],
            "triangles": triangulate(ring), "marks": marks}


def main():
    lookup = {}
    for ids, kind in ((CYCLE, "cycle"), (BOULEVARD_PATHS, "paving"), (CARRIAGEWAYS, "contact"),
                      (MEGA_APPROACHES, "concrete"), (PARK_APPROACHES, "asphalt")):
        for number in ids:
            lookup[f"way/{number}"] = kind
    with gzip.open(PROJECT / "SourceData/omsk.geojson.gz", "rt", encoding="utf-8") as source:
        features = json.load(source)["features"]
    mega, park = [], []
    for feature in features:
        tag = feature.get("id")
        if tag not in lookup:
            continue
        target = park if int(tag.split("/")[1]) in PARK_APPROACHES else mega
        target.append(line(feature, lookup.pop(tag)))
    if lookup:
        raise ValueError(f"Missing designated OSM ways: {list(lookup)}")
    source = PROJECT / "SourceData/omsk-mega-parking.geojson"
    forecourts = [parking(feature) for feature in json.loads(source.read_text())["features"]]
    result = PROJECT / "Assets/Resources/Omsk/LandmarkStreets.json"
    result.write_text(json.dumps({"mega": mega, "parkMall": park, "parking": forecourts}, ensure_ascii=False,
                                 separators=(",", ":")) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
