using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace LogicReinc.BlendFarm.Client
{
    /// <summary>
    /// Settings describing how render
    /// </summary>
    public class RenderManagerSettings
    {
        public string FILE_NAME = "RenderDefaultSettings";

        /// <summary>
        /// How to render among nodes
        /// </summary>
        public RenderStrategy Strategy { get; set; } = RenderStrategy.SplitHorizontal;
        /// <summary>
        /// Order to render tiles (only used when tiles are used)
        /// </summary>
        public TaskOrder Order { get; set; } = TaskOrder.Default;

        /// <summary>
        /// Frame to render
        /// </summary>
        public int Frame { get; set; } = 1;


        /// <summary>
        /// FPS, 0 = inherit
        /// </summary>
        public int FPS { get; set; } = 0;

        /// <summary>
        /// Denoiser to use for render, "" = inherit
        /// </summary>
        public string Denoiser { get; set; } = "";

        /// <summary>
        /// Chunk Height (0..1), used when render is divided into chunks (Chunked, SplitChunked)
        /// </summary>
        public decimal ChunkHeight { get; set; } = Math.Round(((decimal)(256) / 1080),4); //0.066
        /// <summary>
        /// Chunk Width (0..1), used when render is divided into chunks (Chunked, SplitChunked)
        /// </summary>
        public decimal ChunkWidth { get; set; } = Math.Round(((decimal)(256) / 1920), 4); //0.12

        /// <summary>
        /// Output Resolution Width
        /// </summary>
        public int OutputWidth { get; set; } = 1920;
        /// <summary>
        /// Output Resolution Height
        /// </summary>
        public int OutputHeight { get; set; } = 1080;
        /// <summary>
        /// Cycles Samples
        /// </summary>
        public int Samples { get; set; } = 128;


        /// <summary>
        /// Use automatic performance detection based on render times
        /// </summary>
        public bool UseAutoPerformance { get; set; } = true;


        /// <summary>
        /// A sad requirement that works around a problem in Blender.
        /// Blender doesn't properly update before rendering in subsequent tasks in a batch
        /// It changes both rendering at the node as well as handling of incoming tiles
        /// It may cause artifacts and inaccuracies. And a newer (or perhaps even older) version of blender may have this fixed.
        /// Currently enabled by default because 2.91.0 has this issue.
        /// </summary>
        public bool BlenderUpdateBugWorkaround { get; set; } = true;

        /// <summary>
        /// Settings describing render
        /// </summary>
        public RenderPacketModel Render { get; set; }
    }

    public enum RenderStrategy
    {
        SplitHorizontal = 0,
        SplitVertical = 3,
        Chunked = 1,
        SplitChunked = 2
    }
    public enum TaskOrder
    {
        Default = 0,
        Center = 1
    }
}
