using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace LogicReinc.BlendFarm.Server
{
    public class SystemInfo
    {
        /// <summary>
        /// Ignore actual OS and override it with provided version.
        /// Mostly for testing
        /// </summary>
        public static string OverrideOS = null;

        /// <summary>
        /// Returns OS version in Blender formatted name
        /// </summary>
        /// <returns></returns>
        public static string GetOSName()
        {
            if (OverrideOS != null)
                return OverrideOS;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "linux64";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "windows64";
            else
                throw new NotImplementedException("Unknown OS");
        }
    }
}
