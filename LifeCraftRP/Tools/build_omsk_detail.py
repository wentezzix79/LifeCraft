#!/usr/bin/env python3
"""Bake an optional, non-colliding OSM detail layer on top of the main Unity tiles.

blender -b --python-exit-code 1 -P Tools/build_omsk_detail.py -- \
    SourceData/omsk.geojson.gz Assets/Resources/Omsk/Detail
"""
import argparse
from collections import Counter, defaultdict
import gzip
import json
import math
from pathlib import Path
import sys
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.dont_write_bytecode = True
from build_omsk import (
    City, FOLIAGE, MAJOR, MARK, PATH, RAIL, ROOF, SITE, TRUNK, WIDTH, WINDOW,
    area, fit, inside, normalize, point, tile, tile_key,
)


LANDMARK_SHELLS = {"way/61367812", "way/814296045"}
LANDMARK_WOOD = {"relation/2148145", "relation/9926426", "relation/9926427"}
NEAR_START_WINDOWS = 1350
PATHS = {"footway", "pedestrian", "cycleway", "path"}
PAVED = {"asphalt", "paving_stones", "sett", "concrete", "concrete:plates", "brick"}
STREET = MAJOR | {"residential", "living_street", "unclassified", "service"}
FACADE_KINDS = {
    "apartments", "residential", "office", "hotel", "dormitory", "commercial",
    "retail", "hospital", "school", "kindergarten", "college", "university",
    "public", "civic", "government",
}
GABLE_ROOFS = {"gabled", "side_hipped", "half-hipped", "hipped", "pyramidal", "skillion"}


def points(coordinates):
    answer = []
    for c in coordinates:
        p = point(c)
        if not answer or answer[-1] != p:
            answer.append(p)
    return answer


def geometries(features, layer, kind):
    for feature in features:
        if feature["properties"].get("_layer") != layer or not feature.get("geometry"):
            continue
        shape = feature["geometry"]
        if shape["type"] == kind:
            yield feature, shape["coordinates"]
        elif shape["type"] == "Multi" + kind:
            for part in shape["coordinates"]:
                yield feature, part


def inside_starter(x, z, margin=0):
    return SITE[0]-margin <= x <= SITE[2]+margin and SITE[1]-margin <= z <= SITE[3]+margin


def unit(a,b):
    width = math.hypot(b[0]-a[0],b[1]-a[1])
    return (b[0]-a[0])/width, (b[1]-a[1])/width, width


def segment_distance_sq(x,z,a,b):
    dx,dz = b[0]-a[0],b[1]-a[1]
    t = min(1,max(0,((x-a[0])*dx+(z-a[1])*dz)/(dx*dx+dz*dz)))
    return (x-a[0]-t*dx)**2 + (z-a[1]-t*dz)**2


def mid(a,b,t=.5):
    return a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t


def on(tag):
    return tag not in (None,"no","0","false")


class Roads:
    def __init__(self, features):
        self.paths = []
        self.grid = defaultdict(list)
        self.degree = Counter()
        self.step = 60
        for feature, coordinates in geometries(features,"road","LineString"):
            tags = feature["properties"]
            kind = tags.get("highway","").removesuffix("_link")
            if kind not in STREET | PATHS or on(tags.get("tunnel")):
                continue
            route = points(coordinates)
            if len(route) < 2:
                continue
            lanes = max(0,int(fit(tags.get("lanes"),0)))
            width = min(38,max(1,fit(tags.get("width"),max(WIDTH[kind],lanes*3.05))))
            bridge = on(tags.get("bridge"))
            major = kind in MAJOR
            y = .069 if bridge else .034 if major else .028 if WIDTH[kind] > 3.3 else .023
            self.paths.append((route,kind,tags,lanes,width,y,bridge))
            for a,b in zip(route,route[1:]):
                if kind in STREET:
                    self.degree.update((a,b))
                segment = (a,b,width,kind in STREET,bridge)
                margin = width/2+2
                low = self.key(min(a[0],b[0])-margin,min(a[1],b[1])-margin)
                high = self.key(max(a[0],b[0])+margin,max(a[1],b[1])+margin)
                for u in range(low[0],high[0]+1):
                    for v in range(low[1],high[1]+1):
                        self.grid[u,v].append(segment)

    def key(self,x,z):
        return math.floor(x/self.step), math.floor(z/self.step)

    def around(self,x,z):
        return self.grid.get(self.key(x,z),())

    def inhibits_tree(self,x,z):
        for a,b,width,_,bridge in self.around(x,z):
            if not bridge and segment_distance_sq(x,z,a,b) < (width*.5+1.8)**2:
                return True
        return False


class Detail(City):
    def __init__(self, features, selected_tiles=None):
        super().__init__(selected_tiles)
        self.roads = Roads(features)
        self.tree_cells = set()
        self.tree_counts = Counter()

    def strip(self, style, a,b,y,left,right):
        ux,uz,_=unit(a,b)
        nx,nz = -uz,ux
        self.poly(style,[(a[0]+nx*left,y,a[1]+nz*left),(b[0]+nx*left,y,b[1]+nz*left),
                         (b[0]+nx*right,y,b[1]+nz*right),(a[0]+nx*right,y,a[1]+nz*right)])

    def streets(self):
        for route,kind,tags,lanes,width,y,bridge in self.roads.paths:
            major = kind in MAJOR
            walk = (kind in PATHS and tags.get("surface") in PAVED and width >= 1.3)
            if not major and not walk:
                continue
            oneway = tags.get("oneway") in ("yes","1","true","-1")
            lane_lines = []
            if major and width>=5.5 and not (oneway and lanes < 2):
                lanes = min(10, lanes or 2)
                lane_lines = [(i/lanes-.5)*width for i in range(1,lanes)]
            distance = 0
            for a,b in zip(route,route[1:]):
                ux,uz,length=unit(a,b)
                end = distance+length
                if length < .3 or inside_starter(*mid(a,b),8):
                    distance = end
                    continue
                if walk and length >= 2.5:
                    # This follows mapped pedestrian routes and mapped hard surfaces.
                    for side in (-1,1):
                        self.strip(PATH,a,b,y+.009,side*width/2+.07,side*width/2-.07)
                    self.count["mapped_paved_path_segments"] += 1
                if bridge and major and length >= 1.2:
                    for side in (-1,1):
                        offset = side*(width/2-.23)
                        self.strip(MARK,a,b,y+.011,offset+.065,offset-.065)
                    self.count["bridge_edge_segments"] += 1
                if lane_lines and min(math.hypot(*a),math.hypot(*b)) >= 1250:
                    lo = 3.8 if self.roads.degree[a] > 2 else .12
                    hi = length-3.8 if self.roads.degree[b] > 2 else length-.12
                    start = math.ceil((distance+lo)/10)*10-distance
                    while start+3.2 <= hi:
                        p=(a[0]+ux*start,a[1]+uz*start)
                        q=(a[0]+ux*(start+3.2),a[1]+uz*(start+3.2))
                        for offset in lane_lines:
                            self.strip(MARK,p,q,y+.008,offset+.063,offset-.063)
                            self.count["lane_dashes"] += 1
                        start += 10
                distance = end

    def buildings(self, features):
        for feature, coordinates in geometries(features,"building","Polygon"):
            if feature.get("id") in LANDMARK_SHELLS:
                continue
            tags = feature["properties"]
            kind=tags.get("building",tags.get("building:part",""))
            levels = fit(tags.get("building:levels"),0)
            roof = tags.get("roof:shape")
            has_windows = kind in FACADE_KINDS or levels >= 3
            if not has_windows and roof not in GABLE_ROOFS:
                continue
            rings = normalize(coordinates)
            if not rings:
                continue
            ring = rings[0]
            xs,zs = zip(*ring)
            x,z = (min(xs)+max(xs))/2,(min(zs)+max(zs))/2
            if min(xs) <= SITE[2] and max(xs) >= SITE[0] and min(zs) <= SITE[3] and max(zs) >= SITE[1]:
                continue
            chunk = tile(x),tile(z)
            if not self.selected(chunk):
                continue
            size=abs(area(ring))
            # Match the existing shell heights exactly so overlays remain flush with walls.
            default = 16 if kind == "apartments" else (12 if kind in ("industrial","church") else 8)
            if kind in ("garage","garages","shed","roof"):
                default = 2.8
            elif kind in ("house","detached") or size < 100:
                default = 5.0
            top = fit(tags.get("height"),levels*3.1+fit(tags.get("roof:height"),0) if levels>0 else default)
            base = max(0,fit(tags.get("min_height"),0))
            top = min(180,max(base+1,top))
            original_windows = math.hypot(x,z)<NEAR_START_WINDOWS and 5<=top-base<=46 and size>70
            if has_windows and not original_windows and size>65 and 4.7<=top-base<=100:
                floors = min(18,max(1,round(levels if levels else (top-base)/3.1)))
                edges = [(math.dist(a,b),a,b) for a,b in zip(ring,ring[1:]+ring[:1])]
                longest = max(edges)[0]
                # Sample the principal façades, leaving narrow sidewalls and wings solid.
                prominent = longest*.62
                for length,a,b in edges:
                    if length>=prominent:
                        self.wall_windows(a,b,top,base,floors,chunk,kind)
            if (roof in GABLE_ROOFS and len(rings)==1 and len(ring)==4 and size>=38
                and not tags.get("height") and not tags.get("roof:height") and top-base>=3.1):
                self.roof(ring,top,chunk,roof,tags)

    def wall_windows(self,a,b,top,base,floors,chunk,kind):
        ux,uz,length=unit(a,b)
        if length < 3.5:
            return
        large_glass = kind in ("commercial","office","public","retail")
        cell_width = 6.7 if large_glass else 7.6
        cells = min(16,max(1,int(length/cell_width)))
        step=length/cells
        height=(top-base)/floors
        glazing=min(1.87 if large_glass else 1.31,height*.47)
        if height < 2.1:
            return
        half_span=min(1.25 if large_glass else .79, step*.27)
        normal = uz*.041,-ux*.041
        for c in range(cells):
            s=(c+.5)*step
            p = (a[0]+ux*(s-half_span)+normal[0],a[1]+uz*(s-half_span)+normal[1])
            q = (a[0]+ux*(s+half_span)+normal[0],a[1]+uz*(s+half_span)+normal[1])
            for f in range(floors):
                low=base+height*(f+.32)
                high=low+glazing
                self.poly(WINDOW,[(p[0],low,p[1]),(p[0],high,p[1]),
                                  (q[0],high,q[1]),(q[0],low,q[1])],chunk)
                self.count["facade_windows"] += 1

    def roof(self,ring,top,chunk,kind,tags):
        sides = [unit(a,b) for a,b in zip(ring,ring[1:]+ring[:1])]
        index=max(range(4),key=lambda i:sides[i][2])
        if any(abs(sides[i-1][0]*sides[i][0]+sides[i-1][1]*sides[i][1])>.31 for i in range(4)):
            return
        a,b,c,d=ring[index:]+ring[:index]
        width=min(unit(b,c)[2],unit(a,d)[2])
        if not 3.5<=width<=33:
            return
        height=max(.77,min(5.4,fit(tags.get("roof:levels"),1)*2.1,width*.29))
        eave = top+.025
        ridge = eave+height
        def pt(p,level):
            return (p[0],level,p[1])
        def face(*vertices):
            if area([(v[0],v[2]) for v in vertices]) > 0:
                vertices=vertices[::-1]
            self.poly(ROOF,vertices,chunk)
        A,B,C,D = (pt(p,eave) for p in (a,b,c,d))
        if kind=="skillion":
            C,D=pt(c,ridge),pt(d,ridge)
            face(A,B,C,D)
            face(B,C,pt(c,eave))
            face(C,D,pt(d,eave),pt(c,eave))
            face(D,A,pt(d,eave))
        elif kind=="pyramidal":
            R=pt(mid(mid(a,c),mid(b,d)),ridge)
            for p,q in ((A,B),(B,C),(C,D),(D,A)):
                face(p,q,R)
        else:
            r,s = mid(a,d),mid(b,c)
            if kind in ("hipped","half-hipped","side_hipped"):
                t=min(.38,width/max(1,unit(r,s)[2])*.46)
                r,s=mid(r,s,t),mid(s,r,t)
            R,S=pt(r,ridge),pt(s,ridge)
            face(A,B,S,R)
            face(C,D,R,S)
            face(B,S,C)
            face(D,R,A)
        self.count["mapped_pitched_roofs"] += 1

    def trees(self,features):
        for feature,coordinates in geometries(features,"land","Polygon"):
            if feature.get("id") in LANDMARK_WOOD:
                continue
            tags=feature["properties"]
            urban = tags.get("leisure") in ("park","garden","nature_reserve") or tags.get("landuse")=="recreation_ground"
            forest = tags.get("natural")=="wood" or tags.get("landuse")=="forest"
            if not urban and not forest:
                continue
            rings=normalize(coordinates)
            if not rings:
                continue
            outer = rings[0]
            xs,zs = zip(*outer)
            size=abs(area(outer))
            if size < 300:
                continue
            extent = max(max(map(abs,xs)),max(map(abs,zs)))
            if forest and not urban and extent<=4600:
                # Legacy tile already provides trees for this polygon.
                continue
            step=(29 if size < 300_000 else 43) if urban else (61 if extent<9500 else 86)
            for i in range(math.ceil(min(xs)/step),math.floor(max(xs)/step)+1):
                for j in range(math.ceil(min(zs)/step),math.floor(max(zs)/step)+1):
                    seed=(i*73856093 ^ j*19349663 ^ zlib.crc32(feature["id"].encode())) & 0xffff
                    # A half-cell shift complements the main layer instead of overlapping its grid.
                    x=i*step+(seed%19-9)+step*.23
                    z=j*step+((seed//19)%19-9)+step*.23
                    chunk=tile(x),tile(z)
                    if not self.selected(chunk) or self.tree_counts[chunk]>=85 or inside_starter(x,z,4):
                        continue
                    cell=round(x/18),round(z/18)
                    if cell in self.tree_cells or not inside((x,z),outer):
                        continue
                    if any(inside((x,z),hole) for hole in rings[1:]) or self.roads.inhibits_tree(x,z):
                        continue
                    self.tree_cells.add(cell)
                    self.tree_counts[chunk]+=1
                    self.tree(x,z,seed,chunk)

    def tree(self,x,z,seed,chunk):
        h=5.1+seed%19*.27
        r=h*(.22+seed%7*.014)
        self.upright_box(TRUNK,x,z,.01,h*.67,.15,.15,chunk)
        lobes=[]
        for ring,(dy,scale) in enumerate(((.47,.76),(.75,1))):
            band=[]
            for i in range(6):
                a=i*math.tau/6+seed*.39
                k=r*scale*(.88+((seed>>((i+ring)%10))%7)*.045)
                band.append((x+math.cos(a)*k,h*dy,z+math.sin(a)*k))
            lobes.append(band)
        low,middle=lobes
        for i in range(6):
            k=(i+1)%6
            self.poly(FOLIAGE,[low[i],middle[i],middle[k],low[k]],chunk)
            self.poly(FOLIAGE,[middle[i],(x+.14,h+.28,z-.22),middle[k]],chunk)
            self.poly(FOLIAGE,[(x,h*.39,z),low[i],low[k]],chunk)
        self.count["tree_canopies"] += 1

    def upright_box(self,style,x,z,low,high,w,d,chunk):
        rect=[(x-w,z-d),(x+w,z-d),(x+w,z+d),(x-w,z+d)]
        for a,b in zip(rect,rect[1:]+rect[:1]):
            self.poly(style,[(a[0],low,a[1]),(a[0],high,a[1]),
                             (b[0],high,b[1]),(b[0],low,b[1])],chunk)

    def stops(self, features):
        seen=set()
        for feature,coordinates in geometries(features,"poi","Point"):
            tags=feature["properties"]
            if tags.get("highway")!="bus_stop":
                continue
            x,z=point(coordinates)
            if inside_starter(x,z,4):
                continue
            key=round(x/5),round(z/5)
            if key in seen:
                continue
            seen.add(key)
            nearby=[(segment_distance_sq(x,z,a,b),a,b,width) for a,b,width,is_road,bridge
                    in self.roads.around(x,z) if is_road and not bridge]
            if nearby:
                distance_sq,a,b,width=min(nearby)
                ux,uz,_=unit(a,b)
                shift=width/2+.75-math.sqrt(distance_sq)
                if shift>0:
                    sign = 1 if -uz*(x-a[0])+ux*(z-a[1])>=0 else -1
                    x,z=x-sign*uz*shift,z+sign*ux*shift
            else:
                ux,uz=1,0
            chunk=tile(x),tile(z)
            if not self.selected(chunk):
                continue
            self.upright_box(TRUNK,x,z,0,2.36,.034,.034,chunk)
            def sign(style,lo,hi,r,d):
                a=(x-ux*r-uz*d,z-uz*r+ux*d)
                b=(x+ux*r-uz*d,z+uz*r+ux*d)
                front=[(a[0],lo,a[1]),(a[0],hi,a[1]),(b[0],hi,b[1]),(b[0],lo,b[1])]
                self.poly(style,front if d<0 else front[::-1],chunk)
            for side in (-1,1):
                sign(WINDOW,2.03,2.42,.245,side*.041)
                sign(MARK,2.33,2.38,.157,side*.042)
                sign(MARK,2.10,2.14,.116,side*.042)
            self.count["mapped_bus_stops"] += 1


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input",type=Path)
    parser.add_argument("output",type=Path)
    parser.add_argument("--tile",action="append",type=tile_key,help="test on a single x_z tile")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:])
    args.output.mkdir(parents=True,exist_ok=True)
    with gzip.open(args.input,"rt",encoding="utf-8") as src:
        features=json.load(src)["features"]
    detail=Detail(features,set(args.tile) if args.tile else None)
    detail.buildings(features)
    detail.streets()
    detail.trees(features)
    detail.stops(features)
    print(f"Exported {detail.export_tiles(args.output):,} Omsk detail tiles.",dict(detail.count),flush=True)


if __name__=="__main__":
    main()
