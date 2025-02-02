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

isPre3 = bpy.app.version < (3,0,0);
isPreEeveeRename = bpy.app.version < (4, 2, 0);

if(isPre3):
    print('Detected Blender >= 3.0.0\n');

argv = sys.argv
argv = argv[argv.index("--") + 1:]

scn = bpy.context.scene

jsonPathInitial = argv[0];
useContinue = len(argv) > 1 and argv[1] == 'True';

if(useContinue):
    print('Continuation enabled\n');
    

def useDevices(type, allowGPU, allowCPU):
    cyclesPref = bpy.context.preferences.addons["cycles"].preferences;
    
    #For older Blender Builds
    if (isPre3):
        cyclesPref.compute_device_type = type
        devs = cyclesPref.get_devices()
        cuda_devices, opencl_devices = cyclesPref.get_devices()
        print(cyclesPref.compute_device_type)
        
        devices = None;
        if(type == "CUDA"):
            devices = cuda_devices;
        elif(type == "OPTIX"):
            devices = cuda_devices;
        else:
            devices = opencl_devices;
        for d in devices:
            d.use = (allowCPU and d.type == "CPU") or (allowGPU and d.type != "CPU");
            print(type + " Device:", d["name"], d["use"]);
    #For Blender Builds >= 3.0
    else:
        cyclesPref.compute_device_type = type
        
        print(cyclesPref.compute_device_type)
        
        devices = None;
        if(type == "CUDA"):
            devices = cyclesPref.get_devices_for_type("CUDA");
        elif(type == "OPTIX"):
            devices = cyclesPref.get_devices_for_type("OPTIX");
        elif(type == "HIP"):
            devices = cyclesPref.get_devices_for_type("HIP");
        elif(type == "METAL"):
            devices = cyclesPref.get_devices_for_type("METAL");
        elif(type == "ONEAPI"):
            devices = cyclesPref.get_devices_for_type("ONEAPI");
        else:
            devices = cyclesPref.get_devices_for_type("OPENCL");
        print("Devices Found:", devices);
        if(len(devices) == 0):
            raise Exception("No devices found for type " + type + ", Unsupported hardware or platform?");
        for d in devices:
            d.use = (allowCPU and d.type == "CPU") or (allowGPU and d.type != "CPU");
            print(type + " Device:", d["name"], d["use"]);

#Renders provided settings with id to path
def renderWithSettings(renderSettings, id, path):
        #Dump
        print(json.dumps(renderSettings, indent = 4) + "\n");
        
        global scn;

        scen = renderSettings["Scene"];
        if(scen is None):
            scen = "";
        if(scen != ""):
            print("Rendering specified scene " + scen + "\n");
            scn = bpy.data.scenes[scen];
            if(scn is None):
                raise Exception("Unknown Scene :" + scen);


        # Parse Parameters
        frame = int(renderSettings["Frame"])

        # Set threading
        scn.render.threads_mode = 'FIXED';
        scn.render.threads = max(cpu_count(), int(renderSettings["Cores"]));
        
        if (isPre3):
            scn.render.tile_x = int(renderSettings["TileWidth"]);
            scn.render.tile_y = int(renderSettings["TileHeight"]);
        else:
            print("Blender > 3.0 doesn't support tile size, thus ignored");
        

        # Set constraints
        scn.render.use_border = True
        scn.render.use_crop_to_border = renderSettings["Crop"];
        if not renderSettings["Crop"]:
            scn.render.film_transparent = True;

        scn.render.border_min_x = float(renderSettings["X"])
        scn.render.border_max_x = float(renderSettings["X2"])
        scn.render.border_min_y = float(renderSettings["Y"])
        scn.render.border_max_y = float(renderSettings["Y2"])

        #Set Camera
        camera = renderSettings["Camera"];
        if(camera != None and camera != "" and bpy.data.objects[camera]):
            scn.camera = bpy.data.objects[camera];

        #Set Resolution
        scn.render.resolution_x = int(renderSettings["Width"]);
        scn.render.resolution_y = int(renderSettings["Height"]);
        scn.render.resolution_percentage = 100;

        #Set Samples
        scn.cycles.samples = int(renderSettings["Samples"]);

        scn.render.use_persistent_data = True;

        #Render Device
        renderType = int(renderSettings["ComputeUnit"]);
        engine = int(renderSettings["Engine"]);

        if(engine == 2): #Optix
            optixGPU = renderType == 1 or renderType == 3 or renderType == 11 or renderType == 12; #CUDA or CUDA_GPU_ONLY
            optixCPU = renderType != 3 and renderType != 12; #!CUDA_GPU_ONLY && !OPTIX_GPU_ONLY
            if(optixCPU and not optixGPU):
                scn.cycles.device = "CPU";
            else:
                scn.cycles.device = "GPU";
            useDevices("OPTIX", optixGPU, optixCPU);
        else: #Cycles/Eevee
            if renderType == 0: #CPU
                scn.cycles.device = "CPU";
                print("Use CPU");
            elif renderType == 1: #Cuda
                useDevices("CUDA", True, True);
                scn.cycles.device = "GPU";
                print("Use Cuda");
            elif renderType == 2: #OpenCL
                useDevices("OPENCL", True, True);
                scn.cycles.device = "GPU";
                print("Use OpenCL");
            elif renderType == 3: #Cuda (GPU Only)
                useDevices("CUDA", True, False);
                scn.cycles.device = 'GPU';
                print("Use Cuda (GPU)");
            elif renderType == 4: #OpenCL (GPU Only)
                useDevices("OPENCL", True, False);
                scn.cycles.device = 'GPU';
                print("Use OpenCL (GPU)");
            elif renderType == 5: #HIP
                useDevices("HIP", True, False);
                scn.cycles.device = 'GPU';
                print("Use HIP");
            elif renderType == 6: #HIP (GPU Only)
                useDevices("HIP", True, True);
                scn.cycles.device = 'GPU';
                print("Use HIP (GPU)");
            elif renderType == 7: #METAL
                useDevices("METAL", True, True);
                scn.cycles.device = 'GPU';
                print("Use METAL");
            elif renderType == 8: #METAL (GPU Only)
                useDevices("METAL", True, False);
                scn.cycles.device = 'GPU';
                print("Use METAL (GPU)");
            elif renderType == 9: #ONEAPI
                useDevices("ONEAPI", True, True);
                scn.cycles.device = 'GPU';
                print("Use ONEAPI");
            elif renderType == 10: #ONEAPI (GPU Only)
                useDevices("ONEAPI", True, False);
                scn.cycles.device = 'GPU';
                print("Use ONEAPI (GPU)");
            elif renderType == 11: #OptiX
                useDevices("OPTIX", True, True);
                scn.cycles.device = "GPU";
                print("Use OptiX");
            elif renderType == 12: #OptiX (GPU Only)
                useDevices("OPTIX", True, False);
                scn.cycles.device = "GPU";
                print("Use OptiX (GPU)");
        

        #Denoiser
        denoise = renderSettings["Denoiser"];
        if denoise is not None:
            if denoise == "None":
                scn.cycles.use_denoising = False;
            elif len(denoise) > 0:
                scn.cycles.use_denoising = True;
                scn.cycles.denoiser = denoise;

        fps = renderSettings["FPS"];
        if fps is not None and fps > 0:
            scn.render.fps = fps;

        if(engine == 1): #Eevee
            if(isPreEeveeRename):
                print("Using EEVEE");
                scn.render.engine = "BLENDER_EEVEE";
            else:
                print("Using EEVEE_NEXT");
                scn.render.engine = "BLENDER_EEVEE_NEXT";
        else:
            scn.render.engine = "CYCLES";

        # Set frame
        scn.frame_set(frame)
        
        # Set Output
        scn.render.filepath = path;


        # Render
        print("RENDER_START:" + str(id) + "\n", flush=True);

        bpy.ops.render.render(animation=False, write_still=True, use_viewport=False, layer="", scene = scen)

        print("SUCCESS:" + str(id) + "\n", flush=True);




def runBatch(jsonPath):
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
    

    # Loop over batches
    for i in range(len(renderSettingsBatch)):
        current = renderSettingsBatch[i];
        renderSettings = current;
        
        renderFormat = renderSettings["RenderFormat"];
        if (not renderFormat):
            scn.render.image_settings.file_format = "PNG";
        else:
            scn.render.image_settings.file_format = renderFormat;

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

#Main

try:
    newJsonPath = jsonPathInitial;
    count = 0;
    while newJsonPath.strip():
        if(count > 0):
            print("Continue count: " + str(count));

        runBatch(newJsonPath);
        newJsonPath = "";
        
        if(useContinue):
            print("AWAITING CONTINUE:\n");
            newInput = input("");
            newInput = newInput.strip();
            print("Received:" + newInput + "\n");
            if(newInput):
                newJsonPath = newInput;
                count = count + 1;
            else:
                break;
        
except Exception as e:
    print("EXCEPTION:" + str(e));