using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication
{
    [BlendFarmHeader("disconnected")]
    public class BlendFarmDisconnected : BlendFarmMessage
    {
        public bool IsError { get; set; }
        public string Reason { get; set; }
    }
}
