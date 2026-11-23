"""Export the three editable workshop pieces as reusable Unity models."""
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "UnityProject/Assets/Factory/Resources"
bpy.ops.wm.open_mainfile(filepath=str(ROOT / "Art/FactoryWorkshop.blend"))
sources = (
    ("Red", "Coral token", "Diamond symbol", (0, -2.57)),
    ("Blue", "Cobalt token", "Circle symbol", (0, -3.91)),
    ("Yellow", "Amber token", "Triangle symbol", (2.04, 1.57)),
)
graph = bpy.context.evaluated_depsgraph_get()
symbols = [obj for obj in bpy.data.objects if obj.type == "CURVE"]
bpy.ops.object.select_all(action="DESELECT")
for route, piece_name, symbol_name, (x, y) in sources:
    pivot = Vector((x, y, .575))
    symbol = next(obj for obj in symbols if obj.name.startswith(symbol_name)
                  and (Vector(obj.data.splines[0].points[0].co[:3]) - pivot).length < .5)
    for part, source in (("Body", bpy.data.objects[piece_name]), ("Mark", symbol)):
        evaluated = source.evaluated_get(graph)
        mesh = bpy.data.meshes.new_from_object(evaluated)
        mesh.transform(source.matrix_world)
        mesh.transform(Matrix.Translation(-pivot))
        copy = bpy.data.objects.new(f"{route}{part}", mesh)
        bpy.context.scene.collection.objects.link(copy)
        copy.select_set(True)

bpy.ops.export_scene.fbx(filepath=str(OUTPUT / "GameplayTokens.fbx"), use_selection=True,
                        object_types={"MESH"}, axis_forward="-Z", axis_up="Y", add_leaf_bones=False)
