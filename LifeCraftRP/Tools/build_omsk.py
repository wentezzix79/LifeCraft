#!/usr/bin/env python3
"""Convert the companion Omsk OSM GeoJSON into spatially streamed Unity meshes.

blender -b --python-exit-code 1 -P Tools/build_omsk.py -- ../omsk/omsk.geojson.gz Assets/Resources/Omsk
"""
import argparse
from array import array
from collections import Counter, defaultdict
import gzip
import io
import json
import math
from pathlib import Path
import re
import struct
import sys
import zlib

from mathutils import Vector
from mathutils.geometry import delaunay_2d_cdt, tessellate_polygon


LON, LAT = 73.3935, 54.9726
EAST, NORTH = 63_987.4, 111_321.2
TILE = 500
SITE = (-120, -100, 120, 110)
CITY = (73.15, 54.82, 73.64, 55.17)

WATER, PARK, WOOD, FIELD, LOT, ROAD, MAIN, PATH, RAIL, MARK = range(10)
WALL, PANEL, BRICK, INDUSTRIAL, ROOF, WINDOW, TRUNK, FOLIAGE = range(10, 18)

WIDTH = {
    "motorway": 14, "trunk": 12, "primary": 11, "secondary": 8.5, "tertiary": 7.2,
    "residential": 5.5, "unclassified": 5.3, "living_street": 4.4, "service": 3.4,
    "track": 2.5, "pedestrian": 3.5, "footway": 1.5, "steps": 1.4, "path": 1.3,
    "cycleway": 2.1, "construction": 2.8,
}
MAJOR = {"motorway", "trunk", "primary", "secondary", "tertiary"}
WALL_STYLES = {
    "residential": PANEL, "apartments": PANEL, "church": BRICK,
    "cathedral": BRICK, "house": BRICK, "detached": BRICK, "garage": INDUSTRIAL,
    "garages": INDUSTRIAL, "warehouse": INDUSTRIAL, "industrial": INDUSTRIAL,
}
VEG = {"wood", "forest", "grass", "grassland", "park", "garden", "recreation_ground",
       "cemetery", "scrub", "meadow", "allotments", "farmland", "orchard", "residential",
       "industrial", "commercial", "railway", "retail", "brownfield", "village_green",
       "nature_reserve", "wetland", "pitch"}


def point(coordinate):
    lon,lat = coordinate[:2]
    return round((lon-LON)*EAST,3), round((lat-LAT)*NORTH,3)


def tile(axis):
    return math.floor((axis+TILE/2)/TILE)


def area(ring):
    return sum(a[0]*b[1]-a[1]*b[0] for a,b in zip(ring,ring[1:]+ring[:1])) / 2


def inside(point, ring):
    x,z = point
    result = False
    for (a,b),(c,d) in zip(ring, ring[1:]+ring[:1]):
        if (b>z) != (d>z) and x < a + (z-b)*(c-a)/(d-b):
            result = not result
    return result


def normalize(coords):
    rings = []
    for coordinates in coords:
        ring = []
        for coord in coordinates:
            p = point(coord)
            if not ring or p != ring[-1]:
                ring.append(p)
        if len(ring) > 1 and ring[0] == ring[-1]:
            ring.pop()
        if len(ring) >= 3 and abs(area(ring)) >= .02:
            if (area(ring)>0) != (not rings):
                ring.reverse()
            rings.append(ring)
    return rings


def triangulate(rings):
    flat = [p for r in rings for p in r]
    if len(rings) == 1:
        return flat, tessellate_polygon([[Vector(p) for p in flat]])
    edges,offset = [],0
    for ring in rings:
        edges.extend((offset+i, offset+(i+1)%len(ring)) for i in range(len(ring)))
        offset += len(ring)
    pts, _, tris, _, _, _ = delaunay_2d_cdt([Vector(p) for p in flat], edges, [], 0, .003, False)
    result = []
    for tri in tris:
        mean = tuple(sum(pts[i][axis] for i in tri)/len(tri) for axis in (0,1))
        if inside(mean,rings[0]) and not any(inside(mean,hole) for hole in rings[1:]):
            result.append(tri)
    return [tuple(v) for v in pts], result


def fit(value, default):
    if not value:
        return default
    result = re.fullmatch(r"\s*(-?\d+(?:[,.]\d+)?)\s*(m|ft|'|feet)?\s*", str(value))
    if not result:
        return default
    n = float(result[1].replace(",","."))
    return n*.3048 if result[2] in ("ft","'","feet") else n


def clip_axis(verts, axis, bound, sign):
    next = []
    prev = verts[-1]
    prev_inside = (prev[axis]-bound)*sign >= 0
    for v in verts:
        within = (v[axis]-bound)*sign >= 0
        if within != prev_inside:
            t = (bound-prev[axis])/(v[axis]-prev[axis])
            next.append(tuple(round(p+t*(q-p),4) for p,q in zip(prev,v)))
        if within:
            next.append(v)
        prev,prev_inside = v,within
    return next


def clip_tile(verts, i, j):
    for axis, c in ((0,i),(2,j)):
        for bound,sign in ((c*TILE-TILE/2,1),(c*TILE+TILE/2,-1)):
            verts = clip_axis(verts,axis,bound,sign)
            if len(verts) < 3:
                return []
    return verts


class Geometry:
    def __init__(self):
        self.groups = {}

    def add(self, style, polygon):
        vertices, indices, look = self.groups.setdefault(style, (array('f'), array('I'), {}))
        ids = []
        for vertex in polygon:
            p = tuple(round(v, 3) for v in vertex)
            index = look.get(p)
            if index is None:
                index = len(vertices)//3
                look[p] = index
                vertices.extend(p)
            ids.append(index)
        for i in range(1,len(ids)-1):
            if len({ids[0],ids[i],ids[i+1]}) == 3:
                indices.extend((ids[0],ids[i],ids[i+1]))


class City:
    def __init__(self, selected_tiles=None):
        self.chunks = defaultdict(Geometry)
        self.count = Counter()
        self.cartography = []
        self.districts = []
        self.selected_tiles = selected_tiles

    def selected(self, key):
        return self.selected_tiles is None or key in self.selected_tiles

    def poly(self, style, verts, assigned=None):
        if assigned is not None:
            if self.selected(assigned):
                self.chunks[assigned].add(style,verts)
            return
        lo = tile(min(p[0] for p in verts)),tile(min(p[2] for p in verts))
        hi = tile(max(p[0] for p in verts)),tile(max(p[2] for p in verts))
        if lo == hi:
            if self.selected(lo):
                self.chunks[lo].add(style,verts)
            return
        for i in range(lo[0],hi[0]+1):
            for j in range(lo[1],hi[1]+1):
                if not self.selected((i,j)):
                    continue
                clipped = clip_tile(verts,i,j)
                if clipped:
                    self.chunks[i,j].add(style,clipped)

    def flat(self, style, pts, tris, y, chunk=None):
        for tri in tris:
            args = [pts[i] for i in tri]
            if area(args) > 0:
                args.reverse()
            self.poly(style, [(x,y,z) for x,z in args], chunk)

    def building(self, rings, tags):
        outer = rings[0]
        minx,maxx = min(p[0] for p in outer),max(p[0] for p in outer)
        minz,maxz = min(p[1] for p in outer),max(p[1] for p in outer)
        if minx <= SITE[2] and maxx >= SITE[0] and minz <= SITE[3] and maxz >= SITE[1]:
            self.count["site_replacements"] += 1
            return
        length = abs(area(outer))
        kind = tags.get("building",tags.get("building:part",""))
        levels = fit(tags.get("building:levels"),0)
        default = 16 if kind == "apartments" else (12 if kind in ("industrial","church") else 8)
        if kind in ("garage","garages","shed","roof"):
            default = 2.8
        elif kind in ("house","detached") or length < 100:
            default = 5.0
        top = fit(tags.get("height"), levels*3.1+fit(tags.get("roof:height"),0) if levels > 0 else default)
        base = max(0,fit(tags.get("min_height"),0))
        top = min(180, max(base+1,top))
        style = WALL_STYLES.get(kind,WALL)
        center = ((minx+maxx)/2,(minz+maxz)/2)
        chunk = tile(center[0]),tile(center[1])
        if not self.selected(chunk):
            return
        pts,tris = triangulate(rings)
        self.flat(ROOF,pts,tris,top,chunk)
        detail = math.hypot(*center) < 1350 and 5 <= top-base <= 46 and length > 70
        for ring in rings:
            for (x,z),(u,v) in zip(ring,ring[1:]+ring[:1]):
                self.poly(style,[(x,base,z),(x,top,z),(u,top,v),(u,base,v)],chunk)
                if detail:
                    self.windows(x,z,u,v,top,base,chunk)
        self.count["building"] += 1

    def windows(self,x,z,u,v,top,base,chunk):
        length = math.hypot(u-x,v-z)
        if length < 4.0:
            return
        nx,nz = (v-z)/length*.035,(x-u)/length*.035
        cells = max(1,int(length/4))
        levels = min(15,max(1,int((top-base-.6)/3.1)))
        for i in range(cells):
            t = (i+.5)/cells
            spread = min(.28/cells,.9/length)
            a = x+(u-x)*(t-spread)+nx,z+(v-z)*(t-spread)+nz
            b = x+(u-x)*(t+spread)+nx,z+(v-z)*(t+spread)+nz
            for f in range(levels):
                low = base+f*3.1+1.15
                high = min(top-.1,low+1.35)
                if high>low:
                    self.poly(WINDOW,[(a[0],low,a[1]),(a[0],high,a[1]),(b[0],high,b[1]),(b[0],low,b[1])],chunk)

    def polygon(self, layer, coords, tags):
        rings = normalize(coords)
        if not rings:
            return
        if layer == "building":
            self.building(rings,tags)
            return
        subtype = next((tags[k] for k in ("natural","landuse","leisure") if tags.get(k) in VEG),"")
        if layer == "water":
            style,y = WATER,.045
        elif layer == "runway":
            style,y = MAIN,.038
        elif layer == "road":
            style,y = PATH,.022
        else:
            style,y = ((WOOD,.014) if subtype in ("wood","forest","scrub","nature_reserve","wetland")
                       else (FIELD,.011) if subtype in ("meadow","farmland","orchard","allotments")
                       else (LOT,.009) if subtype in ("residential","industrial","commercial","retail","railway","brownfield")
                       else (PARK,.017))
        pts,tris = triangulate(rings)
        self.flat(style,pts,tris,y)
        if style in (WATER,WOOD,PARK):
            self.cartography.append((style,rings))
        self.count[layer] += 1
        if style in (WOOD,PARK):
            self.trees(rings, style)

    def trees(self, rings, style):
        center = rings[0]
        if not center or abs(area(center)) < 200:
            return
        minx,maxx = min(p[0] for p in center),max(p[0] for p in center)
        minz,maxz = min(p[1] for p in center),max(p[1] for p in center)
        if max(abs(minx),abs(maxx),abs(minz),abs(maxz)) > 4600:
            return
        step = 34 if style == WOOD else 49
        for i in range(math.ceil(minx/step), math.floor(maxx/step)+1):
            for j in range(math.ceil(minz/step), math.floor(maxz/step)+1):
                seed = (i*73856093 ^ j*19349663) & 65535
                x,z = i*step+(seed%13-6),j*step+(seed%17-8)
                if SITE[0]-3 < x < SITE[2]+3 and SITE[1]-3 < z < SITE[3]+3:
                    continue
                if not inside((x,z),center) or any(inside((x,z),h) for h in rings[1:]):
                    continue
                h = 5.3 + (seed%10)*.45
                radius = h*.27
                chunk = tile(x),tile(z)
                trunk = [(x-.16,z-.16),(x+.16,z-.16),(x+.16,z+.16),(x-.16,z+.16)]
                for (a,b),(c,d) in zip(trunk,trunk[1:]+trunk[:1]):
                    self.poly(TRUNK,[(a,0,b),(a,h*.67,b),(c,h*.67,d),(c,0,d)],chunk)
                ring = [(x+radius*math.cos(k*math.tau/7),z+radius*math.sin(k*math.tau/7)) for k in range(7)]
                for (a,b),(c,d) in zip(ring,ring[1:]+ring[:1]):
                    self.poly(FOLIAGE,[(a,h*.62,b),(x,h+1,z),(c,h*.62,d)],chunk)
                    self.poly(FOLIAGE,[(x,h*.35,z),(a,h*.62,b),(c,h*.62,d)],chunk)

    def line(self, layer, coordinates, tags):
        coords = [point(c) for c in coordinates]
        coords = [p for i,p in enumerate(coords) if not i or p != coords[i-1]]
        if len(coords)<2 or tags.get("tunnel","no") not in ("no","0","false"):
            return
        road = tags.get("highway","").removesuffix("_link")
        major = road in MAJOR
        bridge = tags.get("bridge") not in (None,"no","0","false")
        if layer == "water":
            style,y,w = WATER,.044,(14 if tags.get("waterway") == "river" else 2.4)
        elif layer == "rail":
            if tags.get("railway") in ("abandoned","disused","razed","proposed"):
                return
            style,y,w = RAIL,.048,2.7
        elif layer == "runway":
            style,y,w = MAIN,.042,(40 if tags.get("aeroway") == "runway" else 11)
        else:
            width = WIDTH.get(road,3)
            style = MAIN if major else (ROAD if width > 3.3 else PATH)
            lanes = fit(tags.get("lanes"),0)
            w = min(38,max(1,fit(tags.get("width"),max(width,lanes*3.05))))
            y = .069 if bridge else .034 if major else .028 if style == ROAD else .023
        if major and layer=="road":
            self.cartography.append((MAIN,coords))
        if layer=="rail" and tags.get("railway") in ("rail","tram"):
            self.cartography.append((RAIL,coords))
        segments = []
        for (x,z),(u,v) in zip(coords,coords[1:]):
            length = math.hypot(u-x,v-z)
            segments.append(((u-x)/length,(v-z)/length))
        edges = []
        for i,(x,z) in enumerate(coords):
            a = segments[max(i-1,0)]
            b = segments[min(i,len(segments)-1)]
            nx,nz = -a[1]-b[1],a[0]+b[0]
            mag = math.hypot(nx,nz)
            if mag<.01:
                nx,nz,mag = -a[1],a[0],1
            nx,nz = nx/mag,nz/mag
            spread = w/2 / max(.58,nx*-a[1]+nz*a[0])
            edges.append(((x+nx*spread,y,z+nz*spread),(x-nx*spread,y,z-nz*spread)))
        meter_count = 0
        for i,((a,b),(c,d)) in enumerate(zip(edges,edges[1:])):
            self.poly(style,[a,c,d,b])
            if major and layer=="road":
                (x,z),(u,v) = coords[i:i+2]
                length = math.hypot(u-x,v-z)
                ux,uz = segments[i]
                # Paint is only needed near the current playable district.
                if min(math.hypot(x,z),math.hypot(u,v)) < 1250:
                    start = math.ceil(meter_count/10)*10-meter_count
                    while start+3.4 < length:
                        p,q = (x+ux*start,z+uz*start),(x+ux*(start+3.4),z+uz*(start+3.4))
                        nx,nz = -uz*.065,ux*.065
                        self.poly(MARK,[(p[0]+nx,y+.006,p[1]+nz),(q[0]+nx,y+.006,q[1]+nz),
                                        (q[0]-nx,y+.006,q[1]-nz),(p[0]-nx,y+.006,p[1]-nz)])
                        start += 10
                meter_count += length
        self.count[layer] += 1

    def feature(self,f):
        layer = f["properties"].get("_layer")
        if layer == "boundary" and f["properties"].get("admin_level") == "9":
            name = f["properties"].get("name","")
            g = f.get("geometry")
            if name.endswith("административный округ") and g:
                if g["type"] == "Polygon":
                    parts = [g["coordinates"]]
                elif g["type"] == "MultiPolygon":
                    parts = g["coordinates"]
                else:
                    return
                self.districts.append((name, [r for c in parts if (r := normalize(c))]))
            return
        if layer not in ("building","water","land","road","rail","runway"):
            return
        geom = f.get("geometry")
        if not geom:
            return
        shape, coords = geom["type"],geom["coordinates"]
        if shape.startswith("Multi"):
            shape = shape[5:]
            items = coords
        else:
            items = [coords]
        for item in items:
            if shape == "Polygon" and layer != "rail":
                self.polygon(layer,item,f["properties"])
            elif shape == "LineString" and layer != "building":
                self.line(layer,item,f["properties"])

    def export_tiles(self, path):
        manifest = []
        for (i,j),geometry in sorted(self.chunks.items()):
            groups = [(key,value) for key,value in sorted(geometry.groups.items()) if value[1]]
            if not groups:
                continue
            buffer = io.BytesIO()
            buffer.write(b"LCW1")
            buffer.write(struct.pack("<H",len(groups)))
            for key,(verts,indices,_) in groups:
                buffer.write(struct.pack("<BII",key,len(verts)//3,len(indices)))
                local = array('f',verts)
                for k in range(0,len(local),3):
                    local[k] -= i*TILE
                    local[k+2] -= j*TILE
                if sys.byteorder != "little":
                    local.byteswap()
                    indices.byteswap()
                buffer.write(local.tobytes())
                buffer.write(indices.tobytes())
            key = f"{i}_{j}"
            (path/(key+".bytes")).write_bytes(gzip.compress(buffer.getvalue(),compresslevel=6,mtime=0))
            manifest.append(key)
        if self.selected_tiles is None:
            (path/"tiles.txt").write_text("\n".join(manifest)+"\n")
        return len(manifest)

    def export(self, path):
        total = self.export_tiles(path)
        if self.selected_tiles is not None:
            return total
        self.draw_map(path/"map.png")
        districts = [{"name": name, "polygons": [
                {"rings":[{"xz": [round(v,1) for point in ring for v in point]} for ring in rings]}
                for rings in polygons]}
            for name,polygons in self.districts]
        (path/"districts.json").write_text(json.dumps({"districts": districts},
                                                       ensure_ascii=False, separators=(",",":")),
                                           encoding="utf-8")
        (path/"attribution.txt").write_text(
            "© OpenStreetMap contributors\nhttps://www.openstreetmap.org/copyright\n"
            "Open Database License (ODbL) 1.0\nhttps://opendatacommons.org/licenses/odbl/1-0/\n"
            "Omsk extract, OpenStreetMap France, 2026-11-17\n"
            "https://download.openstreetmap.fr/extracts/russia/siberian_federal_district/omsk_oblast-latest.osm.pbf\n"
            "Transformed to a local metre grid and clipped into streaming meshes. "
            "Buildings in the fictional start facility footprint have been removed.\n",
            encoding="utf-8")
        return total

    def draw_map(self,path):
        width,height = 1024,1280
        lo,hi = point(CITY[:2]),point(CITY[2:])
        base = (37,49,58)
        palette = {WATER:(43,118,156),PARK:(59,81,67),WOOD:(52,75,60),MAIN:(157,146,119),RAIL:(116,93,86)}
        image = [bytearray(base*width) for _ in range(height)]

        def xy(p):
            return ((p[0]-lo[0])/(hi[0]-lo[0])*(width-1),(hi[1]-p[1])/(hi[1]-lo[1])*(height-1))

        for style,shape in sorted(self.cartography,key=lambda it: (WOOD,PARK,WATER,RAIL,MAIN).index(it[0])):
            color = bytes(palette[style])
            if style in (MAIN,RAIL):
                coords = [xy(c) for c in shape]
                for (x,y),(u,v) in zip(coords,coords[1:]):
                    count = int(max(abs(u-x),abs(v-y)))+1
                    for i in range(count+1):
                        a,b = round(x+(u-x)*i/count),round(y+(v-y)*i/count)
                        if 0<=a<width and 0<=b<height:
                            image[b][a*3:a*3+3] = color
                continue
            rings = [[xy(p) for p in ring] for ring in shape]
            spans = [(a,b) for ring in rings for a,b in zip(ring,ring[1:]+ring[:1])]
            lo_y = max(0, math.floor(min(p[1] for p in rings[0])))
            hi_y = min(height-1, math.ceil(max(p[1] for p in rings[0])))
            for y in range(lo_y,hi_y+1):
                cuts = sorted(a[0]+(y-a[1])*(b[0]-a[0])/(b[1]-a[1])
                              for a,b in spans if (a[1]>y)!=(b[1]>y))
                for a,b in zip(cuts[::2],cuts[1::2]):
                    a,b = max(0,math.ceil(a)),min(width,math.ceil(b))
                    if b>a:
                        image[y][a*3:b*3] = color*(b-a)

        border = bytes((99,137,147))
        for _,parts in self.districts:
            for rings in parts:
                for ring in rings:
                    coords = [xy(p) for p in ring]
                    for (x,y),(u,v) in zip(coords,coords[1:]+coords[:1]):
                        n = int(max(abs(u-x),abs(v-y)))+1
                        for i in range(n+1):
                            a,b = round(x+(u-x)*i/n),round(y+(v-y)*i/n)
                            if 0<=a<width and 0<=b<height and (a+b)%9 < 5:
                                image[b][a*3:a*3+3] = border

        def chunk(tag,data):
            return struct.pack(">I",len(data))+tag+data+struct.pack(">I",zlib.crc32(tag+data))
        data = b"\x89PNG\r\n\x1a\n"
        data += chunk(b"IHDR",struct.pack(">IIBBBBB",width,height,8,2,0,0,0))
        data += chunk(b"IDAT",zlib.compress(b"".join(b"\0"+row for row in image),6))
        path.write_bytes(data+chunk(b"IEND",b""))


def tile_key(value):
    if not re.fullmatch(r"-?\d+_-?\d+", value):
        raise argparse.ArgumentTypeError("tile format is x_z (e.g. 2_-3)")
    return tuple(map(int,value.split("_")))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input",type=Path)
    parser.add_argument("output",type=Path)
    parser.add_argument("--tile", action="append", type=tile_key,
                        help="replace only this tile; repeat for multiple tiles; preserve the master index")
    parser.add_argument("--exclude-building-ids", nargs="+", default=(),
                        help="OSM building IDs (like way/123) supplied by detailed landmark meshes")
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    args.output.mkdir(parents=True,exist_ok=True)
    with gzip.open(args.input,"rt",encoding="utf-8") as stream:
        features = json.load(stream)["features"]
    city = City(set(args.tile) if args.tile else None)
    exclusions = set(args.exclude_building_ids)
    for feature in features:
        if feature.get("id") in exclusions and feature["properties"].get("_layer") == "building":
            continue
        city.feature(feature)
    total = city.export(args.output)
    print(f"Exported {total:,} Omsk tiles.", dict(city.count), flush=True)


if __name__ == "__main__":
    main()
