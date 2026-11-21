"""Anime-style preview of a built astronaut: flat toon look (Shader to RGB ramp) and Freestyle outlines."""
import bpy, math
from mathutils import Vector

def toonify():
    for m in bpy.data.materials:
        if not m.use_nodes: continue
        nt = m.node_tree
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        out = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None)
        if bsdf is None or out is None or m.name in ("Visor",): continue
        if bsdf.inputs["Emission Strength"].default_value > 0.5: continue   # lights stay lit
        tex = next((l.from_node for l in nt.links if l.to_socket == bsdf.inputs["Base Color"]), None)
        diff = nt.nodes.new("ShaderNodeBsdfDiffuse")
        s2r = nt.nodes.new("ShaderNodeShaderToRGB")
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.interpolation = "CONSTANT"
        ramp.color_ramp.elements[0].position = 0.0; ramp.color_ramp.elements[0].color = (0.72, 0.68, 0.82, 1)
        ramp.color_ramp.elements[1].position = 0.12; ramp.color_ramp.elements[1].color = (1, 1, 1, 1)
        mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"; mix.blend_type = "MULTIPLY"; mix.inputs[0].default_value = 1.0
        emit = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(diff.outputs[0], s2r.inputs[0]); nt.links.new(s2r.outputs[0], ramp.inputs[0])
        if tex is not None: nt.links.new(tex.outputs[0], mix.inputs[6])
        else: mix.inputs[6].default_value = bsdf.inputs["Base Color"].default_value
        nt.links.new(ramp.outputs[0], mix.inputs[7]); nt.links.new(mix.outputs[2], emit.inputs[0])
        alpha = next((l.from_socket for l in nt.links if l.to_socket == bsdf.inputs["Alpha"]), None)
        if alpha is not None:
            tr = nt.nodes.new("ShaderNodeBsdfTransparent"); mx = nt.nodes.new("ShaderNodeMixShader")
            nt.links.new(alpha, mx.inputs[0]); nt.links.new(tr.outputs[0], mx.inputs[1]); nt.links.new(emit.outputs[0], mx.inputs[2])
            nt.links.new(mx.outputs[0], out.inputs[0])
        else:
            nt.links.new(emit.outputs[0], out.inputs[0])

def stage(FWD, bg=(0.93, 0.88, 0.95)):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"; sc.view_settings.view_transform = "Standard"
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    b = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND"); b.inputs["Color"].default_value = (*bg, 1); b.inputs["Strength"].default_value = 1.0
    l = bpy.data.lights.new("k", "SUN"); l.energy = 3.0; lo = bpy.data.objects.new("k", l); sc.collection.objects.link(lo)
    lo.rotation_euler = (math.radians(50), 0, math.radians(30 if FWD > 0 else 210))
    sc.render.use_freestyle = True; sc.render.line_thickness_mode = "ABSOLUTE"; sc.render.line_thickness = 1.4
    ls = sc.view_layers[0].freestyle_settings.linesets[0] if sc.view_layers[0].freestyle_settings.linesets else sc.view_layers[0].freestyle_settings.linesets.new("L")
    ls.linestyle.color = (0.25, 0.2, 0.3)
    cd = bpy.data.cameras.new("c"); cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    def shot(loc, tgt, lens, path, w=1000, h=1000, transparent=False):
        sc.render.resolution_x, sc.render.resolution_y = w, h; sc.render.film_transparent = transparent
        cam.location = Vector(loc); cd.lens = lens; cam.rotation_euler = (Vector(tgt) - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = path; bpy.ops.render.render(write_still=True)
    return shot
