#!/usr/bin/env python3
"""Extract the two landmark building footprints from the included Omsk GeoJSON."""

import gzip
import json
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1]
ORIGIN = (73.3935, 54.9726)
SCALE = (63987.4, 111321.2)


def coords(x, z):
    return (round(ORIGIN[0] + x / SCALE[0], 10),
            round(ORIGIN[1] + z / SCALE[1], 10))


def site(key, name, address, center, arrival, heading):
    return dict(id=key, name=name, address=address, lon=center[0], lat=center[1],
                arrivalLon=arrival[0], arrivalLat=arrival[1], heading=heading)


def mall(key, name, address, feature, arrival, heading):
    polygon = feature["geometry"]
    if polygon["type"] != "Polygon":
        raise ValueError(f"{name} requires a single building Polygon")
    outline = list(dict.fromkeys(tuple(p[:2]) for p in polygon["coordinates"][0]))
    center = tuple(sum(p[axis] for p in outline) / len(outline) for axis in range(2))
    model = site(key, name, address, center, coords(*arrival), heading)
    model["footprint"] = [round((p[axis] - center[axis]) * SCALE[axis], 3)
                          for p in outline for axis in range(2)]
    return model


def bounds(geometry, center):
    if geometry["type"] == "Polygon":
        parts = [geometry["coordinates"]]
    else:
        parts = geometry["coordinates"]
    points = [p for polygon in parts for p in polygon[0]]
    local = [[(point[axis] - center[axis]) * SCALE[axis] for point in points]
             for axis in range(2)]
    return [round(aggregate(values), 3) for aggregate in (min, max) for values in local]


def main():
    wanted = {"way/61367812", "way/814296045", "relation/2148145"}
    with gzip.open(PROJECT / "SourceData/omsk.geojson.gz", "rt", encoding="utf-8") as source:
        features = {feature["id"]: feature for feature in json.load(source)["features"]
                    if feature.get("id") in wanted}
    park_center = coords(1263.4, -17.7)
    park = site("culture-park", "ПКиО им. 30-летия ВЛКСМ", "ул. Масленникова, 136",
                park_center, coords(1274, -16.5), 84)
    park["bounds"] = bounds(features["relation/2148145"]["geometry"],park_center)
    data = {"sites": [
        mall("park-mall", "ТЦ «Парк»", "ул. Масленникова, 134",
             features["way/814296045"], (1296.1, 223.1), 174),
        mall("mega", "ТЦ «МЕГА»", "б-р Архитекторов, 35",
             features["way/61367812"], (-6789.6, -187.1), -49),
        park,
    ]}
    result = PROJECT / "Assets/Resources/Omsk/landmarks.json"
    result.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
