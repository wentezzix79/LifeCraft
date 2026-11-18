"""Детализированная 3D-карта островного города для Blender 4.x.

В Blender: Scripting → Open → Run Script. Откройте новую сцену генератора.
Из терминала: blender -b -t 4 -P city_map.py -- /путь/карта.blend
Повторный запуск обновляет отдельную сцену генератора.
"""

import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector, noise
from collections import defaultdict

SEED = 37
SCENE_NAME = "Городская карта · генератор"
rng = random.Random(SEED)

scene = bpy.data.scenes.get(SCENE_NAME)
if scene is None:
    scene = bpy.data.scenes.new(SCENE_NAME)

# Удаляем только объекты и коллекции, созданные этим скриптом.
for group in list(scene.collection.children):
    if group.get("city_map_generated"):
        for obj in list(group.objects):
            data = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if data and data.users == 0:
                bpy.data.batch_remove([data])
        bpy.data.collections.remove(group)

groups = {}
for name in ("Рельеф", "Дороги", "Центр", "Жилые кварталы", "Городские службы",
             "Промзона и порт", "Аэропорт", "Ферма и пригород",
             "Автодром", "Растительность", "Подписи", "Камера и свет"):
    collection = bpy.data.collections.new(name)
    collection["city_map_generated"] = True
    scene.collection.children.link(collection)
    groups[name] = collection

palette = {
    "ocean": (0.016, 0.075, 0.122),
    "shore": (0.28, 0.23, 0.13),
    "grass": (0.16, 0.28, 0.12),
    "park": (0.10, 0.22, 0.11),
    "road": (0.074, 0.090, 0.12),
    "edge": (0.25, 0.27, 0.29),
    "white": (0.82, 0.87, 0.89),
    "gold": (0.90, 0.58, 0.18),
    "glass": (0.13, 0.29, 0.42),
    "glass_dark": (0.072, 0.15, 0.23),
    "concrete": (0.40, 0.45, 0.49),
    "cream": (0.59, 0.46, 0.33),
    "tile": (0.31, 0.10, 0.075),
    "blue": (0.048, 0.21, 0.63),
    "purple": (0.28, 0.085, 0.48),
    "red": (0.66, 0.046, 0.061),
    "soil": (0.36, 0.22, 0.09),
    "crop": (0.49, 0.41, 0.13),
    "foliage": (0.065, 0.22, 0.13),
    "pine": (0.038, 0.14, 0.095),
    "ink": (0.024, 0.036, 0.064),
}
materials = {}
for key, color in palette.items():
    mat = bpy.data.materials.get("CityMap · " + key)
    if mat is None:
        mat = bpy.data.materials.new("CityMap · " + key)
        mat.diffuse_color = (*color, 1)
        mat.use_nodes = True
        shader = mat.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = (*color, 1)
        shader.inputs["Roughness"].default_value = 0.30 if key in {"ocean", "glass"} else 0.73
    materials[key] = mat


def mesh(name, vertices, faces, material, group):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    groups[group].objects.link(obj)
    data.materials.append(materials[material])
    return obj


def prism(name, points, z, height, material, group):
    points = list(points)
    # Верхняя грань ориентирована против часовой стрелки.
    area = sum(x * v - u * y for (x, y), (u, v) in
               zip(points, points[1:] + points[:1]))
    if area < 0:
        points.reverse()
    count = len(points)
    vertices = [(x, y, level) for level in (z, z + height) for x, y in points]
    faces = [tuple(range(count - 1, -1, -1)), tuple(range(count, 2 * count))]
    faces += [(i, (i + 1) % count, (i + 1) % count + count, i + count)
              for i in range(count)]
    return mesh(name, vertices, faces, material, group)


def box(name, x, y, w, d, z, h, material, group, angle=0):
    c, s = math.cos(angle), math.sin(angle)
    points = [(a * c - b * s, a * s + b * c)
              for a, b in [(-w/2, -d/2), (w/2, -d/2), (w/2, d/2), (-w/2, d/2)]]
    obj = prism(name, points, 0, h, material, group)
    obj.location = (x, y, z)
    return obj


def disc(name, x, y, radius, z, h, material, group, sides=24):
    points = [(x + radius * math.cos(i * math.tau / sides),
               y + radius * math.sin(i * math.tau / sides)) for i in range(sides)]
    return prism(name, points, z, h, material, group)


def road(name, path, width=3, z=1.08, markings=False, material="road",
         group="Дороги", closed=False):
    if len(path) > 2:
        smooth = []
        for i, point in enumerate(path):
            if not closed and i in (0, len(path)-1):
                smooth.append(point)
                continue
            center = Vector(point)
            before = Vector(path[i-1]) - center
            after = Vector(path[(i+1)%len(path)]) - center
            rounding = min(width*.65, .24*min(before.length, after.length))
            start = center + before.normalized()*rounding
            end = center + after.normalized()*rounding
            smooth.extend(tuple((1-t)**2*start + 2*t*(1-t)*center + t*t*end)
                          for t in (0,.25,.5,.75,1))
        path = smooth
    # Общие вершины соседних сегментов сохраняют непрерывность покрытия.
    vertices = []
    for i, point in enumerate(path):
        p = Vector(point)
        prev = Vector(path[(i-1) % len(path)]) if closed or i else p
        nxt = Vector(path[(i+1) % len(path)]) if closed or i < len(path)-1 else p
        a = (p - prev).normalized() if p != prev else (nxt - p).normalized()
        b = (nxt - p).normalized() if p != nxt else a
        tangent = (a + b).normalized()
        normal = Vector((-tangent.y, tangent.x))
        offset = normal * (width / 2 / max(0.45, tangent.dot(a)))
        vertices.extend([(p.x + offset.x, p.y + offset.y, z),
                         (p.x - offset.x, p.y - offset.y, z)])
    segments = len(path) if closed else len(path) - 1
    faces = [(2*i, 2*i+1, (2*i+3) % len(vertices), (2*i+2) % len(vertices))
             for i in range(segments)]
    mesh(name, vertices, faces, material, group)
    if markings:
        for i in range(segments):
            a, b = Vector(path[i]), Vector(path[(i+1) % len(path)])
            delta = b - a
            for distance in range(2, int(delta.length)-1, 5):
                p = a + delta.normalized() * distance
                box("Разметка", p.x, p.y, 1.8, 0.15, z + 0.025, 0.018, "gold",
                    group, math.atan2(delta.y, delta.x))


land_outlines = []


def land(name, outline):
    coast = []
    for i, point in enumerate(outline):
        a,b = Vector(point),Vector(outline[(i+1)%len(outline)])
        delta = b-a
        normal = Vector((-delta.y,delta.x)).normalized()
        steps = max(1, math.ceil(delta.length/1.42))
        for j in range(steps):
            t = j/steps
            p = a.lerp(b,t)
            drift = .47*math.sin(math.pi*t)*noise.noise(Vector((p.x*.41,p.y*.41,3.13)))
            coast.append(tuple(p + normal*drift))
    outline = coast
    prism(name + " · берег", outline, -2.4, 3.25, "shore", "Рельеф")
    cx = sum(x for x, _ in outline) / len(outline)
    cy = sum(y for _, y in outline) / len(outline)
    top = [(cx + (x-cx)*0.983, cy + (y-cy)*0.983) for x, y in outline]
    prism(name + " · суша", top, 0.85, 0.15, "grass", "Рельеф")
    land_outlines.append(top)


box("Вода", 0, 0, 1100, 1100, -0.8, 0.8, "ocean", "Рельеф")
land("Городской остров", [
    (-103,-32),(-110,-24),(-105,-12),(-97,0),(-101,12),(-92,22),
    (-81,32),(-70,51),(-55,62),(-45,78),(-28,88),(-11,90),
    (5,84),(17,72),(16,59),(10,46),(19,40),(32,49),(40,63),
    (53,73),(70,74),(86,64),(94,50),(98,33),(105,22),(106,-6),
    (98,-15),(100,-29),(96,-44),(87,-50),(68,-52),(51,-46),
    (35,-43),(18,-49),(1,-49),(-18,-43),(-42,-42),(-66,-39),(-85,-39)])
land("Южный остров", [
    (-107,-48),(-91,-45),(-74,-47),(-58,-52),(-43,-55),(-32,-52),
    (-13,-53),(2,-59),(18,-61),(34,-55),(50,-54),(66,-60),
    (80,-61),(94,-57),(108,-61),(110,-76),(102,-93),(88,-101),
    (67,-101),(52,-93),(34,-94),(19,-101),(1,-100),(-13,-93),
    (-31,-96),(-49,-101),(-68,-105),(-87,-97),(-100,-83),(-109,-68)])

road("Городская магистраль", [(-95,-25),(-75,-33),(-52,-35),(-27,-38),
                             (4,-42),(33,-37),(59,-41),(83,-44),(91,-36),
                             (91,-13),(92,9),(90,31),(83,47)], 4.8, markings=True)
road("Северный проспект", [(-86,13),(-73,30),(-54,42),(-27,42),(-8,43),
                           (9,36),(29,39),(51,43),(67,44),(90,36)], 3.5)
road("Проспект через центр", [(-91,-5),(-68,-8),(-42,-12),(-18,-11),
                             (6,-12),(34,-13),(58,-11),(91,-10)], 4, markings=True)
for x in (-38, -16, 8, 34, 59):
    road("Городская улица", [(x,-38 + (2 if x == 34 else 0)), (x,-12),
                             (x,14),(x,37 if x != -38 else 42)], 2.8)
for y in (4, 20):
    road("Поперечная улица", [(-80,y),(-57,y),(-38,y),(-16,y),(8,y),(34,y),(59,y),(91,y)], 2.4)
for x in (-72, -55):
    road("Жилая улица", [(x,-9),(x,4),(x,20),(x,30),(x+10,38)], 1.9)
road("Южная трасса", [(-100,-68),(-83,-87),(-66,-96),(-48,-93),(-30,-88),
                      (-10,-87),(11,-83),(29,-78),(47,-80),(65,-88),(86,-93),
                      (101,-83)], 4.6, markings=True)

for x, top, bottom, end in [(-43,-36,-58,(-48,-93)), (45,-39,-59,(29,-78))]:
    road("Подъезд к мосту", [(x,top),(x,bottom),end], 4.5, 1.20, True)
    box("Пролёт моста", x, (top+bottom)/2, 4.6, top-bottom, 0.40, 0.80,
        "edge", "Дороги")
    for offset in (-2.24, 2.24):
        box("Ограждение", x+offset, (top+bottom)/2, 0.16, top-bottom,
            1.24, 0.36, "concrete", "Дороги")


def house(x, y, size, group):
    h = rng.uniform(1.8, 3.4)
    w, d = size, size * 0.78
    box("Дом", x, y, w, d, 1.0, h, rng.choice(["cream", "concrete"]), group)
    verts = [(x-w/2-0.2,y-d/2-0.2,1+h), (x+w/2+0.2,y-d/2-0.2,1+h),
             (x+w/2+0.2,y+d/2+0.2,1+h), (x-w/2-0.2,y+d/2+0.2,1+h),
             (x-w/2-0.2,y,2+h), (x+w/2+0.2,y,2+h)]
    mesh("Двускатная крыша", verts, [(0,1,5,4),(4,5,2,3),(0,4,3),(1,2,5)], "tile", group)


for y in (-3, 10, 15, 25, 32):
    for x in (-77,-67,-61,-50,-45):
        if y == 32 and x == -77:
            continue
        house(x, y, 3.7 if y < 20 else 4.4, "Жилые кварталы")
for y in (-80, -72, -66):
    for x in (73, 82, 91, 99):
        if y != -66 or x > 82:
            house(x, y, 3.9, "Ферма и пригород")

for x in (-29, -5, 19, 25, 45):
    for y in (-6, 11, 27, 33):
        g = "Центр"
        h = rng.uniform(6, 14) if x in (-29, 45) else rng.uniform(12, 25)
        w, d = rng.uniform(3.7, 5.0), rng.uniform(3.5, 4.6)
        box("Корпус башни", x, y, w, d, 1, h, rng.choice(["glass","glass_dark"]), g)
        for z in range(3, int(h), 2):
            box("Пояс остекления", x, y, w+0.09, d+0.09, z, 0.23, "concrete", g)
        box("Надстройка", x, y, w*0.64, d*0.64, h+1, 1, "glass_dark", g)
        if (x, y) == (19, 27):
            disc("Шпиль", x, y, 0.17, h+2, 3.2, "concrete", g, 8)


def facility(name, x, y, w, d, h, accent, group):
    box(name + " · площадка", x, y, w+4, d+4, 1, 0.11, "edge", group)
    box(name, x, y, w, d, 1.11, h, "concrete", group)
    box(name + " · крыша", x, y, w+0.35, d+0.35, h+1.11, 0.29, accent, group)
    box(name + " · вход", x, y-d/2, w*0.52, 0.7, 1.14, h*0.70, "glass_dark", group)
    return h + 1.4


facility("Логистический склад", -74, -20, 23, 15, 5.5, "blue", "Промзона и порт")
facility("Полиция", -26, -24, 12, 9, 4, "blue", "Городские службы")
facility("Мэрия", 21, -26, 13, 10, 4.7, "cream", "Городские службы")
hospital = facility("Больница", 73, -20, 17, 11, 5.1, "white", "Городские службы")
for w, d in [(5,1.4),(1.4,5)]:
    box("Красный крест", 73,-20,w,d,hospital,0.05,"red","Городские службы")
facility("Автосалон", 75, -35, 15, 7, 3.4, "purple", "Городские службы")
facility("АЗС", 55, -70, 8, 5, 2.2, "blue", "Городские службы")

for x in (66,78,85):
    for y in (-3,12,28):
        facility("Цех", x, y, 5.5, 6, rng.uniform(2.7,4.6),
                 rng.choice(["edge","tile","concrete"]), "Промзона и порт")
for y in (-8, 5, 16):
    box("Причал", 105,y,13,5,0.7,0.45,"edge","Промзона и порт")
    for i in range(4):
        box("Контейнер", 102+2.1*i,y,1.6,2.5,1.15,0.95,
            rng.choice(["red","gold","blue"]),"Промзона и порт")

track = [(43,50),(39,57),(46,66),(59,69),(76,65),(83,59),(79,51),
         (69,48),(63,55),(55,56),(51,49)]
road("Автодром · обочина", track, 4.1, 1.10, material="edge", group="Автодром", closed=True)
road("Автодром · трасса", track, 3.0, 1.13, group="Автодром", closed=True)
box("Трибуна", 64,62,13,2.7,1,2.8,"concrete","Автодром")

airport = "Аэропорт"
runway = [(-98,-56),(-67,-94)]
road("Взлётно-посадочная полоса", runway, 8.0, 1.09, group=airport)
a, b = map(Vector, runway)
axis = (b-a).normalized()
angle = math.atan2(axis.y, axis.x)
across = Vector((-axis.y, axis.x))
for distance in range(3, int((b-a).length)-2, 4):
    p = a + axis * distance
    box("Осевая разметка ВПП", *p, 2.0, 0.22, 1.10, 0.02, "white", airport, angle)
for endpoint, sign in [(a, 1), (b, -1)]:
    for offset in (-2.7,-1.7,1.7,2.7):
        p = endpoint + sign*axis*2 + across*offset
        box("Порог ВПП", *p, 2.7, 0.35, 1.10, 0.02, "white", airport, angle)
box("Перрон", -66,-68,23,18,1.0,0.08,"edge",airport)
road("Рулёжная дорожка", [(-74,-84),(-69,-78),(-65,-72)], 3.4, 1.10, group=airport)
facility("Терминал", -64,-58,17,5,3.5,"glass",airport)
disc("Диспетчерская вышка", -53,-65,1.05,1,6,"concrete",airport)
disc("Кабина вышки", -53,-65,1.7,6.7,1.5,"glass_dark",airport)
for x, y in [(-72,-68),(-60,-70)]:
    box("Фюзеляж", x,y,1.05,6,1.12,0.62,"white",airport)
    box("Крыло", x,y,6,0.85,1.42,0.12,"white",airport,0.12)
    box("Хвостовое оперение", x,y-2.35,2.7,0.65,1.78,0.12,"blue",airport)

farm = "Ферма и пригород"
for x, y in [(-26,-67),(-12,-68),(-26,-80),(-12,-81),(2,-75)]:
    box("Поле", x,y,12,9,1,0.055,"soil",farm)
    for i in range(9):
        box("Посевы", x-5.2+i*1.3,y,0.58,8.5,1.06,0.09,
            rng.choice(["crop","grass"]),farm)
facility("Амбар", 12,-70,7,5,3,"tile",farm)
disc("Силос", 17,-68,1.25,1,5.6,"concrete",farm)


def tree(x, y, scale):
    g = "Растительность"
    # Ствол и крона объединены в один объект.
    vertices, faces = [], []
    for radius, z in [(0.16,0),(0.16,0.9),(0.85,0.65),(0.55,1.42),(0.025,2.45)]:
        vertices.extend((radius*math.cos(i*math.tau/6)*scale,
                         radius*math.sin(i*math.tau/6)*scale,z*scale) for i in range(6))
    for ring in (0,2,3):
        faces.extend((ring*6+i,ring*6+(i+1)%6,(ring+1)*6+(i+1)%6,(ring+1)*6+i)
                     for i in range(6))
    obj = mesh("Дерево", vertices, faces, rng.choice(["foliage","pine"]), g)
    obj.location = (x,y,1)
    obj.data.materials.append(materials["soil"])
    for face in list(obj.data.polygons)[:6]:
        face.material_index = 1


def on_land(x, y):
    for outline in land_outlines:
        crossings = 0
        for (a, b), (c, d) in zip(outline, outline[1:] + outline[:1]):
            if (b > y) != (d > y) and x < a + (y-b)*(c-a)/(d-b):
                crossings += 1
        if crossings % 2:
            return True
    return False


for cx, cy, rx, ry, n in [(-22,64,29,20,225),(-59,47,10,6,29),
                          (62,76,9,2,20),(-90,2,5,8,22),
                          (65,-53,14,5,37),(88,-58,7,2,12),
                          (81,-94,10,4,23),(-39,-80,3,10,26)]:
    # Выборка внутри эллипса с равномерной плотностью по площади.
    for i in range(n):
        r, angle = math.sqrt(rng.random()), rng.uniform(0, math.tau)
        x, y = cx+rx*r*math.cos(angle), cy+ry*r*math.sin(angle)
        if on_land(x, y):
            tree(x, y, rng.uniform(0.8,1.9))

font = bpy.data.fonts.get("CityMap · подписи")
if font is None:
    candidates = (
        Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
        Path("C:/Windows/Fonts/arial.ttf"),
        Path("/System/Library/Fonts/Supplemental/Arial.ttf"),
    )
    for candidate in candidates:
        if candidate.is_file():
            font = bpy.data.fonts.load(str(candidate))
            font.name = "CityMap · подписи"
            font.pack()
            break


def label(name, fallback, x, y, z=1.29, size=2.2):
    g = "Подписи"
    data = bpy.data.curves.new(name, "FONT")
    data.body = name if font else fallback
    if font:
        data.font = font
    data.size = size
    data.align_x = "CENTER"
    data.align_y = "CENTER"
    data.materials.append(materials["white"])
    obj = bpy.data.objects.new(name, data)
    groups[g].objects.link(obj)
    obj.location = (x,y,z+0.08)
    width = len(data.body) * size * 0.73 + 1.8
    box("Табличка · " + name,x,y,width,size*1.52,z,0.06,"ink",g)


for args in [
    ("ЛЕС", "FOREST", -19,61,6), ("АВТОДРОМ","RACE TRACK",63,45),
    ("ЭЛИТНЫЙ РАЙОН","VILLAS",-61,36,5.9),
    ("ЖИЛОЙ РАЙОН","HOUSING",-60,18,5.8), ("ЦЕНТР","DOWNTOWN",10,0),
    ("ПРОМЗОНА","INDUSTRY",76,17,1.36), ("СКЛАД","WAREHOUSE",-74,-20,7.02),
    ("ПОЛИЦИЯ","POLICE",-26,-31), ("МЭРИЯ","CITY HALL",21,-33),
    ("БОЛЬНИЦА","HOSPITAL",74,-27), ("АВТОСАЛОН","CAR SHOWROOM",75,-40,1.36),
    ("АЭРОПОРТ","AIRPORT",-66,-74), ("ФЕРМА","FARM",-13,-59),
    ("АЗС","FUEL",55,-75), ("ПРИГОРОД","SUBURBS",90,-86),
    ("С","N",104,83,0.28,4.4),
]:
    label(*args)

road("Стрелка севера", [(104,75),(104,80)], 0.46, 0.08, material="white", group="Подписи")
prism("Наконечник", [(102.9,78.8),(105.1,78.8),(104,81.1)], 0.08, 0.05,
      "white", "Подписи")


def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


camera = bpy.data.objects.new("Камера карты", bpy.data.cameras.new("Камера карты"))
groups["Камера и свет"].objects.link(camera)
camera.location = (2, -128, 260)
aim(camera, (2,-6,0))
camera.data.type = "ORTHO"
camera.data.ortho_scale = 246
camera.data.clip_end = 1500
scene.camera = camera

sun = bpy.data.objects.new("Солнце", bpy.data.lights.new("Солнце", "SUN"))
groups["Камера и свет"].objects.link(sun)
sun.location = (-70, -35, 115)
aim(sun, (0,0,0))
sun.data.energy = 2.1
sun.data.angle = math.radians(12)

world = bpy.data.worlds.get("CityMap · небо")
if world is None:
    world = bpy.data.worlds.new("CityMap · небо")
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.52,0.61,0.78,1)
    background.inputs["Strength"].default_value = 0.45
scene.world = world
scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = 1600
scene.render.resolution_y = 1450
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = "AgX"

# Детализация поверхности, фасадов и транспортной инфраструктуры.
detail_rng = random.Random(SEED + 101)


def surface(key, color, roughness=0.72, variation=0, scale=10):
    if key not in materials:
        name = "CityMap · " + key
        materials[key] = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat = materials[key]
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.clear()
    links = mat.node_tree.links
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Roughness"].default_value = roughness
    if variation:
        coordinates = nodes.new("ShaderNodeTexCoord")
        texture = nodes.new("ShaderNodeTexNoise")
        links.new(coordinates.outputs["Object"], texture.inputs["Vector"])
        texture.inputs["Scale"].default_value = scale
        texture.inputs["Detail"].default_value = 4.5
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = 0.20
        ramp.color_ramp.elements[0].color = (*(c*(1-variation) for c in color),1)
        ramp.color_ramp.elements[1].position = 0.82
        ramp.color_ramp.elements[1].color = (*(min(1,c*(1+variation)) for c in color),1)
        links.new(texture.outputs["Fac"], ramp.inputs[0])
        links.new(ramp.outputs[0], shader.inputs["Base Color"])
        bump = nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.24
        bump.inputs["Distance"].default_value = 0.13
        links.new(texture.outputs["Fac"], bump.inputs["Height"])
        links.new(bump.outputs[0], shader.inputs["Normal"])


for values in [
    ("grass", (.12,.17,.052), .89,.58,1.5),
    ("shore", (.21,.15,.091), .88,.52,3),
    ("soil", (.22,.119,.042), .88,.47,5),
    ("crop", (.35,.25,.060), .81,.58,4),
    ("road", (.070,.074,.080), .86,.27,9),
    ("edge", (.23,.22,.20), .79,.29,8),
    ("ocean", (.010,.052,.094), .30,.42,.45),
    ("shallows", (.023,.124,.143), .41,.38,1.2),
    ("sand", (.29,.224,.13), .77,.30,2.4),
    ("asphalt_light", (.105,.109,.109), .82,.30,6),
    ("walk", (.36,.34,.30), .80,.27,8),
    ("facade", (.31,.275,.225), .81,.25,11),
    ("glass_window", (.040,.098,.153), .29,0,1),
    ("glass_reflection", (.20,.35,.43), .22,0,1),
    ("glass_shadow", (.012,.023,.032), .33,0,1),
    ("lit", (.83,.53,.23), .40,0,1),
    ("roof", (.115,.110,.102), .86,.37,9),
    ("pine", (.041,.080,.028), .87,.45,3),
    ("foliage", (.083,.131,.035), .88,.50,4),
    ("leaf_olive", (.13,.15,.043), .86,.40,4),
    ("leaf_dark", (.031,.068,.029), .91,.40,4),
    ("rock", (.20,.184,.15), .87,.58,3),
    ("metal", (.25,.30,.31), .50,0,1),
]:
    surface(*values)
lit_shader = materials["lit"].node_tree.nodes.get("Principled BSDF")
lit_shader.inputs["Emission Color"].default_value = (1,.52,.19,1)
lit_shader.inputs["Emission Strength"].default_value = 0.65

# Формируем береговые полосы с локальным уширением отмелей.
for outline in land_outlines:
    shore_vertices = []
    for i, (x, y) in enumerate(outline):
        p = Vector((x, y))
        prev_dir = (p-Vector(outline[i-1])).normalized()
        next_dir = (Vector(outline[(i+1)%len(outline)])-p).normalized()
        direction = (prev_dir+next_dir).normalized()
        shore_vertices.append((p, Vector((-direction.y,direction.x)) *
                               (1/max(0.5,direction.dot(prev_dir)))))
    for name, inner, outer, level, mat in [
        ("Береговая галька", -.18, 1.6, .32, "sand"),
        ("Прибрежная отмель", 1.10, 4.1, .018, "shallows"),
    ]:
        vertices = []
        for i,(p,normal) in enumerate(shore_vertices):
            variation = 1 + 0.15*math.sin(i*1.63)
            for distance in (inner,outer):
                q = p + normal * distance * variation
                vertices.append((q.x,q.y,level))
        count = len(vertices)
        faces = [(i,i+1,(i+3)%count,(i+2)%count) for i in range(0,count,2)]
        mesh(name,vertices,faces,mat,"Рельеф")


def terrain_height(x, y):
    hill = 0.0
    for cx,cy,rx,ry,height in [(-26,70,29,17,8.5),(-47,60,19,15,3.8),
                               (-8,77,17,10,4.4),(89,-77,19,14,1.5)]:
        distance = ((x-cx)/rx)**2 + ((y-cy)/ry)**2
        hill += height * max(0,1-distance)**2
    shape = noise.fractal(Vector((x*.075,y*.075,1.42)), 0.9, 2.0, 3)
    return 1 + hill*(1 + .27*shape)


def hillside(name, x0, x1, y0, y1):
    coords = [(x,y) for y in range(y0,y1+1) for x in range(x0,x1+1)]
    width = x1-x0+1
    vertices = [(x,y,terrain_height(x,y) + .008) for x,y in coords]
    faces = []
    for i,(x,y) in enumerate(coords):
        if x < x1 and y < y1:
            corners = (i,i+1,i+1+width,i+width)
            if all(on_land(*coords[k]) for k in corners):
                faces.extend([(corners[0],corners[1],corners[2]),
                              (corners[0],corners[2],corners[3])])
    obj = mesh(name, vertices,faces,"grass","Рельеф")
    obj.data.polygons.foreach_set("use_smooth", [True]*len(faces))


hillside("Холмы северного леса", -68,14,44,87)

# Детали с общим назначением объединены в меши по материалу и коллекции.
details = defaultdict(lambda: ([], []))


def detail(name, group, mat, x,y,z,w,d,h,angle=0):
    vertices, faces = details[(name,group,mat)]
    j = len(vertices)
    c,s = math.cos(angle),math.sin(angle)
    vertices.extend((x+a*c-b*s,y+a*s+b*c,z+k)
                    for k in (0,h)
                    for a,b in [(-w/2,-d/2),(w/2,-d/2),(w/2,d/2),(-w/2,d/2)])
    faces.extend(tuple(j+i for i in face) for face in
                 [(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])


def area_pad(x,y,w,d,group):
    detail("Участки и тротуары",group,"walk",x,y,1.004,w,d,.055)


def windows(x,y,z,w,d,h,group, floors, count, lit_chance=.035):
    rows = range(floors)
    for row in rows:
        level = z + (row + .30)*h/floors
        for side in (-1,1):
            for column in range(count):
                alpha = (column+.5)/count-.5
                mat = ("lit" if detail_rng.random() < lit_chance else
                       detail_rng.choice(("glass_window","glass_window","glass_shadow","glass_reflection")))
                detail("Остекление фасадов",group,mat,x+alpha*w,y+side*(d/2+.022),
                       level,w/count*.73,.051,h/floors*.54)
                detail("Остекление фасадов",group,mat,x+side*(w/2+.022),y+alpha*d,
                       level,.051,d/count*.73,h/floors*.54)


def roof_details(x,y,w,d,z,group):
    detail("Вентиляция и оборудование",group,"roof",x-w*.18,y+d*.20,
           z,w*.20,d*.25,.40)
    detail("Вентиляция и оборудование",group,"metal",x+w*.16,y-d*.16,
           z,w*.24,d*.18,.29)


for obj in list(groups["Центр"].objects):
    if not obj.name.startswith("Корпус башни"):
        continue
    x,y,z = obj.location
    w,d,h = obj.dimensions
    area_pad(x,y,w+1.5,d+1.5,"Центр")
    windows(x,y,z,w,d,h,"Центр",max(4,int(h/.87)),4,.08)
    roof_details(x,y,w,d,z+h+1,"Центр")

for group in ("Жилые кварталы","Ферма и пригород"):
    for obj in list(groups[group].objects):
        if not obj.name.startswith("Дом"):
            continue
        x,y,z = obj.location
        w,d,h = obj.dimensions
        area_pad(x,y,w+1.0,d+.9,group)
        windows(x,y,z,w,d,h,group,max(1,int(h/1.4)),2)
        detail("Дымоходы",group,"facade",x+w*.21,y+d*.19,z+h+.47,.32,.34,.82)
        detail("Дорожки к домам",group,"walk",x,y-d/2-.55,1.025,.47,1.2,.045)


def apartment(x,y,w,d,h,group,accent="facade"):
    area_pad(x,y,w+.85,d+.8,group)
    box("Корпус",x,y,w,d,1.06,h,accent,group)
    box("Плоская кровля",x,y,w+.11,d+.11,1.06+h,.19,"roof",group)
    windows(x,y,1.06,w,d,h,group,max(2,int(h/1.05)),max(2,int(w/.96)))
    roof_details(x,y,w,d,h+1.25,group)


for y in (-5,9,15,25,33):
    for x in (-33,-23,-12,1,14,29,39,50,54):
        if min((abs(x-o.location.x) for o in groups["Центр"].objects
                if o.name.startswith("Корпус башни") and abs(y-o.location.y)<4),default=10) < 5:
            continue
        apartment(x,y,3.1,2.9,detail_rng.uniform(3.5,9.2),"Центр",
                  detail_rng.choice(("facade","cream","concrete","glass_dark")))
for y in (-34,-24,-19):
    for x in (-14,-8,-1,32,39,50,55):
        if (x in (32,39) and y == -34) or (x == 55 and y == -19):
            continue
        apartment(x,y,3.2,2.8,detail_rng.uniform(2.7,5.2),"Городские службы")
for y in (-2,8,14,25,31):
    for x in (-83,-74.7,-57.7,-47.7,-41):
        if x < -76 and y > 20:
            continue
        if any((obj.location.xy-Vector((x,y))).length<3.65
               for obj in groups["Жилые кварталы"].objects if obj.name.startswith("Дом")):
            continue
        house(x,y,2.25,"Жилые кварталы")


def car(x,y,angle,group):
    paint = detail_rng.choice(("white","glass_shadow","silver","red","blue","gold"))
    if paint == "silver":
        paint = "metal"
    detail("Автомобили",group,paint,x,y,1.12,.45,.88,.18,angle)
    detail("Автомобили",group,"glass_window",x,y,1.27,.37,.44,.10,angle)
    for dx in (-.155,.155):
        # Световые блоки меняются местами вместе с направлением парковки.
        x1 = x+dx*math.cos(angle)-.41*math.sin(angle)
        y1 = y+dx*math.sin(angle)+.41*math.cos(angle)
        detail("Автомобили",group,"white",x1,y1,1.233,.10,.04,.048,angle)


def parking(x,y,w,group):
    detail("Парковочные площадки",group,"asphalt_light",x,y,1.055,w,3.0,.045)
    for i in range(int(w/.82)):
        px = x-w/2+.45+i*.82
        detail("Парковочные места",group,"white",px-.38,y+.15,1.105,.045,1.33,.012)
        if detail_rng.random()<.63:
            car(px,y+.07,0,group)


for x,y,w,g in [(-76,-30,24,"Промзона и порт"),(-26,-30,10,"Городские службы"),
                (22,-32.5,12,"Городские службы"),(72,-27.5,15,"Городские службы"),
                (74,-39,13,"Городские службы"),(55,-75,9,"Городские службы"),
                (-64,-54.1,15,"Аэропорт"),(78,34,15,"Промзона и порт")]:
    parking(x,y,w,g)

for name, x, y, size, color in [
    ("OZON",-74,-20,3.3,"white"),
    ("27",-94,-61,1.55,"white"),
    ("09",-70,-90,1.55,"white"),
]:
    group = "Промзона и порт" if name == "OZON" else "Аэропорт"
    curve = bpy.data.curves.new("Маркировка " + name,"FONT")
    curve.body = name
    curve.align_x = curve.align_y = "CENTER"
    curve.size = size
    curve.materials.append(materials[color])
    obj = bpy.data.objects.new(curve.name,curve)
    groups[group].objects.link(obj)
    obj.location = (x,y,6.73 if name == "OZON" else 1.128)
    if name != "OZON":
        obj.rotation_euler.z = angle + math.pi/2

for x,y,angle in [(-88,-5,1.57),(-46,-12,1.57),(-15,8,0),(9,25,0),(92,28,0),
                   (89,-24,0),(-60,-34,1.57),(28,-38,1.57),(-43,-46,0),
                   (45,-52,0),(-16,-87,1.57),(48,-81,1.57)]:
    car(x,y,angle,"Дороги")

for g in ("Промзона и порт","Городские службы","Ферма и пригород","Аэропорт"):
    for obj in list(groups[g].objects):
        if obj.type != "MESH" or not any(
            other.name.startswith(obj.name + " · площадка") for other in groups[g].objects
        ):
            continue
        x,y,z = obj.location
        w,d,h = obj.dimensions
        windows(x,y,z,w,d,h,g,max(2,int(h/1.4)),max(3,int(w/1.5)))
        roof_details(x,y,w,d,z+h+.29,g)
        for dx in (-.32,0,.32):
            detail("Входы и ворота",g,"metal",x+w*dx,y-d/2-.35,z,w*.14,.29,h*.58)

# Пристани, складская техника и три портальных крана.
for y in (5,16):
    for x in (101,109):
        detail("Краны", "Промзона и порт","gold",x,y,1.17,.33,.37,7.0)
        detail("Краны", "Промзона и порт","gold",x,y+2.4,1.17,.33,.37,7.0)
        detail("Краны", "Промзона и порт","gold",x,y+1.2,8.17,.35,2.8,.36)
    detail("Краны", "Промзона и порт","gold",108,y+1.2,8.44,15,.35,.38)
    detail("Краны", "Промзона и порт","metal",113,y+1.2,4.8,.09,.09,3.55)
    detail("Краны", "Промзона и порт","blue",113,y+1.2,4.6,1,.57,.34)
for y in (-7.5,-5.6,5.4,16.4):
    for x in (103,105,107,109):
        for j in range(detail_rng.randint(1,3)):
            detail("Контейнерные штабели","Промзона и порт",
                   detail_rng.choice(("red","blue","cream","glass_dark")),
                   x,y,1.15+j*.66,1.8,1.06,.65)

# Объезды, развязка на южном берегу и подъезд к автодрому.
road("Восточная набережная", [(96,26),(97,10),(97,-4),(93,-14)],2.8,1.12)
road("Подъезд к автодрому", [(86,45),(85,49),(81,51)],2.25,1.12)
road("Въезд в пригород", [(65,-88),(73,-85),(85,-83),(96,-81),(103,-76)],2.6,1.14)
road("Съезд с моста", [(45,-58),(45,-65),(48,-71),(53,-76),(61,-82),(68,-89)],3,1.14,True)
road("Кольцевой съезд", [(39,-68),(43,-69),(47,-73),(46,-77),(41,-80),(35,-79),
                          (32,-75),(33,-71)],2.6,1.15,closed=True)
for x in (-43,45):
    center_y = -47 if x == -43 else -51
    for y in (center_y-5,center_y+2):
        detail("Опоры мостов","Дороги","concrete",x,y,-1.0,2.5,.94,1.65)
for points in [[(-68,-63),(-53,-63),(-45,-58)], [(-70,-71),(-79,-68),(-85,-69)]]:
    road("Рулёжные дорожки аэропорта",points,2.4,1.15,group="Аэропорт")
for x,y in [(-72,-68),(-60,-70)]:
    for offset in (-1.48,1.48):
        detail("Мотогондолы","Аэропорт","glass_dark",x+offset,y-.18,1.18,.45,.76,.37)
    detail("Вертикальный стабилизатор","Аэропорт","blue",x,y-2.46,1.68,.13,.64,.57)
for i in range(14):
    p = a + axis * (3+i*((b-a).length-6)/13)
    for side in (-1,1):
        q = p + side*across*3.82
        detail("Огни ВПП","Аэропорт","lit",q.x,q.y,1.13,.12,.12,.09)
for x,y in [(-30,-71),(-17,-76),(-8,-65),(-18,-82)]:
    detail("Сельхозтехника",farm,"red",x,y,1.08,1.02,.55,.29)
    for offset in (-.36,.36):
        detail("Сельхозтехника",farm,"glass_shadow",x,y+offset,1.04,.38,.21,.34)

# Береговые валуны и более густая смешанная растительность.
for outline in land_outlines:
    for p,q in zip(outline, outline[1:]+outline[:1]):
        start,end = Vector(p),Vector(q)
        for i in range(max(1,int((end-start).length/1.9))):
            point = start.lerp(end,detail_rng.random())
            size = detail_rng.uniform(.15,.72)
            if detail_rng.random()<.71:
                detail("Береговые камни","Рельеф","rock",point.x,point.y,.67,
                       size,size*.75,size*.42,detail_rng.uniform(0,math.tau))

for obj in groups["Растительность"].objects:
    obj.location.z = terrain_height(obj.location.x,obj.location.y) if obj.location.y>44 else 1
    obj.rotation_euler.z = detail_rng.uniform(0,math.tau)

# Несколько форм кроны используются как связанные меши для леса и скверов.
def crown_template(index: int):
    mat = ("foliage","pine","leaf_olive","leaf_dark")[index%4]
    radii = ((.04,.68,.86,.62,.08),(.06,.76,.55,.35,.02),
             (.03,.45,.72,.62,.06))[index%3]
    ring_z = (.27,.45,.82,1.25,1.65)
    verts,faces = [],[]
    for ring,(r,z) in enumerate(zip(radii,ring_z)):
        for i in range(8):
            angle = i*math.tau/8 + ring*.21
            rr = r*detail_rng.uniform(.73,1.25)
            verts.append((rr*math.cos(angle),rr*math.sin(angle),z*detail_rng.uniform(.91,1.09)))
    for ring in range(4):
        for i in range(8):
            p = ring*8+i
            q = ring*8+(i+1)%8
            faces.extend([(p,q,q+8),(p,q+8,p+8)])
    faces.append(tuple(range(32,40)))
    data = bpy.data.meshes.new("Крона · " + str(index+1))
    data.from_pydata(verts,[],faces)
    data.materials.append(materials[mat])
    return data


crowns = [crown_template(i) for i in range(12)]


def plant(x,y,scale,z):
    obj = bpy.data.objects.new("Крона",detail_rng.choice(crowns))
    groups["Растительность"].objects.link(obj)
    obj.location = (x,y,z)
    obj.scale = (scale,scale,scale*detail_rng.uniform(.82,1.25))
    obj.rotation_euler.z = detail_rng.uniform(0,math.tau)
    detail("Стволы","Растительность","soil",x,y,z,.106*scale,.106*scale,.63*scale)


for cx,cy,rx,ry,count in [(-26,65,31,19,850),(-60,48,9,6,120),
                            (68,73,15,3,98),(-92,9,6,13,96),(69,-51,17,3,97),
                            (89,-92,12,5,88),(-38,-76,4,13,95),(99,-69,6,4,57)]:
    for _ in range(count):
        r,theta=math.sqrt(detail_rng.random()),detail_rng.uniform(0,math.tau)
        x,y=cx+rx*r*math.cos(theta),cy+ry*r*math.sin(theta)
        if on_land(x,y) and not (-21<x<-13 and 58<y<66):
            plant(x,y,detail_rng.uniform(.47,1.25),terrain_height(x,y) if y>44 else 1)
for x in (-79,-70,-65,-53,-49,-32,-23,-11,4,14,30,40,49,55,65,79,84):
    for y in (-9.2,-15.2,5.9,21.9):
        if on_land(x,y):
            plant(x+detail_rng.uniform(-.22,.22),y,.53,1.04)

for (name,group,mat),(verts,faces) in details.items():
    mesh(name,verts,faces,mat,group)

# Подписи подняты над объектами, при необходимости коллекцию можно скрыть.
for obj in groups["Подписи"].objects:
    if "ЛЕС" in obj.name:
        obj.location.z += 6
    if "ЦЕНТР" in obj.name:
        obj.location.z += 8.5
    if "СКЛАД" in obj.name:
        obj.location.y -= 8.6
        obj.location.z = 2.6 + (.08 if obj.type == "FONT" else 0)
    if "АЭРОПОРТ" in obj.name:
        obj.location.z += 1.2
    if "АЗС" in obj.name or "БОЛЬНИЦА" in obj.name:
        obj.location.z += .4

camera.location = (19,-109,273)
aim(camera,(0,-7,0))
camera.data.ortho_scale = 252
sun.location = (-96,-67,83)
aim(sun,(0,0,0))
sun.data.energy = 2.55
sun.data.angle = math.radians(4.4)
background = scene.world.node_tree.nodes.get("Background")
background.inputs["Color"].default_value = (.52,.65,.85,1)
background.inputs["Strength"].default_value = .56
scene.render.resolution_x = 2100
scene.render.resolution_y = 1900
scene.eevee.taa_render_samples = 48

if bpy.context.window:
    bpy.context.window.scene = scene
    for area in bpy.context.screen.areas:
        if area.type == "VIEW_3D":
            area.spaces.active.region_3d.view_perspective = "CAMERA"
            area.spaces.active.shading.color_type = "MATERIAL"

if __name__ == "__main__" and bpy.app.background and "--" in sys.argv:
    args = sys.argv[sys.argv.index("--")+1:]
    if args:
        bpy.ops.wm.save_as_mainfile(filepath=str(Path(args[0]).expanduser().resolve()))
