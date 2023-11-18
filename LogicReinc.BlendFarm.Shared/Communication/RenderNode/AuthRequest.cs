using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    [BlendFarmHeader("auth")]
    public class AuthRequest : BlendFarmMessage
    {
        public string Pass { get; set; }
    }

    [BlendFarmHeader("authResponse")]
    public class AuthResponse : BlendFarmMessage
    {
        public bool IsAuthenticated { get; set; }
    }
}
