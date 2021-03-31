using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace LogicReinc.BlendFarm.Server
{
    public class SystemInfo
    {
        public const string OS_LINUX64 = "linux64";
        public const string OS_WINDOWS64 = "windows64";
        public const string OS_MACOS = "macOS";


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
                return OS_LINUX64;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return OS_WINDOWS64;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return OS_MACOS;
            else
                throw new NotImplementedException("Unknown OS");
        }


        public static bool IsOS(string osName)
        {
            string name = null;
            try
            {
                name = GetOSName();
            }
            catch (NotImplementedException ex) { }
            return name == osName;
        }
    }
}
