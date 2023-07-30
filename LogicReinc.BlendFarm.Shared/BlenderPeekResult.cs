using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared
{
    public class BlenderPeekResult
    {
        public int RenderWidth { get; set; }
        public int RenderHeight { get; set; }
        public int FrameStart { get; set; }
        public int FrameEnd { get; set; }
        public int Samples { get; set; }

        public string[] Cameras { get; set; }
        public string SelectedCamera { get; set; }


    }
}
