"""Build an editable map of Omsk from the bundled OpenStreetMap GeoJSON.

Blender 4.3+: blender -b -t 8 -P omsk_blender.py -- --data omsk.geojson.gz --out omsk.blend
Use --render omsk.png for the overview or --detail omsk_detail.png for the inset.
"""
import argparse
import gzip
import json
import math
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.geometry import delaunay_2d_cdt, tessellate_polygon

HERE = Path(globals().get("__file__", bpy.data.filepath or ".")).resolve().parent
ORIGIN = (73.401, 54.994)
SHORT_NAMES = ("center", "chkalovsk", "rabochie", "staraya_moskovka")
NEIGHBORHOODS = [
    ("ЦЕНТР", 73.3681, 54.9903, -850, 510, 0),
    ("ЧКАЛОВСК", 73.4731016, 54.9778085, 680, 620, 1),
    ("РАБОЧИЕ", 73.419435, 54.942835, -470, -710, 2),
    ("СТАРАЯ МОСКОВКА", 73.4712567, 54.9247624, 640, -490, 3),
]
GROUPS = {
    "ground": "01 · Рельеф и подложка", "water": "02 · Реки и водоёмы",
    "land": "03 · Парки и землепользование", "road": "04 · Улицы и мосты",
    "rail": "05 · Железная дорога и трамвай", "building": "06 · Здания",
    "runway": "07 · Аэродромы и площадки", "poi": "08 · Городские ориентиры",
    "boundary": "09 · Административные границы", "labels": "10 · Районы и подписи",
    "stage": "11 · Камеры и освещение",
}
COLORS = {
    "ground": (0.048, .071, .062), "water": (.019, .153, .209),
    "waterline": (.032, .175, .229), "green": (.085,.165,.104),
    "forest": (.046,.109,.073), "farm": (.141,.152,.076),
    "industry": (.123,.128,.125), "residential": (.088,.114,.106),
    "park": (.103,.187,.113), "recreation": (.112,.171,.114),
    "sand": (.241,.221,.153), "runway": (.250,.275,.267),
    "rail": (.275,.167,.117), "tram": (.268,.217,.156),
    "path": (.110,.137,.123), "road_local": (.172,.209,.194),
    "road_medium": (.303,.335,.288), "road_main": (.477,.434,.314),
    "bridge": (.631,.504,.305), "boundary": (.369,.521,.468),
    "home": (.383,.428,.359), "house": (.300,.366,.302),
    "historic": (.586,.448,.301), "public": (.519,.535,.443),
    "industrial_bld": (.324,.373,.383), "other_bld": (.383,.412,.373),
    "roof_home": (.366,.401,.338), "roof_house": (.363,.405,.320),
    "roof_historic": (.565,.446,.276), "roof_public": (.594,.584,.433),
    "roof_industrial_bld": (.370,.416,.428), "roof_other_bld": (.432,.463,.391),
    "paper": (.904,.928,.858), "dim": (.402,.584,.552),
    "panel": (.018,.041,.045),
    "mark0": (.94,.69,.31), "mark1": (.34,.77,.63),
    "mark2": (.85,.53,.42), "mark3": (.53,.70,.96),
}
ROAD_WIDTHS = {
    "motorway": (12, "road_main"), "trunk": (10,"road_main"), "primary": (9,"road_main"),
    "secondary": (7,"road_medium"), "tertiary": (6,"road_medium"),
    "unclassified": (4.4,"road_local"), "residential": (4.5,"road_local"),
    "living_street": (3.8,"road_local"), "service": (3.2,"road_local"),
    "pedestrian": (3.6,"path"), "track": (2.6,"path"), "cycleway": (1.8,"path"),
    "footway": (1.5,"path"), "path": (1.3,"path"), "steps": (1.4,"path"),
    "construction": (2.7,"path"), "road": (4,"road_local"),
}
LAND_STYLES = {
    "wood":"forest", "forest":"forest", "scrub":"forest", "park":"park", "garden":"park",
    "grass":"green", "grassland":"green", "greenfield":"green", "cemetery":"green",
    "meadow":"green", "village_green":"green", "flowerbed":"green", "nature_reserve":"forest",
    "wetland":"forest", "farmland":"farm", "farmyard":"farm", "orchard":"farm", "allotments":"farm",
    "quarry":"sand", "sand":"sand", "beach":"sand", "bare_rock":"sand", "construction":"sand",
    "landfill":"industry", "industrial":"industry", "commercial":"industry", "retail":"industry",
    "residential":"residential", "railway":"industry", "military":"industry",
    "pitch":"recreation", "sports_centre":"recreation", "playground":"recreation",
    "recreation_ground":"recreation", "stadium":"recreation", "dog_park":"recreation",
}
LAND_Z = {"farm": .15, "residential":.20, "industry":.25,"sand":.30,"green":.45,
          "forest":.52, "park":.60, "recreation":.72}


def utm43(lon, lat):
    """WGS84 / UTM zone 43N (EPSG:32643), easting and northing in metres."""
    a, e2, k = 6378137.0, 0.0066943799901413165, .9996
    p = math.radians(lat)
    s,c = math.sin(p),math.cos(p)
    e4,e6 = e2**2,e2**3
    m = a * ((1-e2/4-3*e4/64-5*e6/256)*p - (3*e2/8+3*e4/32+45*e6/1024)*math.sin(2*p)
         + (15*e4/256+45*e6/1024)*math.sin(4*p) - 35*e6/3072*math.sin(6*p))
    n = a/math.sqrt(1-e2*s*s)
    t = (s/c)**2
    ep = e2/(1-e2)
    q = ep*c*c
    d = math.radians(lon-75)*c
    x = 500000 + k*n*(d+(1-t+q)*d**3/6+(5-18*t+t*t+72*q-58*ep)*d**5/120)
    y = k*(m+n*(s/c)*(d*d/2+(5-t+9*q+4*q*q)*d**4/24+
           (61-58*t+t*t+600*q-330*ep)*d**6/720))
    return x,y


ORIGIN_UTM = utm43(*ORIGIN)


def xy(coordinate):
    x,y = utm43(*coordinate[:2])
    return x-ORIGIN_UTM[0], y-ORIGIN_UTM[1]


def number(value, default=None):
    if value is None:
        return default
    match = re.match(r"^\s*(-?\d+(?:[.,]\d+)?)\s*(m|ft|'|feet)?\s*$", str(value))
    if not match:
        return default
    n = float(match[1].replace(",","."))
    return n*.3048 if match[2] in ("ft","'","feet") else n


def signed_area(ring):
    return sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(ring,ring[1:]+ring[:1]))/2


def in_ring(x,y,ring):
    inside = False
    for (a,b),(c,d) in zip(ring,ring[1:]+ring[:1]):
        if (b>y) != (d>y) and x < a + (y-b)*(c-a)/(d-b):
            inside = not inside
    return inside


def mesh_faces(rings):
    flat = [v for ring in rings for v in ring]
    if len(rings) == 1:
        triangles = tessellate_polygon([[Vector(v) for v in flat]])
        return flat, triangles
    edges, offset = [], 0
    for ring in rings:
        edges.extend((offset+i,offset+(i+1)%len(ring)) for i in range(len(ring)))
        offset += len(ring)
    pts, _, faces, _, _, _ = delaunay_2d_cdt([Vector(v) for v in flat],edges,[],0,0.002,False)
    clean = []
    for face in faces:
        x,y = (sum(pts[i][axis] for i in face)/len(face) for axis in (0,1))
        if in_ring(x,y,rings[0]) and not any(in_ring(x,y,hole) for hole in rings[1:]):
            clean.append(face)
    return [tuple(v) for v in pts],clean


class Atlas:
    def __init__(self, source):
        self.source = source
        self.scene = bpy.data.scenes.new("ОМСК · геоданные")
        self.groups = {}
        self.batch = defaultdict(lambda: ([],[],[]))
        self.stats = Counter()
        self.caption_candidates = {}
        self.font = None
        self.materials = {}
        # Use a separate generated scene and window context.
        if bpy.context.window:
            bpy.context.window.scene = self.scene
        for layer, name in GROUPS.items():
            col = bpy.data.collections.new(name)
            self.scene.collection.children.link(col)
            self.groups[layer] = col
        for path in (
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "C:/Windows/Fonts/arial.ttf", "/System/Library/Fonts/Supplemental/Arial.ttf",
        ):
            if Path(path).is_file():
                self.font = bpy.data.fonts.load(path,check_existing=True)
                if not self.font.packed_file:
                    self.font.pack()
                break

    def material(self, key):
        if key in self.materials:
            return self.materials[key]
        name = "Омск · " + key
        mat = bpy.data.materials.new(name)
        color = COLORS[key]
        mat.diffuse_color = (*color,1)
        mat.use_nodes = True
        nodes = mat.node_tree.nodes
        shader = nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = (*color,1)
        shader.inputs["Roughness"].default_value = .76
        if key in ("ground","water","forest","farm"):
            noise = nodes.new("ShaderNodeTexNoise")
            noise.inputs["Scale"].default_value = .004 if key != "water" else .018
            noise.inputs["Detail"].default_value = 2.6
            tex = nodes.new("ShaderNodeTexCoord")
            ramp = nodes.new("ShaderNodeValToRGB")
            ramp.color_ramp.elements[0].position = .15
            ramp.color_ramp.elements[0].color = (*(v*.71 for v in color),1)
            ramp.color_ramp.elements[1].position = .90
            ramp.color_ramp.elements[1].color = (*(v*1.30 for v in color),1)
            mat.node_tree.links.new(tex.outputs["Object"],noise.inputs["Vector"])
            mat.node_tree.links.new(noise.outputs["Fac"],ramp.inputs[0])
            mat.node_tree.links.new(ramp.outputs[0],shader.inputs["Base Color"])
            if key == "water":
                shader.inputs["Roughness"].default_value = .38
        if key in ("paper","dim","panel") or key.startswith("mark"):
            emission = nodes.new("ShaderNodeEmission")
            emission.inputs["Color"].default_value = (*color,1)
            mat.node_tree.links.new(emission.outputs[0],nodes.get("Material Output").inputs["Surface"])
        self.materials[key] = mat
        return mat

    def append(self, layer, points, faces, mat, identity=None, tile=None):
        if not faces:
            return
        if tile is None:
            tile = tuple(math.floor(c/2300) for c in points[0][:2]) if layer in ("building","road","poi") else None
        verts, polys, mats = self.batch[(layer,tile)]
        offset = len(verts)
        verts.extend(points)
        polys.extend(tuple(offset+i for i in face) for face in faces)
        mats.extend((mat,identity) for _ in faces)

    def create_mesh(self, name, layer, verts, faces, mat):
        data = bpy.data.meshes.new(name)
        data.from_pydata(verts,[],faces)
        obj = bpy.data.objects.new(name,data)
        self.groups[layer].objects.link(obj)
        if mat:
            data.materials.append(self.material(mat))
        return obj

    def flush(self):
        for (layer,tile),(verts,faces,meta) in self.batch.items():
            name = GROUPS[layer].split(" · ")[1]
            if tile is not None:
                name += f" · {tile[0]:+d} {tile[1]:+d}"
            obj = self.create_mesh(name,layer,verts,faces,None)
            styles = list(dict.fromkeys(key for key,_ in meta))
            for style in styles:
                obj.data.materials.append(self.material(style))
            lookup = {key:i for i,key in enumerate(styles)}
            obj.data.polygons.foreach_set("material_index",[lookup[key] for key,_ in meta])
            if any(identity for _,identity in meta):
                ids = obj.data.attributes.new("osm_id","INT","FACE")
                osm_type = obj.data.attributes.new("osm_type","INT","FACE")
                ids.data.foreach_set("value",[i[0] if i else 0 for _,i in meta])
                osm_type.data.foreach_set("value",[i[1] if i else 0 for _,i in meta])
        self.batch.clear()

    def area(self, layer, coords, props, identity):
        rings = []
        for source in coords:
            ring = []
            for coordinate in source:
                point = tuple(Vector(xy(coordinate)))
                if not ring or point != ring[-1]:
                    ring.append(point)
            if ring and ring[0] == ring[-1]:
                ring.pop()
            if len(ring) >= 3 and abs(signed_area(ring)) > .015:
                rings.append(ring)
        if not rings:
            return False
        if layer == "boundary":
            return sum(self.line(layer, ring+[ring[0]],props,identity,project=False)
                       for ring in rings)
        top_points,faces = mesh_faces(rings)
        if not faces:
            return False
        if layer == "building":
            kind = props.get("building") or props.get("building:part")
            defaults = {"apartments":(15,"home"),"residential":(8,"home"),
                        "house":(5,"house"),"detached":(5,"house"),"garages":(3,"house"),
                        "garage":(3,"house"),"shed":(3,"house"),"industrial":(9,"industrial_bld"),
                        "warehouse":(8,"industrial_bld"),"church":(18,"historic"),
                        "cathedral":(28,"historic"),"retail":(8,"public"),"commercial":(12,"public"),
                        "school":(10,"public"),"hospital":(12,"public"),"office":(15,"public")}
            h,mat = defaults.get(kind,(7,"other_bld"))
            if "historic" in props or "heritage" in props:
                mat = "historic"
            levels = number(props.get("building:levels"))
            known = number(props.get("height"))
            base = max(0,number(props.get("min_height"),0))
            if known is not None and known > 0:
                h = known
                self.stats["height_tag"] += 1
            elif levels and levels > 0:
                h = 3.1*levels+number(props.get("roof:height"),0)
                self.stats["levels_tag"] += 1
            else:
                if kind == "yes":
                    size = abs(signed_area(rings[0]))
                    h,mat = (4.4,"house") if size < 115 else (8.4,"other_bld")
                self.stats["height_estimated"] += 1
            z = max(base+1,min(h,210))
            tile = tuple(math.floor(c/2300) for c in rings[0][0])
            self.append(layer,[(x,y,z) for x,y in top_points],faces,"roof_"+mat,identity,tile)
            for hole, ring in enumerate(rings):
                if (signed_area(ring)>0) == bool(hole):
                    ring.reverse()
                n = len(ring)
                self.append(layer, [(x,y,k) for k in (base,z) for x,y in ring],
                            [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],mat,identity,tile)
            return True
        if layer == "water":
            mat,z = "water",1.1
        elif layer == "road":
            mat,z = "path",1.84
        elif layer == "poi":
            x,y = sum(p[0] for p in rings[0])/len(rings[0]),sum(p[1] for p in rings[0])/len(rings[0])
            return self.point(x,y,props,identity)
        elif layer == "runway":
            mat,z = "runway",2.1
        elif layer == "land":
            category = next((props[k] for k in ("leisure","natural","landuse")
                             if props.get(k) in LAND_STYLES),None)
            mat = LAND_STYLES.get(category,"green")
            z = LAND_Z[mat]
        else:
            return False
        self.append(layer,[(x,y,z) for x,y in top_points],faces,mat,identity)
        return True

    def line(self,layer,coords,props,identity,project=True):
        pts = []
        for coord in coords:
            p = Vector(xy(coord) if project else coord)
            if not pts or (p-pts[-1]).length > .05:
                pts.append(p)
        if len(pts) < 2:
            return False
        if layer == "road":
            street = props.get("highway","road").replace("_link","")
            w,mat = ROAD_WIDTHS.get(street,(2.3,"path"))
            width = number(props.get("width"))
            if width and 1 < width < 55:
                w = width
            bridge = props.get("bridge") not in (None,"no","false","0")
            if props.get("tunnel") not in (None,"no","false","0") and not bridge:
                self.stats["tunnel"] += 1
                return False
            z = 7.0 if bridge else (1.82 if mat == "path" else 2.0 + min(w,35)*.015)
            if bridge:
                mat = "bridge"
        elif layer == "rail":
            railway = props.get("railway","rail")
            if railway in ("abandoned","disused","razed","proposed") or props.get("tunnel") == "yes":
                return False
            w,mat = (2.8,"tram") if railway in ("tram","light_rail") else (3.8,"rail")
            z = 6 if props.get("bridge","no") != "no" else 2.8
        elif layer == "water":
            w,mat,z = (9 if props.get("waterway")=="river" else 2.5),"waterline",1.08
        elif layer == "runway":
            w,mat,z = (40 if props.get("aeroway")=="runway" else 11),"runway",2.17
        elif layer == "boundary":
            w,mat,z = 3.0,"boundary",1.62
        else:
            return False
        verts,faces = [],[]
        length = len(pts)
        for i,p in enumerate(pts):
            before = (p-pts[i-1]).normalized() if i else (pts[1]-p).normalized()
            after = (pts[i+1]-p).normalized() if i+1 < length else before
            n = Vector((-before.y-after.y,before.x+after.x))
            if n.length < .001:
                n = Vector((-before.y,before.x))
            n.normalize()
            spread = w/2 / max(.55,n.dot(Vector((-before.y,before.x))))
            verts.extend([(p.x+n.x*spread,p.y+n.y*spread,z),(p.x-n.x*spread,p.y-n.y*spread,z)])
            if i:
                j = i*2
                faces.append((j-2,j-1,j+1,j))
        self.append(layer,verts,faces,mat,identity)
        return True

    def point(self,x,y,props,identity):
        name = props.get("name:ru",props.get("name",""))
        if not name:
            return False
        r = 3.0
        self.append("poi",[(x-r,y-r,5),(x+r,y-r,5),(x+r,y+r,5),(x-r,y+r,5)],
                    [(0,1,2,3)],"paper",identity)
        return True

    def candidate(self, layer, kind, coords, props):
        name = props.get("name:ru",props.get("name",""))
        if not name:
            return
        lookup = name.casefold().replace("ё","е")
        labels = {
            "иртыш": ("Иртыш",1), "омь": ("Омь",1),
            "омск-пассажирский": ("Омск-Пассажирский",2),
            "парк имени 30-летия влксм": ("Парк 30-летия ВЛКСМ",2),
            "парк 30-летия влксм": ("Парк 30-летия ВЛКСМ",2),
            "парк культуры и отдыха имени 30-летия влксм": ("Парк 30-летия ВЛКСМ",2),
            "парк победы": ("Парк Победы",2), "птичья гавань": ("Птичья гавань",2),
            "омская крепость": ("Омская крепость",2), "омск (центральный)": ("Аэропорт Омск",2),
            "аэропорт омск центральный": ("Аэропорт Омск",2),
            "иртышская набережная": ("Иртышская наб.",3), "улица ленина": ("ул. Ленина",3),
            "улица богдана хмельницкого": ("ул. Богдана Хмельницкого",3),
            "улица кирова": ("ул. Кирова",3), "космический проспект": ("Космический пр.",3),
            "улица гуртьева": ("ул. Гуртьева",3),
        }
        result = labels.get(lookup)
        if not result or (result[1] == 1 and layer != "water"):
            return
        label,rank = result
        if kind == "Point":
            location = xy(coords)
            score = 1.0
            angle = 0
        elif kind == "LineString":
            vertices = [Vector(xy(c)) for c in coords]
            if len(vertices)<2:
                return
            a,b = max(zip(vertices,vertices[1:]),key=lambda pair:(pair[1]-pair[0]).length_squared)
            score = (b-a).length
            location = tuple((a+b)/2)
            angle = math.atan2(b.y-a.y,b.x-a.x)
            angle = (angle+math.pi/2)%math.pi-math.pi/2
        elif kind in ("Polygon","MultiPolygon"):
            outline = coords[0] if kind == "Polygon" else max(coords,key=lambda p:len(p[0]))[0]
            if not outline:
                return
            vertices = [xy(c) for c in outline]
            location = (sum(v[0] for v in vertices)/len(vertices),sum(v[1] for v in vertices)/len(vertices))
            score = math.sqrt(abs(signed_area(vertices)))
            if rank == 1:
                score = -1
            angle = 0
        else:
            return
        detail_origin = xy((73.421,54.9585))
        if not all(abs(a-b) < 5600 for a,b in zip(location,detail_origin)):
            return
        old = self.caption_candidates.get(label)
        if old is None or score > old[0]:
            self.caption_candidates[label] = (score,location,angle,rank)

    def feature(self,feature):
        props = feature.get("properties",{})
        layer = props.get("_layer")
        geom = feature.get("geometry")
        if not layer or not geom:
            return
        kind,coords = geom["type"],geom["coordinates"]
        self.candidate(layer,kind,coords,props)
        key = str(feature.get("id",""))
        match = re.search(r"([nwr])(?:ode|ay|elation)?[/_:]?(\d+)$",key)
        identity = None
        if match:
            value = int(match[2])
            # Blender INT attributes are signed 32-bit. Keep large node IDs in GeoJSON.
            if 0 < value <= 2147483647:
                identity = (value,{"n":1,"w":2,"r":3}[match[1]])
        if kind == "Polygon":
            done = self.area(layer,coords,props,identity)
        elif kind == "MultiPolygon":
            done = sum(self.area(layer,part,props,identity) for part in coords)
        elif kind == "LineString":
            done = self.line(layer,coords,props,identity)
        elif kind == "MultiLineString":
            done = sum(self.line(layer,part,props,identity) for part in coords)
        elif kind == "Point" and layer == "poi":
            done = self.point(*xy(coords),props,identity)
        elif kind == "MultiPoint" and layer == "poi":
            done = sum(self.point(*xy(coordinate),props,identity) for coordinate in coords)
        else:
            done = False
        if done:
            self.stats[layer] += 1

    def text(self, name, caption, x,y,z,size,mat="paper", align="LEFT", parent=None):
        data = bpy.data.curves.new(name,"FONT")
        data.body = caption
        data.size = size
        data.align_x = align
        if self.font:
            data.font = self.font
        data.materials.append(self.material(mat))
        obj = bpy.data.objects.new(name,data)
        self.groups["labels"].objects.link(obj)
        obj.parent = parent
        obj.location = (x,y,z)
        return obj

    def plate(self,name, x,y,z,w,h, mat, parent=None):
        obj = self.create_mesh(name,"labels",[(-w/2,-h/2,0),(w/2,-h/2,0),
                               (w/2,h/2,0),(-w/2,h/2,0)],[(0,1,2,3)],mat)
        obj.parent = parent
        obj.location = (x,y,z)
        return obj

    def marker(self,label,x,y,dx,dy,idx):
        mat = f"mark{idx}"
        z = 91
        # Footprint matching the anchor maps marker centre to the actual location.
        r=57
        verts = [(x+math.cos(i*math.tau/32)*radius,
                  y+math.sin(i*math.tau/32)*radius,z)
                 for radius in (r,r+13) for i in range(32)]
        self.create_mesh("Точка района · "+label,"labels",verts,
                         [(i,i+32,(i+1)%32+32,(i+1)%32) for i in range(32)],mat)
        t = Vector((dx,dy)).normalized()
        q = Vector((-t.y,t.x))*7.5
        a,b = Vector((x,y))+t*66,Vector((x+dx,y+dy))
        vs = [(*point,z) for point in (a+q,a-q,b-q,b+q)]
        self.create_mesh("Выноска · "+label,"labels",vs,[(0,1,2,3)],mat)
        w = max(680,len(label)*106+165)
        self.plate("Подложка · "+label,x+dx,y+dy,z+1,w,286,"panel")
        self.plate("Цвет района · "+label,x+dx-w/2+13,y+dy,z+2,26,286,mat)
        self.text(label,label,x+dx,y+dy-57,z+3,207,mat,"CENTER")

    def camera(self,name,focus,scale,vertical=False):
        obj = bpy.data.objects.new(name,bpy.data.cameras.new(name))
        self.groups["stage"].objects.link(obj)
        x,y = focus
        obj.location = (x,y-(0 if vertical else scale*.32),scale*.96)
        obj.rotation_euler = (Vector((x,y,0))-obj.location).to_track_quat("-Z","Y").to_euler()
        obj.data.type = "ORTHO"
        obj.data.ortho_scale = scale
        obj.data.clip_start = 5
        obj.data.clip_end = max(180000,scale*5)
        return obj

    def framing(self, camera, scale, width, height, detail=False, district=None,
                terrain=False):
        # Text is in the camera plane so the print labels remain sharp and upright.
        half = scale/2
        v = half*height/width
        margin = scale*.035
        x,y = -half+margin,v-margin
        z = -camera.data.clip_start*2
        if district:
            title,index = district
            self.plate("Подложка заголовка", -half+scale*.33, v-scale*.045, z-.8,
                       scale*.66, scale*.09,"panel",camera)
            self.text("Город","ОМСК / РАЙОНЫ",x,y-scale*.010,z,scale*.013,"dim",parent=camera)
            self.text("Заголовок",title,x,y-scale*.042,z,scale*.029,
                      f"mark{index}",parent=camera)
        else:
            self.text("Городской атлас","ГОРОДСКОЙ АТЛАС",x,y-scale*.013,z,scale*.013,"dim",parent=camera)
            title = "РЕЛЬЕФ ОМСКА" if terrain else "ОМСК"
            self.text("Заголовок",title,x,y-scale*.052,z,scale*.043,parent=camera)
            line = ("COPERNICUS DEM GLO-30" if terrain else
                    ("ЦЕНТР И ЮГО-ВОСТОК" if detail else "ОБЗОР ГОРОДА"))
            self.text("Подзаголовок",line,x,y-scale*.073,z,scale*.013,"paper",parent=camera)
        if not terrain:
            self.text("Источник","© OpenStreetMap contributors • ODbL",x,-v+margin*.66,z,
                      scale*.009,"dim",parent=camera)
        right = half-margin
        self.text("Север","С",right,v-margin-scale*.014,z,scale*.015,"paper","CENTER",camera)
        self.plate("Линия севера",right,v-margin-scale*.040,z,scale*.0018,scale*.020,"paper",camera)
        tipy = v-margin-scale*.025
        arrow = self.create_mesh("Стрелка севера","labels",
             [(right,tipy,0),(right-scale*.0048,tipy-scale*.012,0),
              (right+scale*.0048,tipy-scale*.012,0)],[(0,1,2)],"paper")
        arrow.parent = camera
        arrow.location.z = z
        km = .5 if district else (2 if detail else 5)
        length = km*1000
        y = -v+margin*.75
        self.plate("Масштаб",right-length/2,y,z,length,scale*.0018,"paper",camera)
        for tick in (0,.5,1):
            self.plate("Засечка масштаба",right-length*tick,y,z,scale*.0014,scale*.007,"paper",camera)
        self.text("Километры","500 м" if district else f"{km} км",right,y+scale*.007,z,scale*.010,"paper","RIGHT",camera)

    def stage(self,bounds):
        west,south,east,north = bounds
        corners = [xy(p) for p in [(west,south),(east,south),(east,north),(west,north)]]
        # Use the true projected clip as a guide for the overview framing.
        lowx,highx = min(p[0] for p in corners),max(p[0] for p in corners)
        lowy,highy = min(p[1] for p in corners),max(p[1] for p in corners)
        w,h = highx-lowx,highy-lowy
        mx,my = (highx+lowx)/2,(highy+lowy)/2
        self.create_mesh("Матовая картографическая подложка","ground",
                          [(mx+a*w,my+b*h,-.08) for a,b in [(-1.4,-1.4),(1.4,-1.4),(1.4,1.4),(-1.4,1.4)]],
                          [(0,1,2,3)],"ground")
        for label,lon,lat,dx,dy,index in NEIGHBORHOODS:
            self.marker(label,*xy((lon,lat)),dx,dy,index)
        scene = self.scene
        width,height = 1820,1760
        scale = max(w,h*width/height*.95)*1.10
        overview = self.camera("01 · Омск — общий вид",(mx,my),scale)
        labels = self.groups["labels"]
        overview_hud = bpy.data.collections.new("11 · Оформление обзора")
        scene.collection.children.link(overview_hud)
        self.groups["labels"] = overview_hud
        self.framing(overview,scale,width,height)
        detail_scale = 13200
        detail_camera = self.camera("02 · Центр и юго-восток",xy((73.421,54.9585)),detail_scale)
        detail_hud = bpy.data.collections.new("11 · Оформление крупного плана")
        self.groups["labels"] = detail_hud
        self.framing(detail_camera,detail_scale,width,height,True)
        reserved = [(xy((lon,lat))[0]+dx,xy((lon,lat))[1]+dy)
                    for _,lon,lat,dx,dy,_ in NEIGHBORHOODS]
        for label,(_,location,angle,rank) in sorted(self.caption_candidates.items(),key=lambda pair:pair[1][3]):
            if any(math.dist(location,other)<530 for other in reserved):
                continue
            reserved.append(location)
            obj = self.text("Подпись · "+label,label,*location,68,
                            104 if rank == 1 else 75,"dim" if rank == 3 else "paper","CENTER")
            obj.rotation_euler.z = angle if rank != 2 else 0
        self.groups["labels"] = labels
        scene.camera = overview
        world = bpy.data.worlds.new("Омск · пасмурное небо")
        world.use_nodes = True
        back = world.node_tree.nodes.get("Background")
        back.inputs[0].default_value = (.54,.66,.76,1)
        back.inputs[1].default_value = .46
        scene.world = world
        light = bpy.data.objects.new("Солнце · запад",bpy.data.lights.new("Солнце","SUN"))
        self.groups["stage"].objects.link(light)
        light.rotation_euler = Vector((1,.44,-1.24)).to_track_quat("-Z","Y").to_euler()
        light.data.energy = 2.05
        light.data.angle = math.radians(7)
        scene.render.engine = "BLENDER_EEVEE_NEXT"
        scene.eevee.taa_render_samples = 16
        scene.render.resolution_x = width
        scene.render.resolution_y = height
        scene.render.resolution_percentage = 100
        scene.view_settings.view_transform = "AgX"
        if bpy.context.screen:
            for area in bpy.context.screen.areas:
                if area.type == "VIEW_3D":
                    space = area.spaces.active
                    space.clip_end = 220000
                    space.shading.color_type = "MATERIAL"
                    space.region_3d.view_perspective = "CAMERA"
        return overview,detail_camera,overview_hud,detail_hud

    def district_scenes(self,overview_hud):
        result = []
        labels = self.groups["labels"]
        for label,lon,lat,_,_,idx in NEIGHBORHOODS:
            loc = xy((lon,lat))
            scale = (3300,3900,3500,4000)[idx]
            view = self.scene.copy()
            view.use_fake_user = True
            view.name = "ОМСК · " + label.title()
            view.collection.children.unlink(overview_hud)
            view.collection.children.unlink(labels)
            annotations = bpy.data.collections.new("Подписи · " + label.title())
            view.collection.children.link(annotations)
            self.groups["labels"] = annotations
            view.render.resolution_x = 1200
            view.render.resolution_y = 1000
            view.eevee.taa_render_samples = 10
            camera = self.camera(f"0{idx+3} · "+label.title(),loc,scale)
            view.camera = camera
            self.framing(camera,scale,1200,1000,district=(label,idx))
            anchors = []
            for caption, (_,point,angle,rank) in sorted(self.caption_candidates.items(),
                                                         key=lambda pair:pair[1][3]):
                if abs(point[0]-loc[0]) > scale*.36 or abs(point[1]-loc[1]) > scale*.28:
                    continue
                if any(math.dist(point,prior)<150 for prior in anchors):
                    continue
                anchors.append(point)
                text = self.text(caption,caption,*point,43,34,
                                 "paper" if rank < 3 else "dim","CENTER")
                text.rotation_euler.z = angle if rank != 2 else 0
            result.append((view,SHORT_NAMES[idx]))
        self.groups["labels"] = labels
        return result


    def relief_scene(self,path,overview):
        with gzip.open(path,"rt",encoding="utf-8") as stream:
            terrain = json.load(stream)
        cols,rows = terrain["width"],terrain["height"]
        west,south,east,north = terrain["bbox"]
        values = terrain["rows"]
        if cols < 2 or rows < 2 or len(values) != rows or any(len(row)!=cols for row in values):
            raise ValueError("Invalid elevation grid")
        heights = [float(z) for row in values for z in row]
        elevations = sorted(heights)
        lo,hi = (elevations[int(q*(len(elevations)-1))] for q in (.015,.985))
        points = []
        for j,row in enumerate(values):
            lat = north - (j+.5)*(north-south)/rows
            points.extend((*xy((west+(i+.5)*(east-west)/cols,lat)),h)
                          for i,h in enumerate(row))
        faces = [(k,k+cols,k+cols+1,k+1)
                 for k in range(cols*(rows-1)) if k%cols != cols-1]
        view = self.scene.copy()
        view.use_fake_user = True
        view.name = "ОМСК · Высоты Copernicus"
        for collection in list(view.collection.children):
            view.collection.children.unlink(collection)
        layer = bpy.data.collections.new("Цифровая модель поверхности · Copernicus")
        decorations = bpy.data.collections.new("Камера, свет и атрибуция · Copernicus")
        view.collection.children.link(layer)
        view.collection.children.link(decorations)
        old = self.groups.copy()
        self.groups.update(dict(ground=layer,stage=decorations,labels=decorations))
        mesh = self.create_mesh("DSM · высоты в метрах","ground",points,faces,None)
        elevation = mesh.data.attributes.new("height_m","FLOAT","POINT")
        elevation.data.foreach_set("value",heights)
        mesh.data.polygons.foreach_set("use_smooth",[True]*len(faces))
        mat = bpy.data.materials.new("Copernicus · гипсометрическая окраска")
        mat.use_nodes = True
        nodes = mat.node_tree.nodes
        links = mat.node_tree.links
        attr = nodes.new("ShaderNodeAttribute")
        attr.attribute_name = "height_m"
        ramp_map = nodes.new("ShaderNodeMapRange")
        ramp_map.inputs["From Min"].default_value = lo
        ramp_map.inputs["From Max"].default_value = hi
        ramp = nodes.new("ShaderNodeValToRGB")
        palette = [(0,(.036,.174,.236,1)),(.30,(.093,.272,.196,1)),
                   (.68,(.308,.401,.246,1)),(1,(.682,.583,.370,1))]
        for (position,color),element in zip((palette[0],palette[-1]),ramp.color_ramp.elements):
            element.position,element.color = position,color
        for position,color in palette[1:-1]:
            ramp.color_ramp.elements.new(position).color = color
        shader = nodes.get("Principled BSDF")
        shader.inputs["Roughness"].default_value = .92
        links.new(attr.outputs["Fac"],ramp_map.inputs["Value"])
        links.new(ramp_map.outputs["Result"],ramp.inputs["Fac"])
        links.new(ramp.outputs[0],shader.inputs["Base Color"])
        mesh.data.materials.append(mat)
        sun = next(obj for obj in old["stage"].objects if obj.type == "LIGHT")
        decorations.objects.link(sun)
        view.render.resolution_x = 1350
        view.render.resolution_y = 1320
        view.eevee.taa_render_samples = 10
        scale = overview.data.ortho_scale
        camera = self.camera("07 · Высоты",xy(((west+east)/2,(south+north)/2)),scale)
        view.camera = camera
        self.framing(camera,scale,1350,1320,terrain=True)
        h = scale/2*1320/1350
        for i,line in enumerate([
            f"ВЫСОТА ПОВЕРХНОСТИ • {round(min(heights))}–{round(max(heights))} м",
            "produced using Copernicus WorldDEM-30 © DLR e.V. 2010-2014 and",
            "© Airbus Defence and Space GmbH 2014-2018 provided under",
            "COPERNICUS by the European Union and ESA; all rights reserved",
        ]):
            self.text("Высоты и атрибуция",line,-scale*.465,-h+scale*(.06-i*.009),-10,
                      scale*(.010 if i == 0 else .0068),"dim",parent=camera)
        view["Источник высот"] = terrain["source"]
        view["Атрибуция высот"] = terrain["attribution"]
        view["Шаг сетки, м"] = 60
        view["Покрытие"] = terrain["bbox"]
        view["Модель"] = "DSM, включая растительность и застройку. Высоты в метрах, EGM2008."
        self.groups = old
        return view


def main():
    args = argparse.ArgumentParser(description=__doc__)
    args.add_argument("--data",type=Path,default=HERE/"omsk.geojson.gz")
    args.add_argument("--out",type=Path,default=HERE/"omsk.blend")
    args.add_argument("--render",type=Path)
    args.add_argument("--detail",type=Path)
    args.add_argument("--resolution",type=int,default=100)
    args.add_argument("--district-dir",type=Path)
    args.add_argument("--terrain",type=Path,default=HERE/"omsk_copernicus.json.gz")
    args.add_argument("--relief",type=Path)
    options = args.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    opener = gzip.open if options.data.suffix == ".gz" else open
    with opener(options.data,"rt",encoding="utf-8") as stream:
        source = json.load(stream)
    if not source.get("features") or len(source.get("bbox",[])) != 4:
        raise ValueError("Expected a nonempty GeoJSON FeatureCollection with a WGS84 bbox")
    prior = bpy.context.scene
    atlas = Atlas(source)
    for feature in source["features"]:
        atlas.feature(feature)
    atlas.flush()
    overview,inset,overview_hud,detail_hud = atlas.stage(source["bbox"])
    scene = atlas.scene
    scene.unit_settings.system = "METRIC"
    scene["Геоданные"] = "© OpenStreetMap contributors | https://www.openstreetmap.org/copyright"
    scene["CRS"] = "EPSG:32643"
    scene["UTM_origin_easting"] = ORIGIN_UTM[0]
    scene["UTM_origin_northing"] = ORIGIN_UTM[1]
    scene["Статистика"] = json.dumps(atlas.stats,ensure_ascii=False)
    scene["Высоты"] = "OSM height, затем building:levels. В остальных случаях приближённая высота."
    scene["Районы"] = "Точки-ориентиры. Рабочие — район Рабочих улиц."
    scene["Дата выгрузки OSM"] = "17.11.2026"
    scene["Поставщик выгрузки"] = "https://download.openstreetmap.fr/extracts/russia/siberian_federal_district/"
    scene["Границы охвата WGS84"] = source["bbox"]
    scene["Исходный файл"] = options.data.name
    if prior.name == "Scene" and set(o.name for o in prior.objects) == {"Camera","Cube","Light"}:
        for obj in list(prior.objects):
            bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.scenes.remove(prior)
    atlas.groups["boundary"].hide_render = True
    atlas.groups["poi"].hide_render = True
    detail_scene = scene.copy()
    detail_scene.use_fake_user = True
    detail_scene.name = "ОМСК · Центр и юго-восток"
    detail_scene.collection.children.unlink(overview_hud)
    detail_scene.collection.children.link(detail_hud)
    detail_scene.camera = inset
    district_scenes = atlas.district_scenes(overview_hud)
    relief_scene = atlas.relief_scene(options.terrain,overview) if options.terrain.is_file() else None
    if options.relief and not relief_scene:
        raise FileNotFoundError(options.terrain)
    bpy.context.preferences.filepaths.save_version = 0
    if options.out:
        bpy.ops.wm.save_as_mainfile(filepath=str(options.out.resolve()),compress=True)
    print("OMSK_STATS",json.dumps(atlas.stats,ensure_ascii=False),flush=True)
    renders = [(scene,options.render),(detail_scene,options.detail)]
    if options.district_dir:
        options.district_dir.mkdir(parents=True,exist_ok=True)
        renders.extend((view,options.district_dir/(key+".png")) for view,key in district_scenes)
    if relief_scene and options.relief:
        renders.append((relief_scene,options.relief))
    for view,target in renders:
        if target:
            view.render.resolution_percentage = options.resolution
            view.render.filepath = str(target.resolve())
            bpy.ops.render.render(write_still=True,scene=view.name)


if __name__ == "__main__":
    main()
