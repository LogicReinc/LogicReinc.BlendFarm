using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication
{
    /// <summary>
    /// Used to attribute BlendFarm packets and handlers
    /// </summary>
    public class BlendFarmHeaderAttribute : Attribute
    {
        public string Header { get; set; }

        public BlendFarmHeaderAttribute(string header)
        {
            Header = header;
        }
    }
}
