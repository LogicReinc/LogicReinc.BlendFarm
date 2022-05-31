using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LogicReinc.BlendFarm.Client.ImageTypes
{
    public class ImageFormats
    {
        public static string[] Formats = new string[] { "BMP", "PNG", "JPEG", "JPEG2000", "TARGA", "TARGA_RAW", "CINEON", "DPX", "OPEN_EXR_MULTILAYER", "OPEN_EXR", "HDR", "TIFF" };
        public static string[] Extensions => _extensions.Values.ToArray();
        private static Dictionary<string, string> _extensions = new Dictionary<string, string>()
        {
            { "BMP", "bmp" },
            { "PNG", "png" },
            { "JPEG", "jpg"},
            { "JPEG2000", "jpg"},
            { "TARGA", "tga"},
            { "TARGA_RAW", "tga" },
            { "CINEON", "cin" },
            { "DPX", "dpx" },
            { "OPEN_EXR_MULTILAYER", "exr"},
            { "OPEN_EXR", "exr"},
            { "HDR", "hdr" },
            { "TIFF", "tif"}
        };

        public static string GetExtension(String format)
        {
            if (_extensions.ContainsKey(format))
                return _extensions[format];
            return null;
        }
    }
}
