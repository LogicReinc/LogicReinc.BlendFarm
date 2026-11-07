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

if(isPre3):
    print('Detected Blender < 3.0.0\n');

argv = sys.argv
argv = argv[argv.index("--") + 1:]

scn = bpy.context.scene
#The file's own scene, used when a job doesn't specify one (the process may be reused across jobs)
defaultSceneName = scn.name

#Per scene, the values from the file for settings a job only sometimes overrides.
#Restored before every job so "inherit" means the file's value, not the previous job's.
sceneDefaults = {}

def snapshotScene(s):
    if s.name in sceneDefaults:
        return;
    d = dict(
        camera = s.camera,
        film_transparent = s.render.film_transparent,
        fps = s.render.fps
    );
    try:
        d["use_denoising"] = s.cycles.use_denoising;
        d["denoiser"] = s.cycles.denoiser;
    except Exception:
        pass;
    sceneDefaults[s.name] = d;

def restoreScene(s):
    d = sceneDefaults.get(s.name);
    if d is None:
        snapshotScene(s);
        return;
    s.camera = d["camera"];
    s.render.film_transparent = d["film_transparent"];
    s.render.fps = d["fps"];
    if "use_denoising" in d:
        s.cycles.use_denoising = d["use_denoising"];
        s.cycles.denoiser = d["denoiser"];

snapshotScene(scn);

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
            selectedScene = bpy.data.scenes.get(scen);
            if(selectedScene is None):
                raise Exception("Unknown Scene :" + scen);
        else:
            #No scene specified, use the file's scene (not whatever a previous job selected)
            selectedScene = bpy.data.scenes.get(defaultSceneName) or scn;
        if(selectedScene != scn):
            scn = selectedScene;
            if(bpy.context.window is not None):
                bpy.context.window.scene = scn;
            bpy.context.view_layer.update();
        scen = scn.name;

        #Undo overrides of previous jobs in this process
        restoreScene(scn);

        print("Using scene " + scn.name + "\n");

        renderFormat = renderSettings["RenderFormat"];
        if (not renderFormat):
            scn.render.image_settings.file_format = "PNG";
        else:
            scn.render.image_settings.file_format = renderFormat;


        # Parse Parameters
        frame = int(renderSettings["Frame"])

        # Set threading
        scn.render.threads_mode = 'FIXED';
        cores = int(renderSettings["Cores"]);
        scn.render.threads = min(cpu_count(), cores) if cores > 0 else cpu_count();
        
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
        if(camera != None and camera != ""):
            cameraObj = scn.objects.get(camera) or bpy.data.objects.get(camera);
            if(cameraObj is None):
                raise Exception("Unknown Camera :" + camera);
            if(cameraObj.type != "CAMERA"):
                raise Exception("Object is not a Camera :" + camera);
            scn.camera = cameraObj;

        #Set Resolution
        scn.render.resolution_x = int(renderSettings["Width"]);
        scn.render.resolution_y = int(renderSettings["Height"]);
        scn.render.resolution_percentage = 100;

        #Set Samples
        scn.cycles.samples = int(renderSettings["Samples"]);

        scn.render.use_persistent_data = False;

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
                useDevices("HIP", True, True);
                scn.cycles.device = 'GPU';
                print("Use HIP");
            elif renderType == 6: #HIP (GPU Only)
                useDevices("HIP", True, False);
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
            #4.2-4.5 call it BLENDER_EEVEE_NEXT, older and 5.0+ use BLENDER_EEVEE
            engines = [e.identifier for e in scn.render.bl_rna.properties['engine'].enum_items];
            eeveeName = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE";
            print("Using " + eeveeName);
            scn.render.engine = eeveeName;
            scn.eevee.taa_render_samples = int(renderSettings["Samples"]);
        else:
            scn.render.engine = "CYCLES";

        # Set frame
        scn.frame_set(frame)
        bpy.context.view_layer.update()
        bpy.context.evaluated_depsgraph_get().update()
        
        # Set Output
        scn.render.filepath = path;


        # Render
        print("RENDER_START:" + str(id) + "\n", flush=True);

        if(hasattr(bpy.context, "temp_override")):
            with bpy.context.temp_override(scene=scn, view_layer=scn.view_layers[0]):
                bpy.ops.render.render(animation=False, write_still=True, use_viewport=False)
        else:
            bpy.ops.render.render(animation=False, write_still=True, use_viewport=False, layer="", scene=scn.name)

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
