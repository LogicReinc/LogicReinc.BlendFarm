using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    [BlendFarmHeader("peek")]
    public class BlenderPeekRequest: BlendFarmMessage
    {
        public string SessionID { get; set; }
        public string Version { get; set; }
        public long FileID { get; set; }
    }


    [BlendFarmHeader("peekResp")]
    public class BlenderPeekResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }

        public int RenderWidth { get; set; }
        public int RenderHeight { get; set; }
        public int FrameStart { get; set; }
        public int FrameEnd { get; set; }
        public int Samples { get; set; }

        public string[] Cameras { get; set; }
        public string SelectedCamera { get; set; }

    }
}
