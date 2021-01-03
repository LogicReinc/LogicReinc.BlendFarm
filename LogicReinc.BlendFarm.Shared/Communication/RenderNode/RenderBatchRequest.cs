using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for rendering (batch)

    [BlendFarmHeader("renderBatch")]
    public class RenderBatchRequest : BlendFarmMessage
    {
        public string TaskID { get; set; }
        public string SessionID { get; set; }
        public string Version { get; set; }
        public long FileID { get; set; }

        public List<RenderPacketModel> Settings { get; set; } = new List<RenderPacketModel>();
    }

    [BlendFarmHeader("renderBatchResp")]
    public class RenderBatchResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string TaskID { get; set; }
        public List<string> SubTaskIDs { get; set; }
    }

    [BlendFarmHeader("renderBatchResult")]
    public class RenderBatchResult : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string TaskID { get; set; }
        public byte[] Data { get; set; }
    }

}
