#!/usr/bin/env python3
"""Bake the Omsk project's OpenStreetMap roads and footways into a routing graph."""

from collections import Counter
import gzip
import json
from pathlib import Path
import struct

from build_omsk_landmarks import ORIGIN, PROJECT, SCALE

CAR = {
    "motorway", "trunk", "primary", "secondary", "tertiary", "unclassified",
    "residential", "living_street", "service", "track", "road",
}
WALK = (CAR - {"motorway"}) | {"footway", "path", "pedestrian", "steps", "cycleway", "crossing"}
BLOCKED = {"no", "private", "agricultural", "forestry"}


def segment_modes(tags):
    source = tags.get("highway", "")
    kind = source.removesuffix("_link")
    # The current game map streams surface streets and bridges only.
    if tags.get("tunnel", "no") not in ("no", "0", "false"):
        return 0, 0
    public = tags.get("access")
    walking = kind in WALK and tags.get("foot", public) not in BLOCKED
    driving = kind in CAR and tags.get("motorcar", tags.get("motor_vehicle", tags.get("vehicle", public))) not in BLOCKED
    forward = backward = int(walking)
    if driving:
        direction = tags.get("oneway")
        if direction not in ("-1", "reverse"):
            forward |= 2
        if direction not in ("yes", "true", "1") and (
            direction is not None or tags.get("junction") != "roundabout" and kind != "motorway"
        ):
            backward |= 2
    return forward, backward


def project(coordinate):
    return tuple(round((coordinate[i]-ORIGIN[i])*SCALE[i]*10) for i in range(2))


def lines(feature):
    geometry = feature.get("geometry")
    if not geometry:
        return ()
    if geometry["type"] == "LineString":
        return (geometry["coordinates"],)
    if geometry["type"] == "MultiLineString":
        return geometry["coordinates"]
    return ()


def build(features):
    links = {}
    for feature in features:
        tags = feature["properties"]
        if tags.get("_layer") != "road":
            continue
        forward, backward = segment_modes(tags)
        if not (forward or backward):
            continue
        for line in lines(feature):
            route = [project(point) for point in line]
            # Only consecutive points and shared OSM vertices form junctions;
            # geometric crossings of unconnected roads are never linked.
            for start,end in zip(route,route[1:]):
                if start == end:
                    continue
                if start < end:
                    key, mask = (start,end), forward | backward << 2
                else:
                    key, mask = (end,start), backward | forward << 2
                links[key] = links.get(key,0) | mask
    points = sorted({p for edge in links for p in edge})
    indices = {p:i for i,p in enumerate(points)}
    return points, [(indices[a],indices[b],mask) for (a,b),mask in sorted(links.items())]


def serialize(points, links):
    payload = bytearray(struct.pack("<4sII",b"LCN2",len(points),len(links)))

    def packed(value):
        while value >= 128:
            payload.append((value & 127) | 128)
            value >>= 7
        payload.append(value)

    px = pz = 0
    for x,z in points:
        for delta in (x-px,z-pz):
            packed((delta << 1) ^ (delta >> 31))
        px,pz = x,z
    previous = 0
    for a,b,mask in links:
        packed(a-previous)
        packed(b-a)
        payload.append(mask)
        previous = a
    return payload


def main():
    source = PROJECT / "SourceData/omsk.geojson.gz"
    with gzip.open(source,"rt",encoding="utf-8") as data:
        points, links = build(json.load(data)["features"])

    destination = PROJECT / "Assets/Resources/Omsk/navigation.bytes"
    destination.write_bytes(gzip.compress(serialize(points,links),compresslevel=9,mtime=0))
    modes = Counter((bool(mask & 5), bool(mask & 10)) for _,_,mask in links)
    print(f"{len(points):,} nodes, {len(links):,} segments, {destination.stat().st_size:,} bytes, {dict(modes)}")


if __name__ == "__main__":
    main()
