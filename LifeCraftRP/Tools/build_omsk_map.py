#!/usr/bin/env python3
"""Build the game map from the project's OSM extract (requires Pillow)."""
import argparse
from collections import defaultdict
import gzip
import json
from pathlib import Path

from PIL import Image, ImageDraw


# These are the same longitude / latitude extents as OmskWorld.WorldBounds.
CITY_BOUNDS = (73.15, 54.82, 73.64, 55.17)
CITY_SIZE = (2048, 2560)
SUPERSAMPLE = 2
LOCAL_MAPS = {"east-map": (1700, -100), "mega-map": (-6850, -100)}
INTERFACE_REGIONS = {"starter-map": (0, 0, 1250), "east-map": (1700, -100, 1300),
                     "mega-map": (-6850, -100, 1300)}


def local_bounds(x, z, extent=1300):
    longitude, latitude = 73.3935, 54.9726
    east_meters, north_meters = 63_987.4, 111_321.2
    return (longitude + (x - extent) / east_meters,
            latitude + (z - extent) / north_meters,
            longitude + (x + extent) / east_meters,
            latitude + (z + extent) / north_meters)

LAND_COLORS = {
    "field": "#1c2b2a",
    "residential": "#232d38",
    "industrial": "#292f3b",
    "commercial": "#2e3540",
    "grass": "#233a36",
    "woods": "#25423b",
    "park": "#2e4b40",
    "wetland": "#24403d",
}
GROUND_TYPES = {
    "farmland": "field", "meadow": "field", "allotments": "field", "orchard": "field",
    "residential": "residential", "construction": "industrial", "brownfield": "industrial",
    "industrial": "industrial", "military": "industrial", "railway": "industrial",
    "retail": "commercial", "commercial": "commercial",
    "grassland": "grass", "grass": "grass", "village_green": "grass", "pitch": "grass",
    "scrub": "woods", "wood": "woods", "forest": "woods",
    "garden": "park", "park": "park", "recreation_ground": "park",
    "nature_reserve": "park", "cemetery": "park",
    "wetland": "wetland",
}
# Ordered from minor paths to the main network.
ROAD_STYLES = {
    "path": ("#354752", 1),
    "service": ("#485764", 1),
    "street": ("#566574", 2),
    "collector": ("#71838f", 3),
    "arterial": ("#97a2a5", 4),
    "expressway": ("#b3ac93", 5),
}
ROAD_CLASSES = {
    "footway": "path", "cycleway": "path", "path": "path", "steps": "path",
    "track": "service", "service": "service", "living_street": "service",
    "residential": "street", "unclassified": "street", "pedestrian": "street",
    "tertiary": "collector", "secondary": "arterial", "primary": "arterial",
    "trunk": "expressway", "motorway": "expressway",
}
BUILDING_COLORS = {"building": "#404c58", "housing": "#48525e", "civic": "#50616d"}

INTERFACE_LAND = {
    "field": "#dedeb3",
    "residential": "#e7e0d7",
    "industrial": "#d6cdda",
    "commercial": "#dfd4e1",
    "grass": "#c1deaf",
    "woods": "#a5c994",
    "park": "#9ac9a4",
    "wetland": "#b0d1be",
}
INTERFACE_BUILDINGS = {"building": "#b9b0a3", "housing": "#a99d94", "civic": "#9d8fa5"}
# Stroke colours, widths, and optional casings in draw order.
INTERFACE_ROADS = {
    "path": ("#b7b6a7", 1, None),
    "service": ("#f6efe3", 2, None),
    "street": ("#fffaf2", 2.7, "#c7beb0"),
    "collector": ("#fff7db", 3.4, "#cebca2"),
    "arterial": ("#ffdc8a", 4.6, "#bd9866"),
    "expressway": ("#f4b858", 5.7, "#b6834f"),
}
# RGB hues also serve as the geographic accent of each transparent district.
DISTRICT_COLORS = {
    "Советский": (37, 138, 162),
    "Кировский": (91, 94, 174),
    "Ленинский": (168, 80, 140),
    "Октябрьский": (99, 141, 86),
    "Центральный": (183, 112, 79),
}


def classify(features):
    result = defaultdict(list)
    for feature in features:
        geometry = feature.get("geometry")
        if not geometry:
            continue
        tags = feature["properties"]
        layer = tags.get("_layer")
        if layer == "land":
            category = next(
                (GROUND_TYPES[tags[key]] for key in ("leisure", "natural", "landuse")
                 if tags.get(key) in GROUND_TYPES),
                None,
            )
            if category:
                result[category].append(geometry)
        elif layer == "water":
            if tags.get("tunnel", "no") in ("no", "0", "false"):
                result[layer].append(geometry)
        elif layer == "building":
            category = "building"
            if tags.get("building") in ("apartments", "dormitory"):
                category = "housing"
            if tags.get("shop") or tags.get("amenity") or tags.get("building") in ("retail", "commercial"):
                category = "civic"
            result[category].append(geometry)
        elif layer == "road" and tags.get("tunnel", "no") in ("no", "0", "false"):
            road = tags.get("highway", "").removesuffix("_link")
            category = ROAD_CLASSES.get(road)
            if category:
                result[category].append(geometry)
        elif layer == "rail" and tags.get("tunnel", "no") in ("no", "0", "false"):
            if tags.get("railway") in ("rail", "tram", "light_rail", "narrow_gauge"):
                result[layer].append(geometry)
        elif layer == "runway":
            if tags.get("aeroway") in ("runway", "taxiway", "apron"):
                result[layer].append(geometry)
        elif layer == "boundary" and tags.get("admin_level") == "9":
            if tags.get("name", "").endswith("административный округ"):
                result[layer].append(geometry)
    return result


class MapCanvas:
    def __init__(self, bounds=CITY_BOUNDS, output_size=CITY_SIZE, stroke_scale=1,
                 mode="RGB", background="#19232e"):
        self.west, self.south, self.east, self.north = bounds
        self.output_size = output_size
        self.stroke_scale = stroke_scale
        size = tuple(n * SUPERSAMPLE for n in output_size)
        self.image = Image.new(mode, size, background)
        self.draw = ImageDraw.Draw(self.image)
        self.longitude_scale = (size[0] - 1) / (self.east - self.west)
        self.latitude_scale = (size[1] - 1) / (self.north - self.south)

    def pixels(self, path):
        return [
            (round((point[0] - self.west) * self.longitude_scale),
             round((self.north - point[1]) * self.latitude_scale))
            for point in path
        ]

    def polygon(self, rings, color):
        outline = self.pixels(rings[0])
        if len(set(outline)) < 3:
            return
        if len(rings) == 1:
            self.draw.polygon(outline, fill=color)
            return

        # Cut holes on a bounded mask so lower layers remain visible through them.
        left = max(0, min(x for x, _ in outline))
        top = max(0, min(y for _, y in outline))
        right = min(self.image.width - 1, max(x for x, _ in outline))
        bottom = min(self.image.height - 1, max(y for _, y in outline))
        if left > right or top > bottom:
            return
        mask = Image.new("1", (right - left + 1, bottom - top + 1))
        draw = ImageDraw.Draw(mask)
        draw.polygon([(x - left, y - top) for x, y in outline], fill=1)
        for hole in rings[1:]:
            draw.polygon([(x - left, y - top) for x, y in self.pixels(hole)], fill=0)
        self.image.paste(color, (left, top, right + 1, bottom + 1), mask)

    def draw_features(self, geometries, color, width=1, outline_only=False):
        for geometry in geometries:
            name = geometry["type"]
            coords = geometry["coordinates"]
            if name.startswith("Multi"):
                parts = coords
                name = name[5:]
            else:
                parts = (coords,)
            for part in parts:
                if name == "Polygon" and part:
                    if not outline_only:
                        self.polygon(part, color)
                        continue
                    for ring in part:
                        self.line(ring, color, width)
                elif name == "LineString":
                    self.line(part, color, width)

    def line(self, path, color, width):
        if len(path) >= 2:
            self.draw.line(self.pixels(path), fill=color,
                           width=max(1, round(width * self.stroke_scale)), joint="curve")

    def build(self, groups):
        for name, color in LAND_COLORS.items():
            self.draw_features(groups[name], color)

        self.draw_features(groups["water"], "#265777", 3)
        self.draw_features(groups["boundary"], "#3e5360", 1, outline_only=True)
        for name, color in BUILDING_COLORS.items():
            self.draw_features(groups[name], color)
        self.draw_features(groups["runway"], "#667887", 4)

        self.draw_features(groups["rail"], "#3d3840", 3)
        self.draw_features(groups["rail"], "#826b69", 1)
        for name, (color, width) in ROAD_STYLES.items():
            if width >= 3:
                self.draw_features(groups[name], "#34434f", width + 2)
            self.draw_features(groups[name], color, width)

        return self.image.resize(self.output_size, Image.Resampling.LANCZOS)

    def build_interface(self, groups):
        for name, color in INTERFACE_LAND.items():
            self.draw_features(groups[name], color)
        self.draw_features(groups["water"], "#61b9df", 3)
        for name, color in INTERFACE_BUILDINGS.items():
            self.draw_features(groups[name], color)
        self.draw_features(groups["runway"], "#a4a7ac", 4)
        self.draw_features(groups["rail"], "#f3eee6", 3.6)
        self.draw_features(groups["rail"], "#8d86a0", 1.55)
        for name, (color, width, casing) in INTERFACE_ROADS.items():
            if casing:
                self.draw_features(groups[name], casing, width + 1.7)
            self.draw_features(groups[name], color, width)

        return self.image.resize(self.output_size, Image.Resampling.LANCZOS)


def district_layer(features):
    regions = defaultdict(list)
    for feature in features:
        tags = feature["properties"]
        name = tags.get("name", "").split(" ", 1)[0]
        geometry = feature.get("geometry")
        if (tags.get("_layer") == "boundary" and tags.get("admin_level") == "9"
                and name in DISTRICT_COLORS and geometry
                and geometry["type"] in ("Polygon", "MultiPolygon")):
            regions[name].append(geometry)

    canvas = MapCanvas(mode="RGBA", background=(0, 0, 0, 0))
    for name, color in DISTRICT_COLORS.items():
        canvas.draw_features(regions[name], color + (43,))
    for name, color in DISTRICT_COLORS.items():
        canvas.draw_features(regions[name], color + (167,), 4.8, outline_only=True)

    return canvas.image.resize(canvas.output_size, Image.Resampling.LANCZOS)


def save_map(image, path):
    # Indexed PNG keeps antialiased cartography compact with an exact district alpha layer.
    if image.mode == "RGB":
        image = image.quantize(colors=256, dither=Image.Dither.NONE)
    image.save(path, optimize=True)
    print(f"{path} · {image.width} × {image.height}")


def main():
    project = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, nargs="?", default=project / "SourceData/omsk.geojson.gz")
    parser.add_argument("output", type=Path, nargs="?", help="Destination PNG for the city raster")
    parser.add_argument("--landmark-maps", action="store_true",
                        help="Also make the high-resolution districts around Park and Mega")
    parser.add_argument("--interface-maps", action="store_true",
                        help="Make the bright HUD atlases, three close-ups and separate district layer")
    args = parser.parse_args()
    with gzip.open(args.source, "rt", encoding="utf-8") as source:
        features = json.load(source)["features"]
    groups = classify(features)
    city_name = "ui-city-map.png" if args.interface_maps else "city-map.png"
    output = args.output or project / "Assets/Resources/Omsk" / city_name
    output.parent.mkdir(parents=True, exist_ok=True)
    print(f"{sum(map(len, groups.values())):,} OSM features")
    if args.interface_maps:
        save_map(MapCanvas(background="#ece7dc").build_interface(groups), output)
        for name, (x, z, extent) in INTERFACE_REGIONS.items():
            bounds = local_bounds(x, z, extent)
            image = MapCanvas(bounds, (1536, 1536), stroke_scale=2.25,
                              background="#ece7dc").build_interface(groups)
            save_map(image, output.with_name("ui-" + name + ".png"))
        save_map(district_layer(features), output.with_name("ui-districts-map.png"))
        return

    save_map(MapCanvas().build(groups), output)
    if args.landmark_maps:
        for name, (x, z) in LOCAL_MAPS.items():
            bounds = local_bounds(x, z)
            image = MapCanvas(bounds, (1536, 1536), stroke_scale=2.4).build(groups)
            save_map(image, output.with_name(name + ".png"))


if __name__ == "__main__":
    main()
