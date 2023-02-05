using LogicReinc.BlendFarm.Server;
using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for retrieving the render settings from the blend file

    [BlendFarmHeader("importSettings")]
    public class ImportSettingsRequest : BlendFarmMessage
    {

        public BlenderImportSettings Settings { get; set; }
        public string Version { get; set; }
        public string File { get; set; }
    }
    [BlendFarmHeader("importSettingsResp")]
    public class ImportSettingsResponse : BlendFarmMessage
    {
        public BlenderImportSettings Settings { get; set; }
    }
}
