using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    [BlendFarmHeader("checkProtocol")]
    public class CheckProtocolRequest : BlendFarmMessage
    {
        public int ClientVersionMajor { get; set; } = 1;
        public int ClientVersionMinor { get; set; } = 0;
        public int ClientVersionPatch { get; set; } = 5;
        public int ProtocolVersion { get; set; } = 1;
    }

    [BlendFarmHeader("checkProtocolResp")]
    public class CheckProtocolResponse : BlendFarmMessage
    {
        public int ClientVersionMajor { get; set; } = 1;
        public int ClientVersionMinor { get; set; } = 0;
        public int ClientVersionPatch { get; set; } = 5;
        public int ProtocolVersion { get; set; }
        public bool RequireAuth { get; set; } = false;
    }
}
