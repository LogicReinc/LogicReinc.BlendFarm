using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for cancelling sync

    [BlendFarmHeader("cancelSync")]
    public class CancelSyncRequest : BlendFarmMessage
    {
        public string SessionID { get; set; }

        public string UploadID { get; set; }
    }

    [BlendFarmHeader("cancelSyncResp")]
    public class CancelSyncResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
