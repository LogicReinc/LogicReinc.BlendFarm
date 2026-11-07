using LogicReinc.BlendFarm.Shared.Sync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Keeps synced .blend files by content hash so they survive disconnects and restarts.
    /// Re-syncing content the node already has is free, and the latest version of each project
    /// is used as the base for a delta transfer when it changes.
    /// </summary>
    public static class BlendCache
    {
        private const string INDEX_FILE = "index.json";
        private static readonly object _lock = new object();
        private static CacheIndex _index = null;

        public static string Directory => SystemInfo.RelativeToApplicationDirectory(ServerSettings.Instance.BlendCache);

        public static string GetPath(string hash) => Path.Combine(Directory, hash + ".blend");
        private static string GetSignaturePath(string hash, int blockSize) => Path.Combine(Directory, $"{hash}.{blockSize}.sig");

        public static bool Has(string hash)
        {
            return ContentHash.IsValid(hash) && File.Exists(GetPath(hash));
        }

        /// <summary>
        /// Marks a cached file as used now (eviction is least recently used first)
        /// </summary>
        public static void Touch(string hash)
        {
            try { File.SetLastAccessTimeUtc(GetPath(hash), DateTime.UtcNow); } catch { }
            lock (_lock)
            {
                CacheIndex index = GetIndex();
                index.LastUsed[hash] = DateTime.UtcNow;
                SaveIndex(index);
            }
        }

        /// <summary>
        /// Latest cached version of a project, the delta base for its next sync
        /// </summary>
        public static string GetProjectBase(string projectKey)
        {
            if (string.IsNullOrEmpty(projectKey))
                return null;
            lock (_lock)
            {
                if (GetIndex().Projects.TryGetValue(projectKey, out string hash) && Has(hash))
                    return hash;
            }
            return null;
        }

        /// <summary>
        /// Block signatures of a cached file, computed once and kept next to it
        /// </summary>
        public static byte[] GetSignatures(string hash, int blockSize)
        {
            string sigPath = GetSignaturePath(hash, blockSize);
            //One computation per file, a sync arriving during the background precompute waits for it
            lock (string.Intern(sigPath))
            {
                if (File.Exists(sigPath))
                {
                    try { return File.ReadAllBytes(sigPath); } catch { }
                }
                byte[] sigs;
                using (FileStream str = new FileStream(GetPath(hash), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024))
                    sigs = BlockDelta.ComputeSignatures(str, blockSize);
                try
                {
                    File.WriteAllBytes(sigPath + ".tmp", sigs);
                    File.Move(sigPath + ".tmp", sigPath, true);
                }
                catch { }
                return sigs;
            }
        }

        /// <summary>
        /// Temp file to rebuild an incoming version into
        /// </summary>
        public static string GetTempPath(string uploadID)
        {
            System.IO.Directory.CreateDirectory(Directory);
            return Path.Combine(Directory, $"incoming-{uploadID}.tmp");
        }

        /// <summary>
        /// Moves a verified temp file into the cache and records it as the project's latest version
        /// </summary>
        public static void Add(string tempPath, string hash, string projectKey, IEnumerable<string> inUse)
        {
            string target = GetPath(hash);
            if (File.Exists(target))
                File.Delete(tempPath);
            else
            {
                try
                {
                    File.Move(tempPath, target);
                }
                catch (IOException) when (File.Exists(target))
                {
                    //Another transfer of the same content won the race, identical by hash
                    File.Delete(tempPath);
                }
            }
            lock (_lock)
            {
                CacheIndex index = GetIndex();
                if (!string.IsNullOrEmpty(projectKey))
                    index.Projects[projectKey] = hash;
                index.LastUsed[hash] = DateTime.UtcNow;
                SaveIndex(index);
            }
            Evict(inUse.Concat(new[] { hash }));

            //This version is the delta base for the project's next sync, prepare its signatures now instead of while the client waits
            System.Threading.Tasks.Task.Run(() =>
            {
                try { GetSignatures(hash, BlockDelta.DEFAULT_BLOCK_SIZE); }
                catch (Exception ex) { Console.WriteLine($"Blend cache: failed to precompute signatures: {ex.Message}"); }
            });
        }

        /// <summary>
        /// Removes least recently used files until the cache is under its size limit.
        /// Files in use by a session and the latest version of each project are evicted last.
        /// </summary>
        public static void Evict(IEnumerable<string> inUse)
        {
            long limit = (long)(ServerSettings.Instance.BlendCacheGB * 1024L * 1024 * 1024);
            if (limit <= 0)
                return;
            HashSet<string> keep = new HashSet<string>(inUse.Where(x => x != null));
            lock (_lock)
            {
                CacheIndex index = GetIndex();
                HashSet<string> latest = new HashSet<string>(index.Projects.Values);
                List<FileInfo> files = new DirectoryInfo(Directory).GetFiles("*.blend")
                    .Where(x => ContentHash.IsValid(Path.GetFileNameWithoutExtension(x.Name)))
                    .ToList();
                long total = files.Sum(x => x.Length);

                IEnumerable<FileInfo> candidates = files
                    .Where(x => !keep.Contains(Path.GetFileNameWithoutExtension(x.Name)))
                    .OrderBy(x => latest.Contains(Path.GetFileNameWithoutExtension(x.Name)) ? 1 : 0)
                    .ThenBy(x => index.LastUsed.TryGetValue(Path.GetFileNameWithoutExtension(x.Name), out DateTime t) ? t : x.LastAccessTimeUtc);

                foreach (FileInfo file in candidates.ToList())
                {
                    if (total <= limit)
                        break;
                    string hash = Path.GetFileNameWithoutExtension(file.Name);
                    try
                    {
                        file.Delete();
                        foreach (string sig in System.IO.Directory.GetFiles(Directory, hash + ".*.sig"))
                            File.Delete(sig);
                        total -= file.Length;
                        index.LastUsed.Remove(hash);
                        foreach (string key in index.Projects.Where(x => x.Value == hash).Select(x => x.Key).ToList())
                            index.Projects.Remove(key);
                        Console.WriteLine($"Blend cache: evicted {hash}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Blend cache: failed to evict {hash}: {ex.Message}");
                    }
                }
                SaveIndex(index);
            }
        }

        /// <summary>
        /// Removes leftover temp files from interrupted transfers
        /// </summary>
        public static void CleanupIncoming()
        {
            try
            {
                if (!System.IO.Directory.Exists(Directory))
                    return;
                foreach (string file in System.IO.Directory.GetFiles(Directory, "incoming-*.tmp"))
                    try { File.Delete(file); } catch { }
            }
            catch { }
        }

        private static CacheIndex GetIndex()
        {
            if (_index != null)
                return _index;
            string path = Path.Combine(Directory, INDEX_FILE);
            try
            {
                if (File.Exists(path))
                    _index = JsonSerializer.Deserialize<CacheIndex>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Blend cache: index unreadable, starting fresh: " + ex.Message);
            }
            if (_index == null)
                _index = new CacheIndex();
            return _index;
        }

        private static void SaveIndex(CacheIndex index)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                string path = Path.Combine(Directory, INDEX_FILE);
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(index));
                File.Move(path + ".tmp", path, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Blend cache: failed to save index: " + ex.Message);
            }
        }

        public class CacheIndex
        {
            /// <summary>ProjectKey -> latest content hash</summary>
            public Dictionary<string, string> Projects { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, DateTime> LastUsed { get; set; } = new Dictionary<string, DateTime>();
        }
    }
}
