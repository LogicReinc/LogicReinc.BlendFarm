using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace LogicReinc.BlendFarm
{
    public static class Statics
    {
        public static string SanitizePath(string inputPath)
        {
            if (inputPath == null)
                return inputPath;

            //Fix Linux Space escape
            inputPath = inputPath.Replace("\\040", " ");


            return inputPath;
        }

        public static Bitmap ToAvaloniaBitmap(this System.Drawing.Bitmap bitmap)
        {
            //TODO: This needs to be better..
            using (MemoryStream str = new MemoryStream())
            {
                bitmap.Save(str, ImageFormat.Png);
                str.Position = 0;
                return new Bitmap(str);
            }
        }
    }
}
