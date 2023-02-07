#Start
import bpy

s = bpy.context.scene
JOINER = "|X|"

def findSettings():
    list = ["SETTINGS:"]
    list.append("Width=" + str(s.render.resolution_x))
    list.append("Height=" + str(s.render.resolution_y))
    list.append("FrameStart=" + str(s.frame_start))
    list.append("FrameEnd=" + str(s.frame_end))
    list.append("Engine=" + str(s.render.engine))
    if(s.render.engine == "BLENDER_EEVEE"):
        list.append("Samples=" + str(s.eevee.taa_render_samples))
    else:
        list.append("Samples=" + str(s.cycles.samples))
    print(JOINER.join(list))

def findCameras():
    list = ["CAMERAS:"]
    for o in s.objects:
        if o.type == 'CAMERA':
            list.append(o.name)
    print(JOINER.join(list))



try:
    findCameras()
    findSettings()
        
except Exception as e:
    print("EXCEPTION:" + str(e));