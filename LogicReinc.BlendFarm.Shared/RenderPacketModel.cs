using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared
{
    public enum RenderType
    {
        CPU = 0,
        CUDA = 1,
        OPENCL = 2,
        CUDA_GPUONLY = 3,
        OPENCL_GPUONLY = 4
    }
    public enum EngineType
    {
        Cycles = 0,
        Eevee = 1,
        OptiX = 2
    }

    public enum FormatType
    {
        Default = 0,

    }

    /// <summary>
    /// Model used to transfer render settings between client and render node
    /// (SubTask => BlenderRenderSettings)
    /// </summary>
    public class RenderPacketModel
    {
        /// <summary>
        /// Identifier for Task
        /// </summary>
        public string TaskID { get; set; }
        /// <summary>
        /// Frame of Blenderifle to render
        /// </summary>
        public int Frame { get; set; } = 0;

        //System

        /// <summary>
        /// Number of cores to use (in case of CPU, -1 is all)
        /// </summary>
        public int Cores { get; set; } = -1;
        /// <summary>
        /// Render strategy to use
        /// </summary>
        public RenderType RenderType { get; set; }

        /// <summary>
        /// Denoiser for Blender (None/NLM/OptiX/OpenImageDenoise) Empty or null is seen as inherit
        /// </summary>
        public string Denoiser { get; set; } = "";

        /// <summary>
        /// FPS for file, 0 means inherit
        /// </summary>
        public int FPS { get; set; } = 0;

        //Render Info
        /// <summary>
        /// Render resolution Width
        /// </summary>
        public int Width { get; set; } = 1920;
        /// <summary>
        /// Render resolution Height
        /// </summary>
        public int Height { get; set; } = 1080;

        /// <summary>
        /// Render tile width
        /// </summary>
        public int TileWidth { get; set; } = -1;
        /// <summary>
        /// Render tile height
        /// </summary>
        public int TileHeight { get; set; } = -1;

        /// <summary>
        /// Cycles samples to render with
        /// </summary>
        public int Samples { get; set; } = 128;

        /// <summary>
        /// RenderBorder X1 0..1
        /// </summary>
        public decimal X { get; set; } = 0;
        /// <summary>
        /// RenderBorder X2 0..1
        /// </summary>
        public decimal X2 { get; set; } = 1;
        /// <summary>
        /// RenderBorder Y1 0..1
        /// </summary>
        public decimal Y { get; set; } = 0;
        /// <summary>
        /// RenderBorder Y2 0..1
        /// </summary>
        public decimal Y2 { get; set; } = 1;

        /// <summary>
        /// Engine to use
        /// </summary>
        public EngineType Engine { get; set; } = EngineType.Cycles;

        /// <summary>
        /// Format to render to
        /// </summary>
        public string RenderFormat { get; set; } = "";

        /// <summary>
        /// If render should be cropped (otherwise transparant background for non-rendered parts)
        /// </summary>
        public bool Crop { get; set; }
        /// <summary>
        /// If Blender Workaround should be used to assume render settings not updating properly.
        /// </summary>
        public bool Workaround { get; set; }
    }
}
