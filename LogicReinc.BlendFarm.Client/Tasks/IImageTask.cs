using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace LogicReinc.BlendFarm.Client.Tasks
{
    public interface IImageTask
    {
        Image FinalImage { get; }
    }
}
