using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for render (singular)

    [BlendFarmHeader("render")]
    public class RenderRequest : BlendFarmMessage
    {
        public string SessionID { get; set; }
        public string TaskID { get; set; }
        public string Version { get; set; }
        public long FileID { get; set; }

        public RenderPacketModel Settings { get; set; } = new RenderPacketModel();
    }

    [BlendFarmHeader("renderResp")]
    public class RenderResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string TaskID { get; set; }
        public byte[] Data { get; set; } = new byte[0];
    }
}
