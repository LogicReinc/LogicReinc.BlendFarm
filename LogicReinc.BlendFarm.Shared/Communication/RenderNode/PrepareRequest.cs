using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for preparing (version updating etc)

    [BlendFarmHeader("prepare")]
    public class PrepareRequest : BlendFarmMessage
    {
        public string Version { get; set; }
    }


    [BlendFarmHeader("prepareResp")]
    public class PrepareResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
