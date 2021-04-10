using LogicReinc.BlendFarm.Shared;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Manages different versions of Blender and their usage
    /// Assumes maximum of one render active at a time.
    /// </summary>
    public class BlenderManager
    {
        private static string _scripts = null;

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
        /// <summary>
        /// Returns formatted path to a blender version directory (specific os)
        /// </summary>
        public string GetVersionPath(string version, string os)
        {
            return Path.Combine(GetBlenderDataPath(), $"{version}-{os}");
        }
        /// <summary>
        /// Returns formatted path to the render script, if it doesn't exist or is outdated, write it.
        /// Changed script is ignored if Settings.BypassScriptUpdate is true
        /// </summary>
        public string GetRenderScriptPath()
        {
            string path = Path.Combine(GetBlenderDataPath(), $"render.py");
            if (!File.Exists(path) || (!ServerSettings.Instance.BypassScriptUpdate && File.ReadAllText(path) != _scripts))
            {
                Directory.CreateDirectory(GetBlenderDataPath());
                File.WriteAllText(path, GetRenderScript());
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

        /// <summary>
        /// Check if a specific version of Blender is present
        /// </summary>
        public bool IsVersionAvailable(string version)
        {
            if (!SystemInfo.IsOS(SystemInfo.OS_MACOS))
                return Directory.Exists(GetVersionPath(version, SystemInfo.GetOSName()));
            else
                return File.Exists(GetVersionPath(version, SystemInfo.GetOSName()) + ".dmg");
        }
        /// <summary>
        /// Attempt to provide a version of Blender
        /// </summary>
        public bool TryPrepare(string version)
        {
            try
            {
                Prepare(version);
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
        public void Prepare(string version)
        {
            BlenderVersion v = BlenderVersion.FindVersion(version, SystemInfo.RelativeToApplicationDirectory("VersionCache"));

            if (v == null)
                throw new ArgumentException("Version not found");

            string targetDir = GetVersionPath(version, SystemInfo.GetOSName());

            if (Directory.Exists(targetDir))
                Console.WriteLine($"{version} already present");
            else
                Download(SystemInfo.GetOSName(), v);
        }
        /// <summary>
        /// Download a specific version of Blender for OS
        /// </summary>
        public void Download(string os, BlenderVersion version)
        {
            switch (os)
            {
                case "windows64":
                    DownloadWindows(version);
                    break;
                case "linux64":
                    DownloadLinux(version);
                    break;
                case "macOS":
                    DownloadMacOS(version);
                    break;
                default:
                    throw new NotImplementedException("Unknown OS");
            }
        }

        /// <summary>
        /// Downloads windows version of a specific version of Blender (And extract it)
        /// </summary>
        /// <param name="version"></param>
        public void DownloadWindows(BlenderVersion version)
        {
            string os = "windows64";
            string ext = "zip";
            string archiveName = $"{version.Name}-{os}.{ext}";
            string archivePath = Path.Combine(GetBlenderDataPath(), archiveName);
            try
            {
                Directory.CreateDirectory(GetBlenderDataPath());

                using (WebClient client = new WebClient())
                {
                    Console.WriteLine($"Downloading {version.Name}...");
                    client.DownloadFile(version.UrlWindows64, archivePath);
                }
                Console.WriteLine($"Extracting {version.Name}...");

                ZipFile.ExtractToDirectory(archivePath, GetBlenderDataPath());

                Console.WriteLine($"{version.Name} ready");
            }
            catch(Exception ex)
            {
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
        public void DownloadLinux(BlenderVersion version)
        {
            string os = "linux64";
            string ext = "tar.xz";
            string archiveName = $"{version.Name}-{os}.{ext}";
            string archivePath = Path.Combine(GetBlenderDataPath(), archiveName);
            try
            {
                Directory.CreateDirectory(GetBlenderDataPath());

                using (WebClient client = new WebClient())
                {
                    Console.WriteLine($"Downloading {version.Name}...");
                    client.DownloadFile(version.UrlLinux64, archivePath);
                }
                Console.WriteLine($"Extracting {version.Name}...");

                using (FileStream str = new FileStream(archivePath, FileMode.Open))
                using (var reader = ReaderFactory.Open(str))
                {
                    while (reader.MoveToNextEntry())
                    {
                        if (!reader.Entry.IsDirectory)
                        {
                            reader.WriteEntryToDirectory(GetBlenderDataPath(), new SharpCompress.Common.ExtractionOptions()
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                }

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
        public void DownloadMacOS(BlenderVersion version)
        {
            string os = "macOS";
            string ext = "dmg";
            string archiveName = $"{version.Name}-{os}.{ext}";
            string archivePath = Path.Combine(GetBlenderDataPath(), archiveName);
            try
            {
                Directory.CreateDirectory(GetBlenderDataPath());

                using (WebClient client = new WebClient())
                {
                    Console.WriteLine($"Downloading {version.Name}...");
                    client.DownloadFile(version.UrlMacOS, archivePath);
                }
                Console.WriteLine($"Extracting {version.Name}...");

                string versionPath = GetVersionPath(version.Name, os);
                string imagePath = versionPath + "-image";

                Directory.CreateDirectory(imagePath);

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

        /// <summary>
        /// Renders a batch of render settings in a single Blender instance.
        /// </summary>
        public List<string> RenderBatch(string version, string file, BlenderRenderSettings[] batch, Action<BlenderProcess> beforeStart = null)
        {
            if (Busy)
                throw new InvalidOperationException("Currently already rendering");
            Busy = true;

            try
            {

                Directory.CreateDirectory(Path.GetFullPath(RenderData));
                string os = SystemInfo.GetOSName();
                string blenderDir = GetVersionPath(version, os);

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

                    string cmd = $"{blenderDir}/blender";

                    //MacOS has to be special.
                    if (SystemInfo.IsOS(SystemInfo.OS_MACOS))
                        cmd = $"{blenderDir}/Contents/MacOS/Blender";

                    string arg = $"--factory-startup -noaudio -b \"{Path.GetFullPath(file)}\" -P \"{GetRenderScriptPath()}\" -- \"{path}\"";


                    RenderProcess = new BlenderProcess(cmd, arg);
                    if (beforeStart != null)
                        beforeStart(RenderProcess);

                    RenderProcess.Run();
                });
                return batch.Select(x => x.Output).ToList();
            }
            finally
            {
                RenderSession = null;
                RenderProcess = null;
                Busy = false;
            }
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
                if (string.IsNullOrEmpty(Path.GetExtension(settings.Output)))
                    settings.Output += ".png";
            }
        }



        /// <summary>
        /// Render a single render settings (calls batch underneath with single entry)
        /// </summary>
        public string Render(string version, string file, BlenderRenderSettings settings, Action<BlenderProcess> beforeStart = null)
        {
            return RenderBatch(version, file, new[] { settings }, beforeStart).FirstOrDefault();
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
            if (_scripts == null)
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "LogicReinc.BlendFarm.Server.render.py";

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (StreamReader reader = new StreamReader(stream))
                {
                    _scripts = reader.ReadToEnd();
                }
            }
            return _scripts;
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
    }
}
