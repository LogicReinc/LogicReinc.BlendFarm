using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogicReinc.BlendFarm.Shared
{
    /// <summary>
    /// Used to retrieve versions of Blender (and Caching)
    /// </summary>
    public class BlenderVersion
    {
        private const string VERSIONS_URL = "https://download.blender.org/release/";
        private static Regex REGEX_BLENDERVERSION = new Regex("Blender[0-9]\\.[0-9]*\\/");
        private static Regex REGEX_BLENDERSUBVERSION = new Regex("(blender-[0-9]\\.[0-9][0-9]\\.[0-9])-(.*?)\\.(.*)");

        private static int CACHE_DAYS = 3;

        public string Name { get; set; }

        public DateTime Date { get; set; }

        public string UrlLinux64 { get; set; }
        public string UrlMacOS { get; set; }
        public string UrlWindows64 { get; set; }


        public bool HasAll => !string.IsNullOrEmpty(UrlLinux64) && !string.IsNullOrEmpty(UrlWindows64);


        public static BlenderVersion FindVersion(string version)
        {
            return GetBlenderVersions().FirstOrDefault(x => x.Name == version);
        }

        /// <summary>
        /// Retrieve available versions of Blender (from cache if available/recent)
        /// </summary>
        public static List<BlenderVersion> GetBlenderVersions()
        {
            //IMPORTANT: always use cache if able, or get a chance to get your IP blacklisted.
            Cache cache = Cache.GetCache();
            if(cache != null)
                //Refresh every >= CACHE_DAYS days
                if (Math.Abs(cache.Date.DayOfYear - DateTime.Now.DayOfYear) < CACHE_DAYS)
                    return cache.Versions;
            try
            {
                List<BlenderVersion> versions = new List<BlenderVersion>();

                List<WebIndex> coreVersions = WebIndex.GetIndexes(VERSIONS_URL);

                //Parse Blenders Previous Version pages..
                foreach (WebIndex v in coreVersions.Where(x => REGEX_BLENDERVERSION.IsMatch(x.Name) && !x.IsFile))
                {
                    List<WebIndex> subVersions = v.GetIndexes();

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

                Cache.UpdateCache(vs);
                return vs;
            }
            catch(Exception ex)
            {
                Console.WriteLine("Failed to load versions due to " + ex.Message);
                if (cache != null)
                    return cache.Versions;
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
                case "linux64":
                    return UrlLinux64;
                case "windows64":
                    return UrlWindows64;
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
                case "windows64":
                    if (ext == "zip")
                        valid = true;
                    break;
                case "linux64":
                    if (ext == "tar.xz")
                        valid = true;
                    break;

                    //Lets leave out MacOS for now.
                    /*
                    case "macOS":
                        if (ext == "dmg")
                            valid = true;
                        break;*/
            }
            return valid;
        }

        private void AssignUrl(string os, string url)
        {
            switch (os)
            {
                case "linux64":
                    UrlLinux64 = url;
                    break;
                case "windows64":
                    UrlWindows64 = url;
                    break;
                default:
                    throw new ArgumentException("Invalid operating system");
            }
        }


        /// <summary>
        /// Used to keep track of cache status
        /// </summary>
        public class Cache
        {
            public DateTime Date { get; set; }
            public List<BlenderVersion> Versions { get; set; } = new List<BlenderVersion>();

            public static Cache GetCache()
            {
                if (File.Exists("VersionCache"))
                {
                    string cached = File.ReadAllText("VersionCache");
                    return JsonSerializer.Deserialize<Cache>(cached);
                }
                return null;
            }

            public static void UpdateCache(List<BlenderVersion> versions)
            {
                File.WriteAllText("VersionCache", JsonSerializer.Serialize(new Cache()
                {
                    Date = DateTime.Now,
                    Versions = versions
                }));
            }
        }
    }
}
