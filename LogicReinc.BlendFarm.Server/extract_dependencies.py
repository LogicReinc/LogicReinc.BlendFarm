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

    print("Files:\n");
    for img in bpy.data.images:
        if(not img.packed_file and img.filepath):
            imgObj = dict(
                Type = "Image",
                Name = img.name,
                Path = bpy.path.abspath(img.filepath)
            );
            print("SUCCESS:" + json.dumps(imgObj) + "\n");




except Exception as e:
    print("EXCEPTION:" + str(e));