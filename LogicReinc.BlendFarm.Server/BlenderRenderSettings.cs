using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Model used to pass to the python script
    /// </summary>
    public class BlenderRenderSettings
    {
        /// <summary>
        /// Identification of a subtask
        /// </summary>
        public string TaskID { get; set; }
        /// <summary>
        /// Output path, replaced by TaskID if null
        /// </summary>
        public string Output { get; set; } = null;
        /// <summary>
        /// Frame to render
        /// </summary>
        public int Frame { get; set; } = 0;

        /// <summary>
        /// Scene to render
        /// </summary>
        public string Scene { get; set; } = "";

        /// <summary>
        /// Camera of scene
        /// </summary>
        public string Camera { get; set; } = "";

        /// <summary>
        /// Number of CPU cores to use
        /// </summary>
        public int Cores { get; set; } = Environment.ProcessorCount;
        /// <summary>
        /// Compute device to use (CPU/GPU etc)
        /// </summary>
        public RenderType ComputeUnit { get; set; } = RenderType.CPU;

        /// <summary>
        /// Denoiser for Blender (None/NLM/OptiX/OpenImageDenoise) Empty or null is seen as inherit
        /// </summary>
        public string Denoiser { get; set; }

        /// <summary>
        /// FPS, 0 = inherit
        /// </summary>
        public int FPS { get; set; }

        /// <summary>
        /// Render Rectangle Start X (0..1)
        /// </summary>
        public decimal X { get; set; } = 0;
        /// <summary>
        /// Render Rectangle End X (0..1)
        /// </summary>
        public decimal X2 { get; set; } = 1;
        /// <summary>
        /// Render Rectangle Start Y (0..1)
        /// </summary>
        public decimal Y { get; set; } = 0;
        /// <summary>
        /// Render Rectangle End Y (0..1)
        /// </summary>
        public decimal Y2 { get; set; } = 1;

        /// <summary>
        /// TileWidth, replaced optimally if <=0
        /// </summary>
        public int TileWidth { get; set; } = -1;
        /// <summary>
        /// TileHeight, replaced optimally if <=0
        /// </summary>
        public int TileHeight { get; set; } = -1;

        /// <summary>
        /// Number of samples for Cycles
        /// </summary>
        public int Samples { get; set; } = 128;

        /// <summary>
        /// Render Reslution Width
        /// </summary>
        public int Width { get; set; } = 1920;
        /// <summary>
        /// Render Resolution Height
        /// </summary>
        public int Height { get; set; } = 1080;

        /// <summary>
        /// Engine to use
        /// </summary>
        public EngineType Engine { get; set; } = EngineType.Cycles;

        /// <summary>
        /// Format to render to
        /// </summary>
        public string RenderFormat { get; set; } = "";


        /// <summary>
        /// Crop output (discouraged)
        /// </summary>
        public bool Crop { get; set; }
        /// <summary>
        /// A sad requirement that works around a problem in Blender.
        /// Blender doesn't properly update before rendering in subsequent tasks in a batch
        /// It changes both rendering at the node as well as handling of incoming tiles
        /// It may cause artifacts and inaccuracies. And a newer (or perhaps even older) version of blender may have this fixed.
        /// Currently enabled by default because 2.91.0 has this issue.
        /// </summary>
        public bool Workaround { get; set; }

        /// <summary>
        /// Converts a RenderSettings received from Client to the internal class
        /// Merge?
        /// </summary>
        public static BlenderRenderSettings FromRenderSettings(RenderPacketModel settings)
        {
            var result = new BlenderRenderSettings()
            {
                TaskID = (!string.IsNullOrEmpty(settings.TaskID)) ? settings.TaskID : Guid.NewGuid().ToString(),
                X = settings.X,
                X2 = settings.X2,
                Y = settings.Y,
                Y2 = settings.Y2,
                Cores = settings.Cores,
                Frame = settings.Frame,
                Scene = settings.Scene,
                Camera = settings.Camera,
                TileHeight = settings.TileHeight,
                TileWidth = settings.TileWidth,
                Width = settings.Width,
                Height = settings.Height,
                Samples = settings.Samples,
                ComputeUnit = settings.RenderType,
                Crop = settings.Crop,
                Denoiser = settings.Denoiser,
                FPS = settings.FPS,
                Workaround = settings.Workaround,
                Engine = settings.Engine,
                RenderFormat = settings.RenderFormat
            };

            return result;
        }
    }
}
