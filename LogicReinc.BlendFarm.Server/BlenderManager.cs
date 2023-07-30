using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using LogicReinc.BlendFarm.Shared.Models;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Manages different versions of Blender and their usage
    /// Assumes maximum of one render active at a time.
    /// </summary>
    public class BlenderManager
    {
        private static string _scriptRender = null;
        private static string _scriptPeek = null;
        private static string _scriptExtract = null;
        public const int CONTINUE_TIMEOUT = 60000;
        public const bool USE_CONTINUATION = true;

        private object _renderLock = new object();

        public BlenderProcess RenderProcess { get; private set; }
        public string RenderSession { get; private set; }
        public bool Busy { get; private set; }

        /// <summary>
        /// Directory where versions of Blender are saved
        /// </summary>
        public string BlenderData { get; set; }
        /// <summary>
        /// Directory where temporary renders are saved
        /// </summary>
        public string RenderData { get; set; }

        /// <summary>
        /// Use Settings.Instance for directories 
        /// </summary>
        public BlenderManager()
        {
            BlenderData = SystemInfo.RelativeToApplicationDirectory(ServerSettings.Instance.BlenderData);
            RenderData = SystemInfo.RelativeToApplicationDirectory(ServerSettings.Instance.RenderData);
        }
        /// <summary>
        /// Use specific BlenderData and RenderData directories
        /// </summary>
        public BlenderManager(string blenderData, string renderData)
        {
            BlenderData = SystemInfo.RelativeToApplicationDirectory(blenderData);
            RenderData = SystemInfo.RelativeToApplicationDirectory(renderData);
        }

        /// <summary>
        /// Returns formatted BlenderData directory path
        /// </summary>
        /// <returns></returns>
        public string GetBlenderDataPath()
        {
            return Path.GetFullPath(BlenderData);
        }

        public static string GetVersionPath(string blenderDataPath, string version, string os)
        {
            return Path.Combine(blenderDataPath, $"{version}-{os}");
        }
        /// <summary>
        /// Returns formatted path to a blender version directory (specific os)
        /// </summary>
        public string GetVersionPath(string version, string os)
        {
            return GetVersionPath(GetBlenderDataPath(), version, os);
        }
        /// <summary>
        /// Returns formatted path to the render script, if it doesn't exist or is outdated, write it.
        /// Changed script is ignored if Settings.BypassScriptUpdate is true
        /// </summary>
        public string GetRenderScriptPath()
        {
            string path = Path.Combine(GetBlenderDataPath(), $"render.py");
            if (!File.Exists(path) || (!ServerSettings.Instance.BypassScriptUpdate && File.ReadAllText(path) != _scriptRender))
            {
                Directory.CreateDirectory(GetBlenderDataPath());
                File.WriteAllText(path, GetRenderScript());
            }
            return path;
        }
        /// <summary>
        /// Returns formatted path to the peek script, if it doesn't exist or is outdated, write it.
        /// Changed script is ignored if Settings.BypassScriptUpdate is true
        /// </summary>
        public string GetPeekScriptPath()
        {
            string path = Path.Combine(GetBlenderDataPath(), $"peek.py");
            if (!File.Exists(path) || (!ServerSettings.Instance.BypassScriptUpdate && File.ReadAllText(path) != _scriptRender))
            {
                Directory.CreateDirectory(GetBlenderDataPath());
                File.WriteAllText(path, GetPeekScript());
            }
            return path;
        }
        /// <summary>
        /// Returns formatted path to the rewrite script, if it doesn't exist or is outdated, write it.
        /// Changed script is ignored if Settings.BypassScriptUpdate is true
        /// </summary>
        public string GetExtractDependenciesScriptPath()
        {
            string path = Path.Combine(GetBlenderDataPath(), $"rewrite.py");
            if (!File.Exists(path) || (!ServerSettings.Instance.BypassScriptUpdate && File.ReadAllText(path) != _scriptRender))
            {
                Directory.CreateDirectory(GetBlenderDataPath());
                File.WriteAllText(path, GetExtractDependenciesScript());
            }
            return path;
        }

        /// <summary>
        /// Returns OS version, Blender formatted (eg. windows64, linux64)
        /// </summary>
        public string GetOSVersion()
        {
            return SystemInfo.GetOSName();
        }


        public static string GetVersionExecutablePath(string blenderDataPath, string version)
        {
            string os = SystemInfo.GetOSName();
            string blenderDir = GetVersionPath(blenderDataPath, version, os);
            string executable = null;
            switch (os)
            {
                case SystemInfo.OS_WINDOWS64:
                    executable = $"{blenderDir}/blender.exe";
                    break;
                case SystemInfo.OS_LINUX64:
                    executable = $"{blenderDir}/blender";
                    break;
                case SystemInfo.OS_MACOS:
                    executable = $"{blenderDir}/Contents/MacOS/Blender";
                    break;
            }
            return executable;
        }
        public static bool IsVersionValid(string blenderDataPath, string version)
        {
            string os = SystemInfo.GetOSName();
            string blenderDir = GetVersionPath(blenderDataPath, version, os);
            if (!Directory.Exists(blenderDir))
                return false;
            return File.Exists(GetVersionExecutablePath(blenderDataPath, version));
        }
        /// <summary>
        /// Check if a specific version of Blender is present
        /// </summary>
        public bool IsVersionAvailable(string version)
        {
            //if (!SystemInfo.IsOS(SystemInfo.OS_MACOS))
            return Directory.Exists(GetVersionPath(version, SystemInfo.GetOSName()));
            //else
            //    return File.Exists(GetVersionPath(version, SystemInfo.GetOSName()) + ".dmg");
        }
        /// <summary>
        /// Attempt to provide a version of Blender
        /// </summary>
        public bool TryPrepare(string version, Action<string, double> onProgress = null)
        {
            try
            {
                Prepare(version, onProgress);
                return true;
            }
            catch(Exception ex)
            {
                Console.WriteLine("TryPrepare failed due to: " + ex.Message);
                return false;
            }
        }
        /// <summary>
        /// Prepare a version of Blender
        /// </summary>
        /// <param name="version"></param>
        public void Prepare(string version, Action<string, double> onProgress = null)
        {
            BlenderVersion v = BlenderVersion.FindVersion(version, SystemInfo.RelativeToApplicationDirectory("VersionCache"), SystemInfo.RelativeToApplicationDirectory("VersionCustom"));

            if (v == null)
                throw new ArgumentException("Version not found");

            string targetDir = GetVersionPath(version, SystemInfo.GetOSName());

            if (Directory.Exists(targetDir))
                Console.WriteLine($"{version} already present");
            else if (v.IsCustom)
                throw new ArgumentException("Custom version missing");
            else
                Download(SystemInfo.GetOSName(), v, onProgress);
        }
        /// <summary>
        /// Download a specific version of Blender for OS
        /// </summary>
        public void Download(string os, BlenderVersion version, Action<string, double> onProgress = null)
        {
            switch (os)
            {
                case "windows64":
                    DownloadWindows(version, onProgress);
                    break;
                case "linux64":
                    DownloadLinux(version, onProgress);
                    break;
                case "macOS":
                    DownloadMacOS(version, onProgress);
                    break;
                default:
                    throw new NotImplementedException("Unknown OS");
            }
        }

        /// <summary>
        /// Downloads windows version of a specific version of Blender (And extract it)
        /// </summary>
        /// <param name="version"></param>
        public void DownloadWindows(BlenderVersion version, Action<string, double> onProgress = null)
        {
            string os = "windows64";
            string ext = "zip";
            string archiveName = $"{version.Name}-{os}.{ext}";
            string archivePath = Path.Combine(GetBlenderDataPath(), archiveName);
            try
            {
                Directory.CreateDirectory(GetBlenderDataPath());

                /*
                using (WebClient client = new WebClient())
                {
                    Console.WriteLine($"Downloading {version.Name}...");
                    client.DownloadFile
                    client.DownloadFile(version.UrlWindows64, archivePath);
                }*/
                Console.WriteLine($"Downloading {version.Name}...");
                DownloadInternal(version.UrlWindows64, archivePath, onProgress);
                Console.WriteLine($"Extracting {version.Name}...");

                onProgress?.Invoke("Extracting", -1);
                ZipFile.ExtractToDirectory(archivePath, GetBlenderDataPath(), true);

                EnsureOldDirectoryFormat(version.Name, os);

                Console.WriteLine($"{version.Name} ready");
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Exception during extraction:" + ex.Message);
                if (Directory.Exists(GetVersionPath(version.Name, os)))
                    Directory.Delete(GetVersionPath(version.Name, os));
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
            }
        }
        /// <summary>
        /// Downloads a linux version of a specific version of Blender (And extract it)
        /// </summary>
        /// <param name="version"></param>
        public void DownloadLinux(BlenderVersion version, Action<string, double> onProgress = null)
        {
            string os = "linux64";
            string ext = "tar.xz";
            string archiveName = $"{version.Name}-{os}.{ext}";
            string archivePath = Path.Combine(GetBlenderDataPath(), archiveName);
            try
            {
                string blenderDataPath = GetBlenderDataPath();

                Directory.CreateDirectory(blenderDataPath);

                /*
                using (WebClient client = new WebClient())
                {
                    Console.WriteLine($"Downloading {version.Name}...");
                    client.DownloadFile(version.UrlLinux64, archivePath);
                }*/
                Console.WriteLine($"Downloading {version.Name}...");
                DownloadInternal(version.UrlLinux64, archivePath, onProgress);
                Console.WriteLine($"Extracting {version.Name}...");

                onProgress?.Invoke("Extracting", -1);
                List<(string, string)> links = new List<(string, string)>();
                string currentDir = "";
                using (FileStream str = new FileStream(archivePath, FileMode.Open))
                using (var reader = ReaderFactory.Open(str))
                {
                    while (reader.MoveToNextEntry())
                    {
                        if (!reader.Entry.IsDirectory)
                        {
                            if (reader.Entry.LinkTarget != null)
                            {
                                Console.WriteLine($"Link detected, workaround..({reader.Entry.Key}): {reader.Entry.LinkTarget}");
                                string dir = Path.GetDirectoryName(reader.Entry.Key);
                                links.Add((reader.Entry.Key, Path.Combine(dir, reader.Entry.LinkTarget)));
                            }
                            else
                            {
                                reader.WriteEntryToDirectory(blenderDataPath, new SharpCompress.Common.ExtractionOptions()
                                {
                                    ExtractFullPath = true,
                                    Overwrite = true
                                });
                            }
                        }
                        else
                            currentDir = reader.Entry.Key;
                    }
                }
                if (links.Count > 0)
                    Console.WriteLine("Fixing symlinks by copy..");
                foreach ((string, string) link in links)
                {
                    try
                    {
                        Console.WriteLine($"SymLink by Copy: ({link.Item1}) => ({link.Item2})");
                        File.Copy(Path.Combine(blenderDataPath, link.Item2), Path.Combine(blenderDataPath, link.Item1));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to fake-link due to: " + ex.Message);
                    }
                }

                EnsureOldDirectoryFormat(version.Name, os);

                Console.WriteLine($"{version.Name} ready");
                Console.WriteLine("Calling chmod for required permissions");

                //Otherwise can't run blender, not particularily happy with this.
                new ProcessStartInfo()
                {
                    FileName = "chmod",
                    Arguments = "-R u=rwx " + GetVersionPath(version.Name, os),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }.WaitAndPrint();

            }
            catch (Exception ex)
            {
                if (Directory.Exists(GetVersionPath(version.Name, os)))
                    Directory.Delete(GetVersionPath(version.Name, os));
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
            }
        }
        /// <summary>
        /// Downloads macos version of a specific version of Blender (And extract it)
        /// </summary>
        /// <param name="version"></param>
        public void DownloadMacOS(BlenderVersion version, Action<string, double> onProgress = null)
        {
            string os = "macOS";
            string ext = "dmg";
            string archiveName = $"{version.Name}-{os}.{ext}";
            string archivePath = Path.Combine(GetBlenderDataPath(), archiveName);
            try
            {
                Directory.CreateDirectory(GetBlenderDataPath());

                /*
                using (WebClient client = new WebClient())
                {
                    Console.WriteLine($"Downloading {version.Name}...");
                    client.DownloadFile(version.UrlMacOS, archivePath);
                }*/
                Console.WriteLine($"Downloading {version.Name}...");
                DownloadInternal(version.UrlMacOS, archivePath, onProgress);
                Console.WriteLine($"Extracting {version.Name}...");

                string versionPath = GetVersionPath(version.Name, os);
                string imagePath = versionPath + "-image";

                Directory.CreateDirectory(imagePath);

                onProgress?.Invoke("Extracting", -1);
                Console.WriteLine($"Mounting [{archivePath}] to [{imagePath}]");
                Process mountProcess = new Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = "hdiutil",
                        Arguments = $"attach -mountpoint \"{imagePath}\" \"{archivePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                mountProcess.Start();
                mountProcess.WaitForExit();
                Console.WriteLine("Mounted");

                Directory.CreateDirectory(versionPath);

                Console.WriteLine("Copying Blender Files");
                CopyRecursive(Path.Combine(imagePath, "Blender.app"), versionPath);

                Console.WriteLine("Unmounting");
                Process unmountProcess = new Process()
                {
                    StartInfo = new ProcessStartInfo()
                    {
                        FileName = "hdiutil",
                        Arguments = $"detach \"{imagePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                unmountProcess.Start();
                unmountProcess.WaitForExit();
                Console.WriteLine("Unmounted");

                Console.WriteLine($"{version.Name} ready");
            }
            catch (Exception ex)
            {
                if (Directory.Exists(GetVersionPath(version.Name, os)))
                    Directory.Delete(GetVersionPath(version.Name, os));
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
            }
        }

        private void DownloadInternal(string url, string path, Action<string, double> onProgress = null)
        {
            double percentageCom = 0.1;
            Task.Run(async () =>
            {
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    long length = resp.Content.Headers.ContentLength ?? 0L;
                    long lengthMb = length / 1000 / 1000;

                    if (length == 0)
                        Console.WriteLine("Unknown size, cannot provide progress..");

                    Stream stream = await resp.Content.ReadAsStreamAsync();

                    byte[] buffer = new byte[4096];
                    using (FileStream fstr = new FileStream(path, FileMode.Create))
                    using (Stream str = stream)
                    {
                        int written = 0;
                        double lastCom = 0;
                        int read = 0;
                        while ((read = str.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            fstr.Write(buffer, 0, read);
                            written += read;

                            if (length > 0)
                            {
                                double progress = ((double)written / length);
                                if (progress > lastCom + percentageCom)
                                {
                                    lastCom = progress;
                                    Console.WriteLine($"Progress ({Math.Floor(progress * 100)}%, {written / (1000*1000)}MB/{lengthMb}MB)");
                                    onProgress?.Invoke($"Downloading", progress);
                                }
                            }
                        }
                    }
                }
            }).Wait();
            Console.WriteLine($"Progress (100%)");
        }

        private void EnsureOldDirectoryFormat(string version, string os)
        {
            string expectedOld = GetVersionPath(version, BlenderVersion.GetOldOSName(os));
            string expectedNew = GetVersionPath(version, BlenderVersion.GetNewOSName(os));

            //For newer builds, return to old format
            if (Directory.Exists(expectedNew) && !Directory.Exists(expectedOld))
                Directory.Move(expectedNew, expectedOld);
        }


        public string GetVersionCommand(string version)
        {
            string os = SystemInfo.GetOSName();
            string blenderDir = GetVersionPath(version, os);
            string cmd = $"{blenderDir}/blender";

            //MacOS has to be special.
            if (SystemInfo.IsOS(SystemInfo.OS_MACOS))
                cmd = $"{blenderDir}/Contents/MacOS/Blender";

            return cmd;
        }

        /// <summary>
        /// Render a single render settings (calls batch underneath with single entry)
        /// </summary>
        public string Render(string version, string file, BlenderRenderSettings settings, long fileId = -1, Action<BlenderProcess> beforeStart = null, Action<BlenderProcess> beforeEnd = null)
        {
            return RenderBatch(version, file, new[] { settings }, fileId, beforeStart, beforeEnd).FirstOrDefault();
        }
        /// <summary>
        /// Renders a batch of render settings in a single Blender instance.
        /// </summary>
        public List<string> RenderBatch(string version, string file, BlenderRenderSettings[] batch, long fileId = -1, Action<BlenderProcess> beforeStart = null, Action<BlenderProcess> beforeEnd = null)
        {
            lock (_renderLock)
            {
                if (Busy)
                    throw new InvalidOperationException("Currently already rendering");
                Busy = true;
            }

            //Does an ongoing render process match Blender, File, and File version
            if(RenderProcess != null && RenderProcess.Active && RenderProcess.IsContinueing && 
                (version != RenderProcess.Version || file != RenderProcess.File || RenderProcess.FileID != fileId))
            {
                Console.WriteLine("Old continueing RenderProcess, cancelling..");
                RenderProcess.Cancel();
                RenderProcess = null;
            }

            try
            {

                Directory.CreateDirectory(Path.GetFullPath(RenderData));

                try
                {
                    FinalizeSettings(batch);
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to parse/finalize settings due to:" + ex.Message);
                }

                string json = JsonSerializer.Serialize(batch);
                UseTemporaryFile(json, (path) =>
                {

                    string cmd = GetVersionCommand(version);
                    string arg = $"--factory-startup -noaudio -b \"{Path.GetFullPath(file)}\" -P \"{GetRenderScriptPath()}\" -- \"{path}\" {USE_CONTINUATION}";

                    //If an continueing process is ongoing, continue instead.
                    if (RenderProcess == null || !RenderProcess.Active || !RenderProcess.IsContinueing)
                    {
                        RenderProcess = new BlenderProcess(cmd, arg, version, file, fileId);


                        if (beforeStart != null)
                            beforeStart(RenderProcess);
                        RenderProcess.Run();
                        if (beforeEnd != null)
                            beforeEnd(RenderProcess);
                    }
                    else
                    {
                        if (beforeStart != null)
                            beforeStart(RenderProcess);
                        RenderProcess.Continue(path);
                        if (beforeEnd != null)
                            beforeEnd(RenderProcess);
                    }

                });
                return batch.Select(x => FindOutput(x.Output)).Where(x=>x != null).ToList();
            }
            finally
            {
                if (!USE_CONTINUATION || !RenderProcess.Active)
                {
                    RenderSession = null;
                    RenderProcess = null;
                }
                Busy = false;
            }
        }

        public BlenderPeekResponse Peek(string version, string file, long fileId = -1)
        {
            string cmd = GetVersionCommand(version);
            string arg = $"--factory-startup -noaudio -b \"{Path.GetFullPath(file)}\" -P \"{GetPeekScriptPath()}\"";

            BlenderProcess process = new BlenderProcess(cmd, arg, version, file, fileId);

            BlenderProcess.Result result = process.Run();
            if (result.Exceptions.Length > 0)
                throw new Exception("Failed: " + string.Join(", ", result.Exceptions));
            if (result.Results.Length == 0)
                throw new Exception("Exception extracting Blender info");

            BlenderPeekResponse resp = JsonSerializer.Deserialize<BlenderPeekResponse>(result.Results[0]);
            resp.Success = true;
            return resp;
        }

        public List<FileDependency> ExtractDependencies(string version, string file, long fileId = -1)
        {
            string cmd = GetVersionCommand(version);
            string arg = $"--factory-startup -noaudio -b \"{Path.GetFullPath(file)}\" -P \"{GetExtractDependenciesScriptPath()}\"";

            BlenderProcess process = new BlenderProcess(cmd, arg, version, file, fileId);

            BlenderProcess.Result result = process.Run();
            if (result.Exceptions.Length > 0)
                throw new Exception("Failed: " + string.Join(", ", result.Exceptions));

            List<FileDependency> deps = new List<FileDependency>();
            foreach(string res in result.Results)
                deps.Add(JsonSerializer.Deserialize<FileDependency>(res));

            return deps;
        }


        /// <summary>
        /// Checks settings and fills in missing data
        /// </summary>
        /// <param name="batch"></param>
        private void FinalizeSettings(BlenderRenderSettings[] batch)
        {
            //Validate Settings
            for (int i = 0; i < batch.Length; i++)
            {
                BlenderRenderSettings settings = batch[i];

                //Finalize Settings
                if (settings == null)
                {
                    settings = new BlenderRenderSettings();
                    batch[i] = settings;
                }

                if (settings.Cores <= 0)
                    settings.Cores = Environment.ProcessorCount;

                //Check for valid Tile sizes, otherwise, replace with proper one for given device
                switch (settings.ComputeUnit)
                {
                    //CPU tile size is optimally 8 for full scenes, but 16 better deals with quick tiles
                    case RenderType.CPU:
                        if (settings.TileWidth <= 0) settings.TileWidth = 16;
                        if (settings.TileHeight <= 0) settings.TileHeight = 16;
                        break;
                    //CPU/GPU tile size is optimally 64, untill gpu takeover is possible
                    case RenderType.CUDA:
                    case RenderType.OPENCL:
                        if (settings.TileWidth <= 0) settings.TileWidth = 64;
                        if (settings.TileHeight <= 0) settings.TileHeight = 64;
                        break;
                    //GPU tile size is optimally 256
                    case RenderType.CUDA_GPUONLY:
                    case RenderType.OPENCL_GPUONLY:
                    case RenderType.HIP_GPUONLY:
                        if (settings.TileWidth <= 0) settings.TileWidth = 256;
                        if (settings.TileHeight <= 0) settings.TileHeight = 256;
                        break;
                }


                if (settings.TaskID == null)
                    settings.TaskID = Guid.NewGuid().ToString();

                string outputPath = settings.Output;


                if (string.IsNullOrEmpty(outputPath))
                {
                    string outputName = settings.TaskID;
                    outputPath = Path.Combine(RenderData, outputName);
                }

                if (settings.Output == null)
                    settings.Output = outputPath;

                settings.Output = Path.GetFullPath(outputPath);
            }
        }

        /// <summary>
        /// Cancel ongoing render
        /// </summary>
        public void Cancel()
        {
            RenderProcess?.Cancel();
        }

        /// <summary>
        /// Reads render script from assembly 
        /// </summary>
        /// <returns></returns>
        private static string GetRenderScript()
        {
            if (_scriptRender == null)
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "LogicReinc.BlendFarm.Server.render.py";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    _scriptRender = reader.ReadToEnd();
                }
            }
            return _scriptRender;
        }
        /// <summary>
        /// Reads peek script from assembly 
        /// </summary>
        /// <returns></returns>
        private static string GetPeekScript()
        {
            if (_scriptPeek == null)
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "LogicReinc.BlendFarm.Server.peek.py";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    _scriptPeek = reader.ReadToEnd();
                }
            }
            return _scriptPeek;
        }
        /// <summary>
        /// Reads rewrite script from assembly 
        /// </summary>
        /// <returns></returns>
        private static string GetExtractDependenciesScript()
        {
            if (_scriptExtract == null)
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "LogicReinc.BlendFarm.Server.extract_dependencies.py";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    _scriptExtract = reader.ReadToEnd();
                }
            }
            return _scriptExtract;
        }
        /// <summary>
        /// Creates a temporary text file with data
        /// Deleted after action is executed
        /// </summary>
        private static void UseTemporaryFile(string data, Action<string> action)
        {
            string filePath = Path.GetFullPath(SystemInfo.RelativeToApplicationDirectory(Guid.NewGuid().ToString()));
            try
            {
                File.WriteAllText(filePath, data);

                action(filePath);
            }
            finally
            {
                File.Delete(filePath);
            }
        }
    
    
        /// <summary>
        /// Recursively copies directory dir to dest
        /// </summary>
        /// <param name="dir">Directory to copy</param>
        /// <param name="dest">Destination</param>
        private static void CopyRecursive(string dir, string dest)
        {
            DirectoryInfo info = new DirectoryInfo(dir);

            if (!info.Exists)
                throw new DirectoryNotFoundException(dir);

            Directory.CreateDirectory(dest);

            DirectoryInfo destInfo = new DirectoryInfo(dest);

            foreach (FileInfo file in info.GetFiles())
            {
                string targetPath = Path.Combine(destInfo.FullName, file.Name);
                Console.WriteLine($"[{file.FullName}] =>\n    [{targetPath}]");
                file.CopyTo(targetPath);
            }

            foreach (DirectoryInfo subInfo in info.GetDirectories())
            {
                //Ignore empty paths in dmgs
                if (subInfo.Name.Length == 0)
                    continue;
                Console.WriteLine($"[{subInfo.Name}] {subInfo.FullName}");
                CopyRecursive(subInfo.FullName, Path.Combine(dest, subInfo.Name));
            }
        }


        internal static string FindOutput(string output)
        {
            string dir = Path.GetDirectoryName(output);
            output = Path.GetFileName(output);

            FileInfo[] filesInDir = new DirectoryInfo(dir).GetFiles();
            return filesInDir.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x.Name) == output)?.FullName;
        }
    }
}
