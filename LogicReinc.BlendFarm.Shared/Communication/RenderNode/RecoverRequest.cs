using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for render (singular)

    [BlendFarmHeader("recover")]
    public class RecoverRequest : BlendFarmMessage
    {
        public string[] SessionIDs { get; set; } = new string[0];
    }

    [BlendFarmHeader("recoverResp")]
    public class RecoverResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string[] SessionIDs { get; set; } = new string[0];
    }
}
