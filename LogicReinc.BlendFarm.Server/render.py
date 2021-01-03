# Script used by LogicReinc.BlendFarm.Server for rendering in Blender
# Assumes usage of structures from said assembly


#Workaround refers to:
# A sad requirement that works around a problem in Blender.
# Blender doesn't properly update before rendering in subsequent tasks in a batch
# It changes both rendering at the node as well as handling of incoming tiles
# It may cause artifacts and inaccuracies. And a newer (or perhaps even older) version of blender may have this fixed.
# Currently enabled by default because 2.91.0 has this issue.



#Start
import bpy
import sys
import json
import time
from multiprocessing import cpu_count


argv = sys.argv
argv = argv[argv.index("--") + 1:]

scn = bpy.data.scenes["Scene"]


jsonPath = argv[0];

def useGPU(type):
    bpy.context.preferences.addons[
        "cycles"
    ].preferences.compute_device_type = type
    bpy.context.preferences.addons["cycles"].preferences.get_devices()
    print(bpy.context.preferences.addons["cycles"].preferences.compute_device_type)
    for d in bpy.context.preferences.addons["cycles"].preferences.devices:
        d["use"] = 1
        print(type + " Device:", d["name"], d["use"]);


#Renders provided settings with id to path
def renderWithSettings(renderSettings, id, path):
        #Dump
        print(json.dumps(renderSettings, indent = 4) + "\n");

        # Parse Parameters
        frame = int(renderSettings["Frame"])

        # Set threading
        scn.render.threads_mode = 'FIXED';
        scn.render.threads = max(cpu_count(), int(renderSettings["Cores"]));
        
        scn.render.tile_x = int(renderSettings["TileWidth"]);
        scn.render.tile_y = int(renderSettings["TileHeight"]);
        
        # Set constraints
        scn.render.use_border = True
        scn.render.use_crop_to_border = renderSettings["Crop"];
        if not renderSettings["Crop"]:
            scn.render.film_transparent = True;

        scn.render.border_min_x = float(renderSettings["X"])
        scn.render.border_max_x = float(renderSettings["X2"])
        scn.render.border_min_y = float(renderSettings["Y"])
        scn.render.border_max_y = float(renderSettings["Y2"])

        #Set Resolution
        scn.render.resolution_x = int(renderSettings["Width"]);
        scn.render.resolution_y = int(renderSettings["Height"]);
        scn.render.resolution_percentage = 100;

        #Set Samples
        scn.cycles.samples = int(renderSettings["Samples"]);

        scn.render.use_persistent_data = True;

        #Render Device
        #TODO: Proper GPU
        renderType = int(renderSettings["ComputeUnit"]);
        if renderType == 0: #CPU
            scn.cycles.device = "CPU";
            print("Use CPU");
        elif renderType == 1: #Cuda
            scn.cycles.device = 'GPU';
            useGPU("CUDA");
            print("Use Cuda");
        elif renderType == 2: #OpenCL
            scn.cycles.device = 'GPU';
            useGPU("OPENCL");
            print("Use OpenCL");
        
        # Set frame
        scn.frame_set(frame)
        
        # Set Output
        scn.render.filepath = path;


        # Render
        print("RENDER_START:" + str(id) + "\n", flush=True);

        bpy.ops.render.render(animation=False, write_still=True, use_viewport=False, layer="", scene = "")

        print("SUCCESS:" + str(id) + "\n", flush=True);






#Main
try:
    print("Json Path:" + jsonPath + "\n");

    # Load Json
    print("Reading Json Config\n");
    jsonFile = open(jsonPath);
    jsonData = jsonFile.read();
    jsonFile.close();

    # Parse Json
    print("Parsing Json Config\n");
    renderSettingsBatch = json.loads(jsonData);

    isFirst = True
        
    scn.render.engine = "CYCLES"
    scn.render.image_settings.file_format = "PNG";

    # Loop over batches
    for i in range(len(renderSettingsBatch)):
        current = renderSettingsBatch[i];
        renderSettings = current;

        output = renderSettings["Output"];
        id = renderSettings["TaskID"];

        #Workaround for scene not updating...
        if not isFirst and renderSettings["Workaround"] and (len(renderSettingsBatch) > 1 and i < len(renderSettingsBatch)):
            previous = renderSettingsBatch[i - 1];
            output = previous["Output"];
            id = previous["TaskID"];
            
        renderWithSettings(renderSettings, id, output);
        
        #Workaround for scene not updating...
        if (renderSettings["Workaround"] and len(renderSettingsBatch) > 1 and i == len(renderSettingsBatch) - 1):
            renderWithSettings(current, current["TaskID"], current["Output"]);

        isFirst = False

    print("BATCH_COMPLETE\n");

except Exception as e:
    print("EXCEPTION:" + str(e));