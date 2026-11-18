#!/usr/bin/env python3
"""Extract Blender map layers from local OpenStreetMap XML or PBF files.

Example:
    /usr/bin/python3 omsk_extract.py region.osm.pbf omsk_data.geojson.gz

Requires GDAL Python bindings with the OSM driver. Coordinates are kept in
WGS 84, in longitude and latitude order, and clipped to the requested extent.
"""

import argparse
from collections import Counter
from datetime import datetime, timezone
import gzip
import json
import os
from pathlib import Path
import tempfile

from osgeo import gdal, ogr


BBOX = (73.20, 54.86, 73.60, 55.14)
ATTRIBUTION = "© OpenStreetMap contributors"
COPYRIGHT = "https://www.openstreetmap.org/copyright"

AREA_TAGS = (
    "aeroway,amenity,boundary,building,building:part,craft,geological,historic,"
    "landuse,leisure,man_made,military,natural,office,place,shop,sport,tourism,"
    "water,waterway=riverbank,highway=platform,public_transport=platform"
)
TAG_KEYS = {
    "building", "building:part", "building:levels", "building:min_level",
    "building:colour", "building:material", "height", "min_height", "ele",
    "roof:levels", "roof:height", "roof:shape", "roof:colour", "roof:material",
    "highway", "lanes", "width", "oneway", "maxspeed", "surface", "bridge",
    "tunnel", "layer", "seamark:type", "railway", "water", "waterway",
    "intermittent", "natural", "name", "name:ru", "alt_name", "official_name",
    "ref", "landuse", "leisure", "aeroway", "public_transport", "amenity",
    "tourism", "historic", "man_made", "sport", "boundary", "admin_level",
    "place", "addr:street", "addr:housenumber", "shop", "office", "source",
}
GEOMETRY_BY_DIMENSION = {
    0: ("Point", "MultiPoint"),
    1: ("LineString", "MultiLineString"),
    2: ("Polygon", "MultiPolygon"),
}
LANDFORM = {
    "wood", "scrub", "heath", "grassland", "sand", "shingle", "beach", "bare_rock",
    "wetland", "fell", "scree",
}


def osm_config(path):
    with path.open("w", encoding="utf-8") as stream:
        stream.write("[general]\nclosed_ways_are_polygons=" + AREA_TAGS
                     + "\ntags_format=json\nreport_all_tags=yes\n")
        for layer in ("points", "lines", "multipolygons", "multilinestrings", "other_relations"):
            stream.write(f"\n[{layer}]\nosm_id=yes\nall_tags=yes\n"
                         "unsignificant=created_by,converted_by,source,time,attribution\n"
                         "ignore=created_by,converted_by,time,note,todo,fixme,FIXME\n")


def classify(tags, dimension):
    if dimension == 2 and (tags.get("building") not in (None, "no")
                           or tags.get("building:part") not in (None, "no")):
        return "building"
    if tags.get("aeroway") in {"runway", "taxiway", "helipad", "apron"}:
        return "runway"
    if dimension > 0 and (
        tags.get("natural") == "water" or tags.get("water") or tags.get("waterway")
    ):
        return "water"
    if tags.get("highway") and dimension > 0:
        return "road"
    if tags.get("railway") and dimension == 1:
        return "rail"
    if tags.get("place"):
        return "place"
    if tags.get("boundary") in {"administrative", "political"} and dimension > 0:
        return "boundary"
    if dimension == 2 and (
        tags.get("landuse") or tags.get("leisure") or tags.get("natural") in LANDFORM
    ):
        return "land"
    if tags.get("name") and any(
        tags.get(key) for key in (
            "amenity", "tourism", "historic", "man_made", "aeroway", "public_transport",
            "railway", "sport", "shop", "office"
        )
    ):
        return "poi"
    return None


def geometry_parts(geometry, dimension):
    allowed = GEOMETRY_BY_DIMENSION[dimension]
    shape = geometry.get("type")
    if shape == allowed[0]:
        return [geometry["coordinates"]]
    if shape == allowed[1]:
        return geometry["coordinates"]
    if shape == "GeometryCollection":
        return [part for child in geometry.get("geometries", ())
                for part in geometry_parts(child, dimension)]
    return []


def inside_extent(geom, bbox, rectangle):
    xmin, xmax, ymin, ymax = geom.GetEnvelope()
    west, south, east, north = bbox
    if xmax < west or xmin > east or ymax < south or ymin > north:
        return None
    if west <= xmin and xmax <= east and south <= ymin and ymax <= north:
        return geom
    try:
        return geom.Intersection(rectangle)
    except RuntimeError:
        # Tiled regions sometimes contain self-crossing area outlines.
        return geom.MakeValid().Intersection(rectangle)


def osm_id(feature, source_layer):
    if source_layer == "points":
        return "node/" + str(feature.GetField("osm_id"))
    if source_layer == "multipolygons" and feature.GetField("osm_way_id") is not None:
        return "way/" + str(feature.GetField("osm_way_id"))
    kind = "way" if source_layer == "lines" else "relation"
    return kind + "/" + str(feature.GetField("osm_id"))


def records(path, config, bbox, rectangle):
    dataset = gdal.OpenEx(str(path), gdal.OF_VECTOR, allowed_drivers=["OSM"],
                          open_options=["CONFIG_FILE=" + str(config)])
    for feature, source_layer, *_ in iter(dataset.GetNextFeature, (None, None, None)):
        if feature is None:
            break
        source_name = source_layer.GetName()
        if source_name == "other_relations":
            continue
        raw_tags = feature.GetField("all_tags")
        geom = feature.GetGeometryRef()
        if not raw_tags or geom is None or geom.IsEmpty():
            continue
        tags = json.loads(raw_tags)
        dim = geom.GetDimension()
        layer = classify(tags, dim)
        if layer is None or dim not in GEOMETRY_BY_DIMENSION:
            continue
        clipped = inside_extent(geom, bbox, rectangle)
        if clipped is None or clipped.IsEmpty():
            continue
        parts = geometry_parts(json.loads(clipped.ExportToJson(["COORDINATE_PRECISION=7"])), dim)
        if not parts:
            continue
        kinds = GEOMETRY_BY_DIMENSION[dim]
        props = {key: value for key, value in tags.items() if key in TAG_KEYS}
        props["_layer"] = layer
        yield {
            "type": "Feature",
            "id": osm_id(feature, source_name),
            "properties": props,
            "geometry": {
                "type": kinds[len(parts) > 1],
                "coordinates": parts if len(parts) > 1 else parts[0],
            },
        }


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", nargs="+", type=Path, metavar="INPUT")
    parser.add_argument("output", type=Path, metavar="OUTPUT")
    parser.add_argument("--bbox", type=float, nargs=4, default=BBOX,
                        metavar=("WEST", "SOUTH", "EAST", "NORTH"),
                        help="WGS 84 bounding box, defaults to Omsk")
    args = parser.parse_args()
    west, south, east, north = args.bbox
    if not -180 <= west < east <= 180 or not -90 <= south < north <= 90:
        parser.error("--bbox requires ordered longitude and latitude bounds")
    if any(source.resolve() == args.output.resolve() for source in args.inputs):
        parser.error("output must differ from the input files")
    return args


def write_extract(args):
    west, south, east, north = args.bbox
    rectangle = ogr.CreateGeometryFromWkt(
        f"POLYGON (({west} {south},{east} {south},{east} {north},"
        f"{west} {north},{west} {south}))"
    )
    collection = {
        "type": "FeatureCollection",
        "bbox": list(args.bbox),
        "attribution": ATTRIBUTION,
        "license": "Open Database License (ODbL) 1.0",
        "copyright": COPYRIGHT,
        "sources": [source.name for source in args.inputs],
        "extracted_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
    }
    stats = Counter()
    seen = set()
    with tempfile.TemporaryDirectory(prefix="omsk-osm-") as temp:
        config = Path(temp) / "osmconf.ini"
        osm_config(config)
        opener = gzip.open if args.output.suffix == ".gz" else open
        stage_file = tempfile.NamedTemporaryFile(
            dir=args.output.parent, prefix=".omsk-extract-", delete=False
        )
        temp_output = Path(stage_file.name)
        stage_file.close()
        try:
            with opener(temp_output, "wt", encoding="utf-8") as stream:
                stream.write(json.dumps(collection, ensure_ascii=False)[:-1] + ',"features":[\n')
                first = True
                for source in args.inputs:
                    for feature in records(source, config, args.bbox, rectangle):
                        uid = feature["id"]
                        if uid in seen:
                            continue
                        seen.add(uid)
                        if not first:
                            stream.write(",\n")
                        json.dump(feature, stream, ensure_ascii=False, separators=(",", ":"))
                        first = False
                        stats[feature["properties"]["_layer"]] += 1
                stream.write('\n],"counts":' + json.dumps(dict(stats), sort_keys=True) + '}\n')
            if not stats:
                raise ValueError("No map features found. Check the source file and bounding box.")
            os.replace(temp_output, args.output)
        finally:
            temp_output.unlink(missing_ok=True)
    print(f"{sum(stats.values()):,} features → {args.output}")
    print(", ".join(f"{key}={value:,}" for key, value in sorted(stats.items())))


if __name__ == "__main__":
    gdal.UseExceptions()
    write_extract(parse_args())
