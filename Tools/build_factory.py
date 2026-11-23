"""Build the editable factory diorama, export the meshes and render review views."""
import json
import math
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
MODELS = ROOT / "UnityProject/Assets/Factory/Models"
REVIEW = ROOT / "docs/review"
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
bpy.context.preferences.filepaths.save_version = 0
catalog = {}
geometry = []
group = "Workshop"


def paint(name, hex_color, metal=0, roughness=.42, glow=0):
    rgb = [int(hex_color[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in rgb]
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*linear, 1)
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*linear, 1)
    shader.inputs["Metallic"].default_value = metal
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Emission Color"].default_value = (*linear, 1)
    shader.inputs["Emission Strength"].default_value = glow
    catalog[name] = dict(name=name, hexColor="#" + hex_color, metallic=metal,
                         roughness=roughness, emission=glow)
    return material


ink = paint("Graphite", "19263D", .25)
belt = paint("BeltRubber", "25374F")
tread = paint("BeltTread", "425572")
steel = paint("BlueSteel", "728CA6", .48)
silver = paint("BrushedAluminum", "AFC8D8", .42)
navy = paint("MidnightEnamel", "233A59", .22)
white = paint("Ceramic", "F1F4EF", .08, .29)
black = paint("VisorGlass", "081626", .35, .21)
gold = paint("SafetyAmber", "FFAD37", .16, .35)
amber = paint("AmberSignal", "FFC164", 0, .28, 1.3)
cyan = paint("CyanSignal", "49E2F9", 0, .28, 1.2)
red = paint("Vermilion", "E64052", .21, .32)
blue = paint("Cobalt", "197AD5", .23, .31)
yellow = paint("Sunflower", "F7B42E", .22, .31)
pink = paint("CoralSignal", "FF776A", 0, .29, 1.15)
tile = [paint("Floor" + str(i), value, .1, .65)
        for i, value in enumerate(("35485F", "3D5068", "40546B", "394B63"))]


def modeled_part(name, location, material, create, **parameters):
    data = bpy.data.meshes.new(name)
    mesh = bmesh.new()
    create(mesh, **parameters)
    mesh.to_mesh(data)
    mesh.free()
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = location
    obj.data.materials.append(material)
    geometry.append((group, obj))
    return obj


def bevel(obj, amount, smooth=False):
    if amount:
        mod = obj.modifiers.new("Machined edges", "BEVEL")
        mod.width, mod.segments = amount, 3
        mod.harden_normals = True
        normal = obj.modifiers.new("Weighted normals", "WEIGHTED_NORMAL")
        normal.keep_sharp = True
    if smooth:
        for face in obj.data.polygons:
            face.use_smooth = True
    return obj


def box(name, location, size, mat, radius=.06, angle=0):
    transform = Matrix.Diagonal((*size, 1))
    obj = modeled_part(name, location, mat, bmesh.ops.create_cube, size=1, matrix=transform)
    obj.rotation_euler.z = angle
    return bevel(obj, radius)


def sphere(name, pos, size, mat):
    transform = Matrix.Diagonal((*size, 1))
    obj = modeled_part(name, pos, mat, bmesh.ops.create_uvsphere, u_segments=24,
                       v_segments=12, radius=1, matrix=transform)
    return bevel(obj, 0, True)


def rod(name, start, end, radius, mat, sides=20):
    a, b = Vector(start), Vector(end)
    obj = modeled_part(name, (a + b) * .5, mat, bmesh.ops.create_cone,
                       cap_ends=True, cap_tris=False, segments=sides,
                       radius1=radius, radius2=radius, depth=(b - a).length)
    obj.rotation_euler = (b - a).to_track_quat("Z", "Y").to_euler()
    return bevel(obj, min(.035, radius * .23), sides > 8)


def tube(name, coordinates, radius, mat):
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth, curve.bevel_resolution = radius, 3
    curve.use_fill_caps = True
    spline = curve.splines.new("POLY")
    spline.points.add(len(coordinates) - 1)
    for point, co in zip(spline.points, coordinates):
        point.co = (*co, 1)
    obj = bpy.data.objects.new(name, curve)
    scene.collection.objects.link(obj)
    curve.materials.append(mat)
    geometry.append((group, obj))


def lettering(body, pos, size, mat):
    label = bpy.data.curves.new(body, "FONT")
    label.body, label.size, label.align_x, label.align_y = body, size, "CENTER", "CENTER"
    label.extrude = .001
    obj = bpy.data.objects.new(body, label)
    scene.collection.objects.link(obj)
    label.materials.append(mat)
    obj.location, obj.rotation_euler.x = pos, math.pi / 2
    geometry.append((group, obj))


def emblem(kind, pos, size, mat, horizontal=False):
    shapes = {
        "diamond": [(0, 1), (1, 0), (0, -1), (-1, 0), (0, 1)],
        "circle": [(math.sin(i * math.tau / 24), math.cos(i * math.tau / 24)) for i in range(25)],
        "triangle": [(0, 1), (.98, -.74), (-.98, -.74), (0, 1)],
    }
    x, y, z = pos
    coords = [(x + u * size, y + v * size, z) if horizontal
              else (x + u * size, y, z + v * size) for u, v in shapes[kind]]
    tube(kind.title() + " symbol", coords, size * .1, mat)


box("Foundation", (0, .1, -.3), (11.7, 13, .5), ink, .22)
for row in range(9):
    for col in range(8):
        box("Inset floor panel", ((col - 3.5) * 1.43, (row - 4) * 1.4 + .1, -.042),
            (1.41, 1.38, .12), tile[(row * 3 + col * 7) % 4], .027)
for x in (-5.18, 5.18):
    box("Safety lane", (x, -.2, .026), (.042, 11, .018), gold, .006)
    for i in range(10):
        box("Lane marker", (x * .94, i * 1.04 - 4.9, .027), (.055, .32, .019), gold, .006, -.38)

box("Back service wall", (0, 5.84, 1.45), (11.5, .42, 3.05), navy, .13)
box("Wall cap", (0, 5.62, 3.01), (11.46, .62, .16), steel)
box("Lower wall bumper", (0, 5.59, .25), (11.44, .23, .22), ink)
for x in (-4.85, -2.48, 0, 2.48, 4.85):
    box("Wall rib", (x, 5.55, 1.55), (.19, .28, 2.8), steel)
for x in (-3.69, 3.69):
    box("Service window surround", (x, 5.52, 2.1), (1.9, .19, 1.12), ink)
    box("Service window", (x, 5.4, 2.1), (1.73, .09, .94), blue)
    for i in (-.54, 0, .54):
        box("Window glazing", (x + i, 5.34, 2.1), (.46, .04, .77), steel, .02)
    box("Work light", (x, 5.37, 2.79), (1.31, .14, .095), amber, .036)
box("Factory sign frame", (0, 5.42, 2.22), (3.5, .26, 1.19), steel, .14)
box("Factory sign inset", (0, 5.25, 2.22), (3.29, .11, .97), ink, .1)
lettering("PUZZLE FACTORY", (0, 5.18, 2.38), .247, white)
lettering("SORTING  /  WORKSHOP", (0, 5.18, 2.01), .133, cyan)
for x in (-1.46, 1.46):
    rod("Sign fastener", (x, 5.19, 2.24), (x, 5.14, 2.24), .051, gold, 8)

for s in (-1, 1):
    x = 4.84 * s
    tube("Wall utility feed", [(x, 5.31, .28), (x, 5.31, 1.12),
         (x - .08 * s, 5.31, 1.28), (x - .26 * s, 5.31, 1.34),
         (s * 2.08, 5.31, 1.34)], .097, gold)
    for y in (.7, 1.85, 3):
        box("Guardrail plinth", (x, y, .12), (.35, .34, .17), ink)
        rod("Guardrail stanchion", (x, y, .13), (x, y, .88), .073, gold)
    for z in (.48, .88):
        rod("Guardrail", (x, .65, z), (x, 3.05, z), .048, gold)


def machine(x, shell, signal, kind, name):
    global group
    group = name + " sorter"
    y = 4.32
    box("Foundation shoe", (x, y, .16), (2.02, 1.8, .25), ink, .13)
    box("Upper enamel housing", (x, y, 1.7), (1.88, 1.63, 1.36), shell, .2)
    box("Crown inset", (x, y, 2.37), (1.41, 1.16, .15), shell, .075)
    box("Tunnel back", (x, y + .61, .85), (1.44, .22, 1.27), ink)
    box("Tunnel canopy", (x, y - .03, 1.13), (1.31, 1.56, .18), ink)
    box("Tunnel bed", (x, y, .44), (1.28, 1.5, .16), belt)
    for side in (-1, 1):
        post = x + side * .72
        box("Tunnel column", (post, y, .78), (.43, 1.53, 1.24), shell, .105)
        box("Intake light", (x + side * .54, 3.48, .81), (.065, .07, .62), signal, .031)
        box("Foot trim", (post, 3.49, .28), (.28, .13, .12), steel)
    box("Intake cross light", (x, 3.48, 1.155), (1.145, .07, .071), signal, .031)
    box("Instrument bezel", (x, 3.482, 1.78), (1.35, .15, .81), steel, .12)
    box("Instrument inset", (x, 3.39, 1.78), (1.20, .08, .68), ink, .09)
    emblem(kind, (x, 3.335, 1.84), .213, white)
    lettering(name.upper() + "  SORT", (x, 3.335, 1.553), .098, signal)
    rod("Beacon mount", (x, y, 2.37), (x, y, 2.54), .31, ink)
    rod("Beacon bezel", (x, y, 2.53), (x, y, 2.62), .257, silver)
    rod("Beacon lens", (x, y, 2.61), (x, y, 2.88), .176, signal, 12)
    rod("Beacon cap", (x, y, 2.87), (x, y, 2.92), .19, shell, 12)
    box("Heat sink plate", (x + .82, y, 1.54), (.3, .91, .58), ink, .08)
    for i in range(5):
        box("Heat sink fin", (x + .997, y + (i - 2) * .136, 1.54), (.062, .067, .36), steel, .017)
    tube("Coolant elbow", [(x + .72, y + .42, 1.91), (x + .99, y + .42, 1.91),
         (x + 1.13, y + .42, 1.85), (x + 1.19, y + .42, 1.71),
         (x + 1.19, y + .42, .35)], .113, silver)
    for z in (.45, .84, 1.38):
        rod("Coolant collar", (x + 1.19, y + .42, z), (x + 1.19, y + .42, z + .09), .157, gold)
    for side in (-1, 1):
        for z in (1.37, 2.17):
            rod("Front screw", (x + side * .78, 3.5, z), (x + side * .78, 3.445, z),
                .041, silver, 8)


machine(-3.15, red, pink, "diamond", "Red")
machine(0, blue, cyan, "circle", "Blue")
machine(3.15, yellow, amber, "triangle", "Yellow")


def alignment(path, i):
    point = Vector(path[i])
    tangent = (Vector(path[min(i + 1, len(path) - 1)]) - Vector(path[max(i - 1, 0)])).normalized()
    direction = math.atan2(-tangent.x, tangent.y)
    return point, Vector((tangent.y, -tangent.x)), direction


def conveyor(name, path, width):
    global group
    group = name
    for i in range(len(path) - 1):
        a, b = Vector(path[i]), Vector(path[i + 1])
        middle = (a + b) * .5
        tangent = (b - a).normalized()
        angle = math.atan2(-tangent.x, tangent.y)
        cross = Vector((tangent.y, -tangent.x))
        step = (b - a).length
        box("Rubber conveyor slat", (*middle, .533), (width, step - .014, .075),
            tread if i % 3 == 0 else belt, .018, angle)
        box("Slat support", (*middle, .435), (width + .06, step + .035, .138), ink, .025, angle)
        for s in (-1, 1):
            edge = middle + s * cross * (width * .5 + .036)
            box("Rounded rail panel", (*edge, .515), (.079, step + .073, .205), steel, .031, angle)
            if i % 6 == 2:
                box("Amber rail marker", (*edge, .631), (.085, min(.18, step), .035), gold, .012, angle)
                outer = edge + s * cross * .06
                rod("Rail rivet", (*edge, .52), (*outer, .52), .052, silver, 12)
        if i % 6 == 1:
            for s in (-1, 1):
                leg = middle + s * cross * (width * .36)
                box("Pedestal foot", (*leg, .082), (.29, .32, .12), ink, .037, angle)
                box("Conveyor pedestal", (*leg, .245), (.17, .19, .33), steel, .035, angle)
    for i in (0, len(path) - 1):
        end, cross, _ = alignment(path, i)
        rod("End drum", (*(end - cross * (width * .5 + .02)), .499),
            (*(end + cross * (width * .5 + .02)), .499), .085, silver)


def line(x0, y0, x1, y1, count, ease=False):
    points = []
    for i in range(count + 1):
        t = i / count
        u = t * t * (3 - 2 * t) if ease else t
        points.append((x0 + (x1 - x0) * u, y0 + (y1 - y0) * t))
    return points


conveyor("Infeed conveyor", line(0, -5.04, 0, -1.68, 16), 1.05)
conveyor("Center conveyor", line(0, -.69, 0, 3.74, 21), .94)
conveyor("Left branch conveyor", line(-.72, -1.04, -3.15, 3.76, 23, True), .89)
conveyor("Right branch conveyor", line(.72, -1.04, 3.15, 3.76, 23, True), .89)

group = "Three-way diverter"
box("Diverter skid", (0, -1.2, .18), (2.77, 1.22, .28), ink, .16)
box("Cast diverter housing", (0, -1.2, .38), (2.63, 1.16, .29), gold, .16)
box("Diverter routing deck", (0, -1.19, .524), (2.44, 1.055, .11), belt, .15)
rod("Rotary routing plate", (0, -1.19, .573), (0, -1.19, .595), .39, steel, 32)
for s in (-1, 0, 1):
    start = (s * .24, -1.45, .608)
    end = (s * .73, -.85, .608)
    tube("White routing arrow", [start, end], .023, white)
    tube("Arrow tip", [(end[0] - .112, end[1] - .073, .608), end,
                      (end[0] + .06, end[1] - .145, .608)], .023, white)
box("Diverter operator panel", (1.23, -1.6, .57), (.42, .39, .28), navy, .075)
rod("Illuminated diverter button", (1.23, -1.6, .712), (1.23, -1.6, .778), .126, cyan)
for x in (-1.15, 1.15):
    rod("Junction fastener", (x, -1.34, .57), (x, -1.34, .619), .06, silver, 8)


def piece(name, x, y, mat, kind):
    global group
    group = "Puzzle pieces"
    if kind == "diamond":
        box(name, (x, y, .78), (.36, .36, .38), mat, .057, math.pi / 4)
    else:
        rod(name, (x, y, .575), (x, y, .974), .254, mat, 3 if kind == "triangle" else 20)
    emblem(kind, (x, y, .978), .123, white, horizontal=True)


piece("Cobalt token", 0, -3.91, blue, "circle")
piece("Coral token", 0, -2.57, red, "diamond")
piece("Coral token", -2.38, 1.98, red, "diamond")
piece("Cobalt token", 0, 1.22, blue, "circle")
piece("Amber token", 2.04, 1.57, yellow, "triangle")
piece("Amber token", 2.96, 2.94, yellow, "triangle")


def robot(x, y):
    global group
    group = "Helper robot"
    def p(dx, dy, z):
        return (x + dx, y + dy, z)

    rod("Service pad", p(0, 0, .023), p(0, 0, .109), .85, navy, 32)
    rod("Pad trim", p(0, 0, .096), p(0, 0, .132), .77, gold, 32)
    rod("Pad tread", p(0, 0, .128), p(0, 0, .147), .695, ink, 32)
    for s in (-1, 1):
        box("Boot sole", p(s * .24, -.09, .223), (.36, .56, .16), ink, .08)
        box("Armored boot", p(s * .24, -.09, .32), (.33, .5, .23), gold, .1)
        rod("Ankle piston", p(s * .24, 0, .33), p(s * .20, 0, .61), .11, steel)
        sphere("Knee shell", p(s * .22, -.028, .52), (.153, .142, .164), white)
    sphere("Undersuit", p(0, 0, .79), (.408, .287, .355), ink)
    box("Ceramic torso armor", p(0, -.073, .83), (.706, .54, .58), white, .225)
    box("Chest badge", p(0, -.351, .88), (.24, .047, .203), gold, .065)
    box("Badge status", p(0, -.381, .88), (.10, .018, .047), cyan, .019)
    rod("Head swivel", p(0, 0, 1.02), p(0, 0, 1.23), .18, steel)
    box("Ceramic head", p(0, -.012, 1.46), (1.09, .79, .63), white, .265)
    box("Visor seal", p(0, -.389, 1.455), (.97, .12, .477), steel, .195)
    box("Curved visor", p(0, -.446, 1.455), (.897, .09, .404), black, .167)
    for s in (-1, 1):
        coords = [p(s * .205 + .091 * math.cos(i * math.pi / 14), -.5,
                    1.45 + .098 * math.sin(i * math.pi / 14)) for i in range(15)]
        tube("Smiling LED eye", coords, .022, cyan)
        rod("Earphone isolation ring", p(s * .45, .015, 1.47),
            p(s * .58, .015, 1.47), .18, ink)
        rod("Earphone enamel shell", p(s * .54, .015, 1.47),
            p(s * .64, .015, 1.47), .143, gold)
        rod("Earphone inlay", p(s * .63, .015, 1.47),
            p(s * .66, .015, 1.47), .089, steel)
    sphere("Hard hat dome", p(0, .02, 1.717), (.515, .405, .299), gold)
    box("Hard hat brim", p(0, -.06, 1.71), (1.139, .93, .085), yellow, .04)
    tube("Hard hat raised seam", [p(0, .019 + .401 * math.cos(i * math.pi / 24),
         1.719 + .298 * math.sin(i * math.pi / 24)) for i in range(25)], .035, amber)
    for s in (-1, 1):
        shoulder = p(s * .4, 0, .985)
        elbow = p(s * .55, -.04, .73 if s < 0 else .87)
        palm = p(s * .64, -.28, .61 if s < 0 else 1.04)
        sphere("Shoulder cap", shoulder, (.184, .177, .18), gold)
        rod("Arm core", shoulder, elbow, .107, ink)
        sphere("Elbow joint", elbow, (.105, .105, .107), steel)
        rod("Forearm cuff", elbow, palm, .121, white)
        sphere("Padded glove", palm, (.133, .124, .133), navy)
        for i in range(3):
            sphere("Glove finger", (palm[0] + (i - 1) * .061, palm[1] - .071,
                   palm[2] + (.08 if s > 0 else -.065)), (.034, .068, .064), ink)


robot(-2.2, -3.12)
group = "Upgrade arm display"
x, y = 3.37, -2.46
box("Service platform", (x, y, .185), (1.65, 1.48, .28), navy, .14)
box("Service platform lip", (x, y, .325), (1.47, 1.29, .10), steel, .065)
for i in range(5):
    box("Platform warning inset", (x + (i - 2) * .19, y - .661, .326),
        (.088, .076, .041), gold, .008, -.4)
rod("Arm turntable", (x, y, .36), (x, y, .59), .39, ink)
rod("Arm rotation housing", (x, y, .57), (x, y, .75), .302, gold)
joints = [(x, y, .719), (x + .31, y, 1.47), (x - .36, y - .1, 1.77),
          (x - .71, y - .18, 1.4)]
for a, b in zip(joints, joints[1:]):
    rod("Arm structural link", a, b, .153, gold)
for jx, jy, jz in joints[:-1]:
    rod("Arm joint shell", (jx, jy - .196, jz), (jx, jy + .183, jz), .209, navy)
    rod("Arm bushing", (jx, jy - .227, jz), (jx, jy - .187, jz), .104, amber)
gx, gy, gz = joints[-1]
for s in (-1, 1):
    tube("Gripper jaw", [(gx, gy, gz), (gx + s * .15, gy, gz - .084),
         (gx + s * .14, gy, gz - .22), (gx + s * .075, gy, gz - .27)], .055, ink)

group = "Workshop props"
for index, (x, y) in enumerate(((-4.36, -1.55), (-4.27, -.72), (4.32, -.49), (4.31, .25))):
    box("Stackable cargo case", (x, y, .33), (.65, .61, .55), navy, .078)
    box("Inset cargo lid", (x, y, .633), (.55, .52, .10), gold if index % 2 else steel, .044)
    for side in (-1, 1):
        box("Cargo reinforcement", (x + side * .224, y - .316, .345), (.08, .04, .40), steel, .014)
    box("Cargo handle", (x, y - .326, .416), (.217, .044, .083), ink, .023)


def view(name, position, target, scale):
    bpy.ops.object.camera_add(location=position)
    camera = bpy.context.object
    camera.name = name
    camera.rotation_euler = (Vector(target) - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type, camera.data.ortho_scale = "ORTHO", scale
    camera.data.lens = 50
    return camera


def area(name, location, target, power, color, width):
    bpy.ops.object.light_add(type="AREA", location=location)
    obj = bpy.context.object
    obj.name = name
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()
    obj.data.energy, obj.data.color, obj.data.shape, obj.data.size = power, color, "DISK", width


composite = view("Portrait review camera", (6.5, -16, 15), (0, .45, .85), 15.3)
detail = view("Robot and routing detail camera", (1.3, -9.5, 5.1), (-.73, -2.46, .82), 6.4)
area("Warm key", (-4.6, -4.4, 9), (0, .3, .4), 2200, (1, .79, .61), 6.5)
area("Cool fill", (5.1, -1.75, 6.1), (0, 1, 1), 1670, (.57, .78, 1), 6)
area("Warm rim", (1.3, 6.4, 7), (0, 2.5, 1), 2480, (1, .69, .43), 5)
scene.world = bpy.data.worlds.new("Workshop environment")
scene.world.use_nodes = True
background = scene.world.node_tree.nodes.get("Background")
background.inputs["Color"].default_value = (.105, .145, .22, 1)
background.inputs["Strength"].default_value = .55
scene.camera = composite
scene.render.engine = "CYCLES"
scene.cycles.samples = 20
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = 860, 1080
scene.render.image_settings.file_format = "PNG"
scene.view_settings.view_transform = "AgX"

# The .blend retains individual modeled parts. The FBX combines static mesh parts by
# assembly and material to reduce renderer and material overhead in Unity.
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "Art/FactoryWorkshop.blend"))
export_objects = []
buckets = {}
depsgraph = bpy.context.evaluated_depsgraph_get()
for assembly, source in geometry:
    evaluated = source.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(evaluated)
    material = mesh.materials[0]
    mesh.transform(source.matrix_world)
    copy = bpy.data.objects.new(source.name + " export", mesh)
    scene.collection.objects.link(copy)
    buckets.setdefault((assembly, material.name), []).append(copy)

for (assembly, name), parts in buckets.items():
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    combined = bpy.context.object
    combined.name = assembly + "__" + name
    export_objects.append(combined)

bpy.ops.object.select_all(action="DESELECT")
for combined in export_objects:
    combined.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(MODELS / "FactoryWorkshop.fbx"), use_selection=True,
                        object_types={"MESH"}, axis_forward="-Z", axis_up="Y", add_leaf_bones=False)
(MODELS / "FactoryPalette.json").write_text(
    json.dumps({"materials": list(catalog.values())}, indent=2) + "\n", encoding="utf-8")
print(f"Factory export: {len(geometry)} source parts, {len(export_objects)} material batches")

bpy.ops.wm.open_mainfile(filepath=str(ROOT / "Art/FactoryWorkshop.blend"))
scene = bpy.context.scene
for name, camera, width, height in (
    ("blender-workshop", "Portrait review camera", 860, 1080),
    ("blender-details", "Robot and routing detail camera", 950, 720),
):
    scene.camera = bpy.data.objects[camera]
    scene.render.resolution_x, scene.render.resolution_y = width, height
    scene.render.filepath = str(REVIEW / (name + ".png"))
    bpy.ops.render.render(write_still=True)
