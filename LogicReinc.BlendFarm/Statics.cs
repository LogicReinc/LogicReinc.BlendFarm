using System;
using System.Collections.Generic;
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
    }
}
