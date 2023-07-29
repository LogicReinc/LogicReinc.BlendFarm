using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    [BlendFarmHeader("consoleActivityRequest")]
    public class ConsoleActivityRequest : BlendFarmMessage
    {
    }

    [BlendFarmHeader("consoleActivityResponse")]
    public class ConsoleActivityResponse : BlendFarmMessage
    {
        public string Output { get; set; }
    }
}
