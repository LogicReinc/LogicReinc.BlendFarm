# Script used by LogicReinc.BlendFarm.Server for information extraction in Blender
# Assumes usage of structures from said assembly

#Start
import bpy
import sys
import json
import time
from multiprocessing import cpu_count

scn = bpy.context.scene

try:
    peekObj = dict(
        RenderWidth = scn.render.resolution_x,
        RenderHeight = scn.render.resolution_y,
        FrameStart = scn.frame_start,
        FrameEnd = scn.frame_end,
        Samples = scn.cycles.samples,
        Cameras = [],
        SelectedCamera = scn.camera.name,
        Scenes = [],
        SelectedScene = scn.name
    )
    for obj in scn.objects:
        if(obj.type == "CAMERAS:"):
            peekObj.Cameras.append(obj.name);
            
    for scene in bpy.data.scenes:
        peekObj["Scenes"].append(scene.name);

    print("SUCCESS:" + json.dumps(peekObj) + "\n");

except Exception as e:
    print("EXCEPTION:" + str(e));