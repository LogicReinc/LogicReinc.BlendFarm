using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Model used to pass to the python script
    /// </summary>
    public class BlenderImportSettings
    {
        public int Width { get; set; } = 0;
        public int Height { get; set; } = 0;
        public int FrameStart { get; set; } = 0;
        public int FrameEnd { get; set; } = 0;
        public List<string> Cameras { get; set; } = new List<string>();
        public int Samples { get; set; } = 128;
        public EngineType Engine { get; set; } = EngineType.Cycles;
        public bool UseWidth { get; set; } = true;
        public bool UseHeight { get; set; } = true;
        public bool UseFrameStart { get; set; } = true;
        public bool UseFrameEnd { get; set; } = true;
        public bool UseCameras { get; set; } = true;
        public bool UseSamples { get; set; } = true;
        public bool UseEngine { get; set; } = true;

        public static BlenderImportSettings FromBlender(Dictionary<string, string> settings, List<string> cameras)
        {
            var result = new BlenderImportSettings()
            {
                Width = int.Parse(settings["Width"]),
                Height = int.Parse(settings["Height"]),
                FrameStart= int.Parse(settings["FrameStart"]),
                FrameEnd= int.Parse(settings["FrameEnd"]),
                Cameras = cameras,
                Samples = int.Parse(settings["Samples"]),
                Engine = settings["Engine"] == "BLENDER_EEVEE" ? EngineType.Eevee : EngineType.Cycles

            };
            return result;
        }
    }
}
