using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace LogicReinc.BlendFarm.Shared
{
    /// <summary>
    /// Used to retrieve versions of Blender (and Caching)
    /// </summary>
    public class BlenderVersion
    {
        private static string[] REQUIRED_OS = new string[] { OS_LINUX, OS_WINDOWS, OS_MACOS };

        private const string OS_LINUX = "linux64";
        private const string OS_LINUX_x64 = "linux-x64";
        private const string OS_WINDOWS = "windows64";
        private const string OS_WINDOWS_x64 = "windows-x64";
        private const string OS_MACOS = "macOS";
        private const string OS_MACOS_x64 = "macos-x64";

        private const string VERSIONS_URL = "https://download.blender.org/release/";
        private static Regex REGEX_BLENDERVERSION = new Regex("Blender[0-9]\\.[0-9]*\\/");
        private static Regex REGEX_BLENDERSUBVERSION = new Regex("(blender-[0-9]\\.[0-9][0-9]\\.[0-9])-(.*?)\\.(.*)");

        private static int CACHE_DAYS = 3;

        public bool IsCustom { get; set; } = false;
        public string Name { get; set; }

        public DateTime Date { get; set; }

        public string UrlLinux64 { get; set; }
        public string UrlMacOS { get; set; }
        public string UrlWindows64 { get; set; }


        public bool HasAll => !string.IsNullOrEmpty(UrlLinux64) && !string.IsNullOrEmpty(UrlWindows64);


        public static BlenderVersion FindVersion(string version, string cacheFile = null, string customFile = null)
        {
            return GetBlenderVersions(cacheFile, customFile).FirstOrDefault(x => x.Name == version);
        }

        public static List<BlenderVersion> GetCustomBlenderVersions(string customFile = null)
        {
            List<BlenderVersion> custom = new List<BlenderVersion>();
            if (customFile != null && File.Exists(customFile))
            {
                string[] customLines = File.ReadAllLines(customFile);
                foreach (string line in customLines)
                {
                    if (!string.IsNullOrEmpty(line))
                        custom.Add(new BlenderVersion()
                        {
                            IsCustom = true,
                            Name = line.Trim()
                        });
                }
            }
            return custom;
        }
        /// <summary>
        /// Retrieve available versions of Blender (from cache if available/recent)
        /// </summary>
        public static List<BlenderVersion> GetBlenderVersions(string cacheFile = null, string customFile = null)
        {
            if (cacheFile != null)
            {
                string fullCachePath = Path.GetFullPath(cacheFile);

                //Intercept designer
                if (fullCachePath.StartsWith("C:\\Windows"))
                    return new List<BlenderVersion>();
            }

            List<BlenderVersion> custom = GetCustomBlenderVersions(customFile);

            //IMPORTANT: always use cache if able, or get a chance to get your IP blacklisted.
            Cache cache = Cache.GetCache(cacheFile);
            if (cache != null)
                //Refresh every >= CACHE_DAYS days
                if (Math.Abs(cache.Date.DayOfYear - DateTime.Now.DayOfYear) < CACHE_DAYS)
                    return custom.Concat(cache.Versions).ToList();
            try
            {
                List<BlenderVersion> versions = new List<BlenderVersion>();

                List<WebIndex> coreVersions = WebIndex.GetIndexes(VERSIONS_URL);

                //Parse Blenders Previous Version pages..
                foreach (WebIndex v in coreVersions.Where(x => REGEX_BLENDERVERSION.IsMatch(x.Name) && !x.IsFile))
                {
                    List<WebIndex> subVersions = v.GetIndexes();

                    if (v.Name == "Blender2.93/")
                        v.Name = v.Name;

                    List<(WebIndex, Match)> matches = subVersions.Select(x => (x, REGEX_BLENDERSUBVERSION.Match(x.Name)))
                        .Where(x => x.Item2.Groups.Count == 4)
                        .ToList();

                    Dictionary<string, BlenderVersion> submapping = new Dictionary<string, BlenderVersion>();


                    foreach (var match in matches)
                    {
                        string url = match.Item1.Url;
                        DateTime date = match.Item1.DateTime;
                        string name = match.Item2.Groups[1].Value;
                        string os = match.Item2.Groups[2].Value;
                        string ext = match.Item2.Groups[3].Value.ToLower();

                        if (IsValidFile(os, ext))
                        {
                            if (!submapping.ContainsKey(name))
                                submapping.Add(name, new BlenderVersion()
                                {
                                    Name = name,
                                    Date = date
                                });
                            submapping[name].AssignUrl(os, url);
                        }
                    }

                    versions.AddRange(submapping.Values.Where(x => x.HasAll).ToList());
                }
                List<BlenderVersion> vs = versions.OrderByDescending(x => x.Date).ToList();

                //Prevent server spam
                Thread.Sleep(500);

                Cache.UpdateCache(vs, cacheFile);
                return custom.Concat(vs).ToList();
            }
            catch(Exception ex)
            {
                Console.WriteLine("Failed to load versions due to " + ex.Message);
                if (cache != null)
                    return custom.Concat(cache.Versions).ToList();
                else
                    throw;
            }
        }

        /// <summary>
        /// Returns the download Url for the provided os
        /// </summary>
        public string GetOSUrl(string os)
        {
            switch (os)
            {
                case OS_LINUX:
                    return UrlLinux64;
                case OS_WINDOWS:
                    return UrlWindows64;
                case OS_MACOS:
                    return UrlMacOS;
                default:
                    throw new ArgumentException("Invalid operating system");
            }
        }

        /// <summary>
        /// Checks if the file extension matches the OS
        /// </summary>
        public static bool IsValidFile(string os, string ext)
        {
            bool valid = false;
            switch (os)
            {
                case OS_WINDOWS:
                case OS_WINDOWS_x64:
                    if (ext == "zip")
                        valid = true;
                    break;
                case OS_LINUX:
                case OS_LINUX_x64:
                    if (ext == "tar.xz")
                        valid = true;
                    break;
                case OS_MACOS:
                case OS_MACOS_x64:
                    if (ext == "dmg")
                        valid = true;
                    break;
            }
            return valid;
        }

        private void AssignUrl(string os, string url)
        {
            switch (os)
            {
                case OS_LINUX:
                case OS_LINUX_x64:
                    UrlLinux64 = url;
                    break;
                case OS_WINDOWS:
                case OS_WINDOWS_x64:
                    UrlWindows64 = url;
                    break;
                case OS_MACOS:
                case OS_MACOS_x64:
                    UrlMacOS = url;
                    break;
                default:
                    throw new ArgumentException("Invalid operating system");
            }
        }

        public static string GetOldOSName(string os)
        {
            switch (os)
            {
                case OS_LINUX:
                case OS_LINUX_x64:
                    return OS_LINUX;
                case OS_WINDOWS:
                case OS_WINDOWS_x64:
                    return OS_WINDOWS;
                case OS_MACOS:
                case OS_MACOS_x64:
                    return OS_MACOS;
                default:
                    return os;
            }
        }
        public static string GetNewOSName(string os)
        {
            switch (os)
            {
                case OS_LINUX:
                case OS_LINUX_x64:
                    return OS_LINUX_x64;
                case OS_WINDOWS:
                case OS_WINDOWS_x64:
                    return OS_WINDOWS_x64;
                case OS_MACOS:
                case OS_MACOS_x64:
                    return OS_MACOS_x64;
                default:
                    return os;
            }
        }


        /// <summary>
        /// Used to keep track of cache status
        /// </summary>
        public class Cache
        {
            public DateTime Date { get; set; }
            public List<BlenderVersion> Versions { get; set; } = new List<BlenderVersion>();

            public static Cache GetCache(string cacheFile = null)
            {
                if (cacheFile == null)
                    cacheFile = "VersionCache";

                if (File.Exists(cacheFile))
                {
                    string cached = File.ReadAllText(cacheFile);
                    return JsonSerializer.Deserialize<Cache>(cached);
                }
                return null;
            }

            public static void UpdateCache(List<BlenderVersion> versions, string cacheFile = null)
            {
                if(cacheFile == null)
                    cacheFile = "VersionCache";
                File.WriteAllText(cacheFile, JsonSerializer.Serialize(new Cache()
                {
                    Date = DateTime.Now,
                    Versions = versions
                }));
            }
        }
    }
}
