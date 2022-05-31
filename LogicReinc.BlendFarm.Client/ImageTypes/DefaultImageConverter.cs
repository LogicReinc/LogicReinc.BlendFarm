using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client.ImageTypes
{
    public class DefaultImageConverter : IImageConverter
    {
        public Image FromStream(Stream str)
        {
            return new Bitmap(Bitmap.FromStream(str));
        }
    }
}
