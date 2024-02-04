using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    [BlendFarmHeader("activity")]
    public class ActivityRequest : BlendFarmMessage
    {
        public string Type { get; set; }
        public string Activity { get; set; }
        public double Progress { get; set; } = -1;
    }
}
