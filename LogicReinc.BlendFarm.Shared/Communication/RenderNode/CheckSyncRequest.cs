using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for checking sync

    [BlendFarmHeader("checkSync")]
    public class CheckSyncRequest : BlendFarmMessage
    {
        public string SessionID { get; set; }
        public long FileID { get; set; }

    }

    [BlendFarmHeader("checkSyncResp")]
    public class CheckSyncResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
