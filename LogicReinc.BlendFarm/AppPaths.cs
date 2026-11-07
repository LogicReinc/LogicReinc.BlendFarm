using LogicReinc.BlendFarm.Server;
using System;
using System.IO;

namespace LogicReinc.BlendFarm
{
    /// <summary>
    /// Resolves the client's data files (settings, history, queue, local blend copies) against the
    /// application directory instead of the process working directory, so they don't vanish
    /// when the app is started from a different folder.
    /// </summary>
    public static class AppPaths
    {
        private static bool IsMac => SystemInfo.IsOS(SystemInfo.OS_MACOS) || SystemInfo.IsOS(SystemInfo.OS_MACOSARM64);

        public static string BaseDirectory => AppContext.BaseDirectory;

        public static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
                return path;
            //MacOS already maps relative paths into Application Support
            if (IsMac)
                return SystemInfo.RelativeToApplicationDirectory(path);
            return Path.Combine(BaseDirectory, path);
        }

        /// <summary>
        /// Makes the application directory the working directory, so relative paths used by the
        /// embedded server (VersionCache, BlenderData, ...) resolve to the same place as the client's.
        /// </summary>
        public static void UseApplicationDirectoryAsWorkingDirectory()
        {
            if (IsMac)
                return;
            try
            {
                string baseDir = BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir) &&
                    !string.Equals(Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                   Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                   StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Switching working directory to application directory: {baseDir}");
                    Directory.SetCurrentDirectory(baseDir);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to switch working directory to application directory: {ex.Message}");
            }
        }
    }
}
