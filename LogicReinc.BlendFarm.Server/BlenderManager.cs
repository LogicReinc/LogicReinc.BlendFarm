using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using LogicReinc.BlendFarm.Shared.Models;
using SharpCompress.Readers;
using System;
using System.Collections.Concurrent;
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
        public const bool USE_CONTINUATION = true;

        private object _renderLock = new object();
        private static object _scriptLock = new object();

        /// <summary>
        /// Who started the current render (eg. a connection), only they may cancel it
        /// </summary>
        private object _renderOwner = null;
        /// <summary>
        /// Set by Cancel during a render, checked before a Blender process is started or continued
        /// </summary>
        private bool _cancelRequested = false;

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
            return EnsureScript("render.py", GetRenderScript());
        }
        /// <summary>
        /// Returns formatted path to the peek script, if it doesn't exist or is outdated, write it.
        /// Changed script is ignored if Settings.BypassScriptUpdate is true
        /// </summary>
        public string GetPeekScriptPath()
        {
            return EnsureScript("peek.py", GetPeekScript());
        }
        /// <summary>
        /// Returns formatted path to the rewrite script, if it doesn't exist or is outdated, write it.
        /// Changed script is ignored if Settings.BypassScriptUpdate is true
        /// </summary>
        public string GetExtractDependenciesScriptPath()
        {
            return EnsureScript("rewrite.py", GetExtractDependenciesScript());
        }

        /// <summary>
        /// Writes a script to BlenderData if it is missing or differs from the embedded one
        /// </summary>
        private string EnsureScript(string name, string script)
        {
            string path = Path.Combine(GetBlenderDataPath(), name);
            lock (_scriptLock)
            {
                if (!File.Exists(path) || (!ServerSettings.Instance.BypassScriptUpdate && File.ReadAllText(path) != script))
                {
                    Directory.CreateDirectory(GetBlenderDataPath());
                    File.WriteAllText(path, script);
                }
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
                case SystemInfo.OS_MACOSARM64:
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
            return TryPrepare(version, out _, onProgress);
        }
        /// <summary>
        /// Attempt to provide a version of Blender, error contains the reason on failure
        /// </summary>
        public bool TryPrepare(string version, out string error, Action<string, double> onProgress = null)
        {
            try
            {
                Prepare(version, onProgress);
                error = null;
                return true;
            }
            catch(Exception ex)
            {
                Console.WriteLine("TryPrepare failed due to: " + ex.Message);
                error = ex.Message;
                return false;
            }
        }

        private static ConcurrentDictionary<string, object> _prepareLocks = new ConcurrentDictionary<string, object>();

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

            //Concurrent prepares of the same version wait for the first one instead of downloading over it
            lock (_prepareLocks.GetOrAdd(Path.GetFullPath(targetDir), _ => new object()))
            {
                if (Directory.Exists(targetDir))
                    Console.WriteLine($"{version} already present");
                else if (v.IsCustom)
                    throw new ArgumentException("Custom version missing");
                else
                    Download(SystemInfo.GetOSName(), v, onProgress);
            }
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
                case "macOS-arm64":
                    DownloadMacOSARM64(version, onProgress);
                    break;
                default:
                    throw new NotImplementedException("Unknown OS");
            }
        }

        /// <summary>
        /// Downloads an archive and installs it into the version directory.
        /// install extracts the archive into an empty staging directory and returns the directory to become the version directory,
        /// which is only moved into place once complete, so a partial install never counts as installed.
        /// </summary>
        private void DownloadAndInstall(BlenderVersion version, string os, string ext, string url, Action<string, double> onProgress, Func<string, string, string> install)
        {
            string blenderDataPath = GetBlenderDataPath();
            string archivePath = Path.Combine(blenderDataPath, $"{version.Name}-{os}.{ext}");
            string targetDir = GetVersionPath(version.Name, os);
            string stagingDir = Path.Combine(blenderDataPath, $".staging-{version.Name}-{os}-{Guid.NewGuid().ToString("N")}");
            try
            {
                Directory.CreateDirectory(blenderDataPath);

                Console.WriteLine($"Downloading {version.Name}...");
                DownloadInternal(url, archivePath, onProgress);
                Console.WriteLine($"Extracting {version.Name}...");
                onProgress?.Invoke("Extracting", -1);

                Directory.CreateDirectory(stagingDir);
                string installed = install(archivePath, stagingDir);

                if (Directory.Exists(targetDir))
                    throw new IOException($"Version directory [{targetDir}] appeared during install");
                Directory.Move(installed, targetDir);

                Console.WriteLine($"{version.Name} ready");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to install {version.Name}: " + ex.Message);
                throw;
            }
            finally
            {
                TryDeleteDirectory(stagingDir);
                try
                {
                    if (File.Exists(archivePath))
                        File.Delete(archivePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to delete archive [{archivePath}]: " + ex.Message);
                }
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to delete [{dir}]: " + ex.Message);
            }
        }

        /// <summary>
        /// Finds the extracted Blender directory in staging (either old or new OS naming, or the only directory)
        /// </summary>
        private string FindExtractedDirectory(string stagingDir, string version, string os)
        {
            foreach (string name in new[] { $"{version}-{BlenderVersion.GetOldOSName(os)}", $"{version}-{BlenderVersion.GetNewOSName(os)}" })
            {
                string candidate = Path.Combine(stagingDir, name);
                if (Directory.Exists(candidate))
                    return candidate;
            }
            string[] dirs = Directory.GetDirectories(stagingDir);
            if (dirs.Length == 1)
                return dirs[0];
            throw new InvalidOperationException($"Could not find extracted Blender directory for {version}");
        }

        /// <summary>
        /// Downloads windows version of a specific version of Blender (And extract it)
        /// </summary>
        /// <param name="version"></param>
        public void DownloadWindows(BlenderVersion version, Action<string, double> onProgress = null)
        {
            string os = "windows64";
            DownloadAndInstall(version, os, "zip", version.UrlWindows64, onProgress, (archivePath, stagingDir) =>
            {
                ZipFile.ExtractToDirectory(archivePath, stagingDir, true);
                return FindExtractedDirectory(stagingDir, version.Name, os);
            });
        }
        /// <summary>
        /// Downloads a linux version of a specific version of Blender (And extract it)
        /// </summary>
        /// <param name="version"></param>
        public void DownloadLinux(BlenderVersion version, Action<string, double> onProgress = null)
        {
            string os = "linux64";
            DownloadAndInstall(version, os, "tar.xz", version.UrlLinux64, onProgress, (archivePath, stagingDir) =>
            {
                List<(string, string)> links = new List<(string, string)>();
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
                                reader.WriteEntryToDirectory(stagingDir, new SharpCompress.Common.ExtractionOptions()
                                {
                                    ExtractFullPath = true,
                                    Overwrite = true
                                });
                            }
                        }
                    }
                }
                if (links.Count > 0)
                    Console.WriteLine("Fixing symlinks by copy..");
                foreach ((string, string) link in links)
                {
                    try
                    {
                        Console.WriteLine($"SymLink by Copy: ({link.Item1}) => ({link.Item2})");
                        File.Copy(Path.Combine(stagingDir, link.Item2), Path.Combine(stagingDir, link.Item1));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to fake-link due to: " + ex.Message);
                    }
                }

                string extracted = FindExtractedDirectory(stagingDir, version.Name, os);

                //Otherwise can't run blender, not particularily happy with this.
                //Done before moving into place, so an installed version is always runnable
                Console.WriteLine("Calling chmod for required permissions");
                new ProcessStartInfo()
                {
                    FileName = "chmod",
                    Arguments = $"-R u=rwx \"{extracted}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }.WaitAndPrint();

                return extracted;
            });
        }
        /// <summary>
        /// Downloads macos version of a specific version of Blender (And extract it)
        /// </summary>
        /// <param name="version"></param>
        public void DownloadMacOS(BlenderVersion version, Action<string, double> onProgress = null)
        {
            string os = "macOS";
            DownloadAndInstall(version, os, "dmg", version.UrlMacOS, onProgress, (archivePath, stagingDir) => InstallDmg(archivePath, stagingDir));
        }
        /// <summary>
        /// Downloads macos version of a specific version of Blender (And extract it)
        /// </summary>
        /// <param name="version"></param>
        public void DownloadMacOSARM64(BlenderVersion version, Action<string, double> onProgress = null)
        {
            string os = "macOS-arm64";
            DownloadAndInstall(version, os, "dmg", version.UrlMacOSARM64, onProgress, (archivePath, stagingDir) => InstallDmg(archivePath, stagingDir));
        }

        /// <summary>
        /// Mounts a Blender dmg, copies Blender.app into staging and always detaches the image
        /// </summary>
        private static string InstallDmg(string archivePath, string stagingDir)
        {
            string imagePath = stagingDir + "-image";
            string appPath = Path.Combine(stagingDir, "Blender.app");
            Directory.CreateDirectory(imagePath);
            bool mounted = false;
            try
            {
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
                mountProcess.StandardOutput.ReadToEnd();
                mountProcess.WaitForExit();
                mounted = true;
                if (mountProcess.ExitCode != 0)
                    throw new InvalidOperationException($"Failed to mount [{archivePath}] (hdiutil exit code {mountProcess.ExitCode})");
                Console.WriteLine("Mounted");

                Console.WriteLine("Copying Blender Files");
                CopyRecursive(Path.Combine(imagePath, "Blender.app"), appPath);
                return appPath;
            }
            finally
            {
                if (mounted)
                {
                    try
                    {
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
                        unmountProcess.StandardOutput.ReadToEnd();
                        unmountProcess.WaitForExit();
                        Console.WriteLine("Unmounted");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to detach image: " + ex.Message);
                    }
                }
                //Only removes the (now empty) mountpoint, a still-mounted image fails to delete instead of being emptied
                try
                {
                    if (Directory.Exists(imagePath))
                        Directory.Delete(imagePath, false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to remove mountpoint [{imagePath}]: " + ex.Message);
                }
            }
        }

        private void DownloadInternal(string url, string path, Action<string, double> onProgress = null)
        {
            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("No download available for this OS");
            double percentageCom = 0.1;
            Task.Run(async () =>
            {
                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!resp.IsSuccessStatusCode)
                        throw new HttpRequestException($"Download of [{url}] failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");

                    long length = resp.Content.Headers.ContentLength ?? 0L;
                    long lengthMb = length / 1000 / 1000;

                    if (length == 0)
                        Console.WriteLine("Unknown size, cannot provide progress..");

                    Stream stream = await resp.Content.ReadAsStreamAsync();

                    byte[] buffer = new byte[4096];
                    using (FileStream fstr = new FileStream(path, FileMode.Create))
                    using (Stream str = stream)
                    {
                        long written = 0;
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
                        if (length > 0 && written != length)
                            throw new IOException($"Download of [{url}] incomplete ({written}/{length} bytes)");
                    }
                }
            }).GetAwaiter().GetResult();
            Console.WriteLine($"Progress (100%)");
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
        public string Render(string version, string file, BlenderRenderSettings settings, long fileId = -1, Action<BlenderProcess> beforeStart = null, Action<BlenderProcess> beforeEnd = null, object owner = null)
        {
            return RenderBatch(version, file, new[] { settings }, fileId, beforeStart, beforeEnd, owner).FirstOrDefault();
        }
        /// <summary>
        /// Renders a batch of render settings in a single Blender instance.
        /// </summary>
        /// <param name="owner">Identifies who started the render, only Cancel(owner) with the same owner cancels it</param>
        public List<string> RenderBatch(string version, string file, BlenderRenderSettings[] batch, long fileId = -1, Action<BlenderProcess> beforeStart = null, Action<BlenderProcess> beforeEnd = null, object owner = null)
        {
            lock (_renderLock)
            {
                if (Busy)
                    throw new InvalidOperationException("Currently already rendering");
                Busy = true;
                _renderOwner = owner;
                _cancelRequested = false;
            }

            bool useContinuation = USE_CONTINUATION && !batch.Any(x => x.FreshProcess);

            try
            {
                //Does an ongoing render process match Blender, File, and File version
                if(RenderProcess != null && RenderProcess.Active && RenderProcess.IsContinueing && 
                    (!useContinuation || version != RenderProcess.Version || file != RenderProcess.File || RenderProcess.FileID != fileId))
                {
                    Console.WriteLine("Old continueing RenderProcess, cancelling..");
                    RenderProcess.Cancel();
                    RenderProcess = null;
                }

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
                    string arg = $"--factory-startup -noaudio -b \"{Path.GetFullPath(file)}\" -P \"{GetRenderScriptPath()}\" -- \"{path}\" {useContinuation}";

                    //If an continueing process is ongoing, continue instead.
                    if (RenderProcess != null && RenderProcess.Active && RenderProcess.IsContinueing)
                    {
                        BlenderProcess reused = SetRenderProcess(RenderProcess);
                        int outputsBefore = reused.Results.Count + reused.Exceptions.Count;
                        bool continued = false;
                        beforeStart?.Invoke(reused);
                        try
                        {
                            reused.Continue(path);
                            //Blender exiting without reporting anything means the continuation failed
                            continued = reused.IsContinueing || reused.Results.Count + reused.Exceptions.Count > outputsBefore;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Continuing Blender process failed, starting a fresh one: " + ex.Message);
                        }
                        finally
                        {
                            beforeEnd?.Invoke(reused);
                        }
                        if (continued)
                            return;
                        reused.Cancel();
                        lock (_renderLock)
                            RenderProcess = null;
                    }

                    BlenderProcess fresh = SetRenderProcess(new BlenderProcess(cmd, arg, version, file, fileId));
                    beforeStart?.Invoke(fresh);
                    try
                    {
                        fresh.Run();
                    }
                    finally
                    {
                        beforeEnd?.Invoke(fresh);
                    }

                });
                return batch.Select(x => FindOutput(x.Output)).Where(x=>x != null).ToList();
            }
            finally
            {
                //RenderProcess is null if the render failed before Blender was started
                lock (_renderLock)
                {
                    if (!useContinuation || RenderProcess?.Active != true || RenderProcess.IsCancelled)
                    {
                        RenderSession = null;
                        RenderProcess = null;
                    }
                    _renderOwner = null;
                    _cancelRequested = false;
                    Busy = false;
                }
            }
        }

        /// <summary>
        /// Makes process the current render process, unless the render was cancelled in the meantime
        /// </summary>
        private BlenderProcess SetRenderProcess(BlenderProcess process)
        {
            lock (_renderLock)
            {
                if (_cancelRequested)
                    throw new OperationCanceledException("Render was cancelled");
                RenderProcess = process;
                return process;
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
        /// Cancel ongoing render, regardless of who started it
        /// </summary>
        public void Cancel()
        {
            lock (_renderLock)
            {
                if (Busy)
                    _cancelRequested = true;
                RenderProcess?.Cancel();
            }
        }
        /// <summary>
        /// Cancel the ongoing render only if it was started by owner
        /// </summary>
        /// <returns>If a render was cancelled</returns>
        public bool Cancel(object owner)
        {
            if (owner == null)
                return false;
            lock (_renderLock)
            {
                if (!Busy || _renderOwner != owner)
                    return false;
                _cancelRequested = true;
                RenderProcess?.Cancel();
                return true;
            }
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
