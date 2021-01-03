using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    [BlendFarmHeader("isBusy")]
    public class IsBusyRequest : BlendFarmMessage
    {
    }
    [BlendFarmHeader("isBusyResp")]
    public class IsBusyResponse : BlendFarmMessage
    {
        public bool IsBusy { get; set; }
    }
}
