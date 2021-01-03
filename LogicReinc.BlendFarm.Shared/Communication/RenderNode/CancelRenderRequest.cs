using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    [BlendFarmHeader("cancelRender")]
    public class CancelRenderRequest : BlendFarmMessage
    {
        public string Session { get; set; }
    }
}
