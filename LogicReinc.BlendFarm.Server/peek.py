# Script used by LogicReinc.BlendFarm.Server for information extraction in Blender
# Assumes usage of structures from said assembly

#Start
import bpy
import sys
import json
import time
from multiprocessing import cpu_count

argv = sys.argv
argv = argv[argv.index("--") + 1:]

scn = bpy.context.scene

try:
    peekObj = {};

    peekObj.RenderWidth = scn.render.resolution_x;
    peekObj.RenderHeight = scn.render.resolution_y;
    peekObj.FrameStart = scn.frame_start;
    peekObj.FrameEnd = scn.frame_end;
    peekObj.Samples = scn.cycles.samples;

    peekObj.Cameras = [];
    peekObj.SelectedCamera = peekObj.camera.name;
    for obj in scn.objects:
        if(obj.type == "CAMERAS:"):
            peekObj.Cameras.append(obj.name);



    print("SUCCESS:" + json.dumps(peekObj, indent = 4) + "\n");

except Exception as e:
    print("EXCEPTION:" + str(e));