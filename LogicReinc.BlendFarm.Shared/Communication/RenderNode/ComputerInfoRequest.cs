using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for retrieving computer info

    [BlendFarmHeader("computerInfo")]
    public class ComputerInfoRequest : BlendFarmMessage
    {
    }
    [BlendFarmHeader("computerInfoResp")]
    public class ComputerInfoResponse : BlendFarmMessage
    {
        public string Name { get; set; }
        public int Cores { get; set; }
        public string OS { get; set; }
        public bool CUDA { get; set; } = false; //Not implemented
        public bool GPU { get; set; } = false; //Not implemented
    }
}
