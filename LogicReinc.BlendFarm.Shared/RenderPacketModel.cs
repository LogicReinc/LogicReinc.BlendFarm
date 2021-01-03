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
    }
    /// <summary>
    /// Model used to transfer render settings between client and render node
    /// (SubTask => BlenderRenderSettings)
    /// </summary>
    public class RenderPacketModel
    {
        public string TaskID { get; set; }
        public int Frame { get; set; } = 0;

        //System

        public int Cores { get; set; } = -1;
        public RenderType RenderType { get; set; }

        //Render Info
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;

        public int TileWidth { get; set; } = -1;
        public int TileHeight { get; set; } = -1;

        public int Samples { get; set; } = 128;

        public decimal X { get; set; } = 0;
        public decimal X2 { get; set; } = 1;
        public decimal Y { get; set; } = 0;
        public decimal Y2 { get; set; } = 1;

        public bool Crop { get; set; }
        public bool Workaround { get; set; }
    }
}
