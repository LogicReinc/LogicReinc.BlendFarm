using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for checking version

    [BlendFarmHeader("isVersionAvailable")]
    public class IsVersionAvailableRequest : BlendFarmMessage
    {
        public string Version { get; set; }
    }

    [BlendFarmHeader("isVersionAvailableResp")]
    public class IsVersionAvailableResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
    }
}
