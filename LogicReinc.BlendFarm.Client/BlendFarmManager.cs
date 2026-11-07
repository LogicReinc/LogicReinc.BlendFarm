using LogicReinc.BlendFarm.Client.Tasks;
using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using LogicReinc.BlendFarm.Shared.Sync;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client
{
    public class BlendFarmFileSession
    {
        /// <summary>
        /// ID used to identify a session (mostly by render nodes)
        /// </summary>
        public string SessionID { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Blendfile to render
        /// </summary>
        public string BlendFile { get; set; }

        public long FileID { get; set; }

        /// <summary>
        /// Path to Blendfile copy used for rendering
        /// </summary>
        public string LocalBlendFile { get; set; }

        public bool Networked { get; set; }

        /// <summary>
        /// SHA-256 of LocalBlendFile, and the FileID it was computed for
        /// </summary>
        public string ContentHash { get; set; }
        public long ContentHashFileID { get; set; }

        /// <summary>
        /// Stable id of the project across sessions and restarts, nodes use it to find the previous version as delta base
        /// </summary>
        public string ProjectKey { get; }

        /// <summary>
        /// Held while the local copy is updated or a sync hashes, plans and sends it,
        /// so the file watcher can't replace the snapshot halfway through a sync
        /// </summary>
        public SemaphoreSlim SyncLock { get; } = new SemaphoreSlim(1, 1);

        public BlendFarmFileSession(string file, string localDir, string sessionID = null)
        {
            BlendFile = file;
            if (sessionID != null)
                SessionID = sessionID;
            LocalBlendFile = Path.GetFullPath(Path.Combine(localDir, SessionID + ".blend"));
            FileID = new FileInfo(LocalBlendFile).LastWriteTime.Ticks;
            ProjectKey = Shared.Sync.ContentHash.ComputeString(Environment.MachineName + "|" + Path.GetFullPath(file).ToLowerInvariant());
        }
    }

    public class BlendFarmManager
    {
        public const string LocalNodeName = "Local";

        /// <summary>
        /// ID used to identify a session (mostly by render nodes)
        /// </summary>
        //public string SessionID { get; private set; }
        /// <summary>
        /// Blender Version to use for renders
        /// </summary>
        public string Version { get; private set; }
        /// <summary>
        /// A unique identifier used to detect change in file
        /// </summary>
        //public long FileID { get; private set; }
        /// <summary>
        /// Blendfile to render
        /// </summary>
        //public string BlendFile { get; private set; }
        /// <summary>
        /// Path to Blendfile copy used for rendering
        /// </summary>
        //public string LocalBlendFile { get; private set; }

        public string SelectedSessionID { get; set; } = null;


        /// <summary>
        /// Possible RenderNodes
        /// </summary>
        public List<RenderNode> Nodes { get; private set; } = new List<RenderNode>();

        /// <summary>
        /// Nr of RenderNodes that are connected
        /// </summary>
        public int Connected => Nodes.ToList().Where(x => x.Connected).Count();


        /// <summary>
        /// Interval (ms) to check for file change
        /// </summary>
        public int WatchInterval { get; set; } = 1000;

        /// <summary>
        /// If Blendfile is being watched for change
        /// </summary>
        public bool IsWatchingFile { get; private set; } = false;

        /// <summary>
        /// If Live Render is being used
        /// </summary>
        public bool AlwaysUpdateFile { get; set; }

        /// <summary>
        /// If currently syncing
        /// </summary>
        public bool Syncing => Volatile.Read(ref _syncCount) > 0;
        private int _syncCount = 0;

        /// <summary>
        /// If currently rendering
        /// </summary>
        public bool IsRendering => CurrentTask != null;

        /// <summary>
        /// If file is network shared
        /// </summary>
        public bool IsNetworked { get; set; } = false;

        /// <summary>
        /// Current render task (null if none)
        /// </summary>
        public RenderTask CurrentTask { get; private set; } = null;

        /// <summary>
        /// Event on Blendfile changed
        /// </summary>
        public event Action<BlendFarmManager> OnFileChanged;

        /// <summary>
        /// Event on RenderNode added
        /// </summary>
        public event Action<BlendFarmManager, RenderNode> OnNodeAdded;
        /// <summary>
        /// Event on RenderNode removed
        /// </summary>
        public event Action<BlendFarmManager, RenderNode> OnNodeRemoved;

        private Dictionary<string, BlendFarmFileSession> _sessions = new Dictionary<string, BlendFarmFileSession>();

        private string _localDir = null;

        public BlendFarmManager(string file, string version, string sessionID = null, string localDir = "LocalBlendFiles")
        {
            _localDir = localDir;
            Directory.CreateDirectory(localDir);
            BlendFarmFileSession session = GetOrCreateSession(file);
            //BlendFile = file;
            //LocalBlendFile = Path.GetFullPath(Path.Combine(localDir, sessionID + ".blend"));
            Version = version;
            //SessionID = sessionID ?? Guid.NewGuid().ToString();
            SelectedSessionID = session.SessionID;
            UpdateFileVersion(session, false);
        }

        public void Cleanup()
        {
            foreach(BlendFarmFileSession session in _sessions.Values)
            {
                if (session.Networked)
                {
                    string path = SessionUtil.GetSessionNetworkPath(session.BlendFile, session.SessionID);
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
        }

        public string GetFileSessionID(string file)
        {
            return GetOrCreateSession(file).SessionID;
        }
        public BlendFarmFileSession GetOrCreateSession(string file, string sessionID = null)
        {
            lock (_sessions)
            {
                if (!_sessions.ContainsKey(file))
                    _sessions.Add(file, new BlendFarmFileSession(file, _localDir, sessionID));
                return _sessions[file];
            }
        }
        public List<BlendFarmFileSession> GetSessions()
        {
            lock (_sessions)
                return _sessions.Values.ToList();
        }

        //Nodes
        public RenderNode GetNodeByName(string name)
        {
            name = name.ToLower();
            return Nodes.FirstOrDefault(x => x.Name.ToLower() == name);
        }
        public RenderNode GetNodeByAddress(string address)
        {
            return Nodes.FirstOrDefault(x => x.Address == address);
        }

        public RenderNode AddNode(string name, string address, RenderType type = RenderType.OPTIX_GPUONLY, string pass = null) => AddNode(new RenderNode() { Name = name, Address = address, RenderType = type, Pass = pass });
        public RenderNode AddNode(RenderNode node)
        {
            RenderNode existing = GetNodeByName(node.Name);

            if (string.IsNullOrEmpty(node.Name))
                throw new ArgumentException("Node needs a name");
            if (string.IsNullOrEmpty(node.Address))
                throw new ArgumentException("Node needs an address");
            if (existing != null)
                throw new ArgumentException($"Already have a node with name {node.Name}");

            node.SelectSessionID(SelectedSessionID);

            if (node.Name == LocalNodeName)
                Nodes.Insert(0, node);
            else
                Nodes.Add(node);
            OnNodeAdded?.Invoke(this, node);

            return node;
        }
        /// <summary>
        /// Attempts to add a node that was auto-discovered if its not already in the list of rendernodes
        /// </summary>
        public RenderNode TryAddDiscoveryNode(string name, string address, int port)
        {
            string addressPort = $"{address}:{port}";
            RenderNode existing = Nodes.FirstOrDefault(x => AddressesMatch(x.Address, addressPort));
            if (existing != null)
                return existing;

            string nodeName = GetUniqueNodeName(name);
            return AddNode(nodeName, addressPort, RenderType.OPTIX_GPUONLY);
        }

        private string GetUniqueNodeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = "Discovered Node";

            string candidate = name;
            int index = 2;
            while (GetNodeByName(candidate) != null)
            {
                candidate = $"{name} {index}";
                index++;
            }

            return candidate;
        }

        private static bool AddressesMatch(string left, string right)
        {
            return NormalizeAddress(left) == NormalizeAddress(right);
        }

        private static string NormalizeAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return "";

            address = address.Trim().ToLowerInvariant();
            string host = address;
            string port = "";
            int portIndex = address.LastIndexOf(':');
            if (portIndex >= 0 && portIndex < address.Length - 1)
            {
                host = address.Substring(0, portIndex);
                port = address.Substring(portIndex + 1);
            }

            if (host == "127.0.0.1" || host == "::1")
                host = "localhost";

            return $"{host}:{port}";
        }

        public void RemoveNode(string name)
        {
            RenderNode node = GetNodeByName(name);
            if(node != null)
            {
                Nodes.Remove(node);
                node.Disconnect();
                OnNodeRemoved?.Invoke(this, node);
            }
        }


        //File Sync
        /// <summary>
        /// Starts a file watching thread detecting changes
        /// </summary>
        public void StartFileWatch()
        {
            if (IsWatchingFile)
                return;
            IsWatchingFile = true;
            bool isNetworking = IsNetworked;
            new Thread(() =>
            {
                while (IsWatchingFile)
                {
                    List<BlendFarmFileSession> sessions = GetSessions();

                    foreach (BlendFarmFileSession session in sessions)
                    {
                        try
                        {
                            if (new FileInfo(session.BlendFile).LastWriteTime.Ticks != session.FileID || isNetworking != IsNetworked)
                            {
                                if (isNetworking != IsNetworked)
                                    session.FileID = -1;
                                isNetworking = IsNetworked;
                                //Skipped while a sync holds the session, retried next interval
                                if (!Syncing && (CurrentTask == null || AlwaysUpdateFile)
                                    && TryUpdateFileVersion(session, isNetworking, out _))
                                    OnFileChanged?.Invoke(this);
                            }
                        }
                        catch(Exception ex)
                        {
                            Console.WriteLine($"File Watch exception: [{ex.GetType().Name}] {ex.Message}");
                        }
                    }
                    Thread.Sleep(WatchInterval);
                }
            }).Start();
        }
        /// <summary>
        /// Stops file watching thread
        /// </summary>
        public void StopFileWatch()
        {
            IsWatchingFile = false;
        }

        /// <summary>
        /// Blender's "Compress" save option writes zstd (magic 28 B5 2F FD)
        /// </summary>
        private static bool IsZstdCompressed(string path)
        {
            try
            {
                byte[] magic = new byte[4];
                using (FileStream str = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    if (str.Read(magic, 0, 4) != 4)
                        return false;
                return magic[0] == 0x28 && magic[1] == 0xB5 && magic[2] == 0x2F && magic[3] == 0xFD;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Updates the file version and copy it to a local directory.
        /// While a sync of this session is in progress, nothing is copied and the version being synced is returned.
        /// </summary>
        public long UpdateFileVersion(BlendFarmFileSession session, bool shared)
        {
            TryUpdateFileVersion(session, shared, out long id);
            return id;
        }
        private bool TryUpdateFileVersion(BlendFarmFileSession session, bool shared, out long id)
        {
            if (!session.SyncLock.Wait(0))
            {
                id = session.FileID;
                return false;
            }
            try
            {
                id = UpdateFileVersionLocked(session, shared);
                return true;
            }
            finally
            {
                session.SyncLock.Release();
            }
        }
        /// <summary>
        /// Copies the file to a temporary file first, then renames it over the destination,
        /// so the destination is never a half written copy
        /// </summary>
        private static void CopyAtomic(string source, string destination)
        {
            string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temp, true);
                if (File.Exists(destination))
                    File.Replace(temp, destination, null);
                else
                    File.Move(temp, destination);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
        /// <summary>
        /// UpdateFileVersion, caller must hold session.SyncLock
        /// </summary>
        private long UpdateFileVersionLocked(BlendFarmFileSession session, bool shared)
        {
            long oldID = session.FileID;
            FileInfo info = new FileInfo(session.BlendFile);
            long newID = info.LastWriteTime.Ticks;
            session.Networked = shared;

            string sharedPath = SessionUtil.GetSessionNetworkPath(info.FullName, session.SessionID);
            if (oldID != newID)
            {
                if (!shared)
                    CopyAtomic(session.BlendFile, session.LocalBlendFile);
                else
                    CopyAtomic(session.BlendFile, sharedPath);
                //Only take the new version once the copy succeeded, so a failed copy is retried
                session.FileID = newID;
                foreach (RenderNode node in Nodes)
                    node.UpdateSyncedStatus(session.SessionID, false);
            }
            else if (shared && !File.Exists(sharedPath))
            {
                CopyAtomic(session.BlendFile, sharedPath);
                foreach (RenderNode node in Nodes)
                    node.UpdateSyncedStatus(session.SessionID, false);
            }
            return session.FileID;
        }
        public long UpdateFileVersion(string file, bool shared)
        {
            return UpdateFileVersion(GetOrCreateSession(file), shared);
        }


        //Connection
        /// <summary>
        /// Attempt to connect to all clients
        /// </summary>
        /// <returns></returns>
        public async Task ConnectAll()
        {
            await Task.WhenAll(Nodes.Select(async x => await Connect(x.Name)));
        }
        /// <summary>
        /// Attempt to connect to all clients and prepare the Blender version
        /// </summary>
        public async Task ConnectAndPrepareAll()
        {
            await Task.WhenAll(Nodes.Select(async x =>
            {
                if (!await ConnectAndPrepare(x.Name))
                    x.UpdateException("Failed Version Download");
            }));
        }
        /// <summary>
        /// Connect to a specific client by name
        /// </summary>
        public async Task<RenderNode> Connect(string name)
        {
            RenderNode node = GetNodeByName(name);
            if (node == null)
                throw new ArgumentException($"Node does not exist with name {name}");
            await node.Connect();
            return node;
        }
        /// <summary>
        /// Connect to a specifc client by name and prepare the Blender version
        /// </summary>
        public async Task<bool> ConnectAndPrepare(string name)
        {
            RenderNode node = await Connect(name);

            return (await node.PrepareVersion(Version))?.Success ?? false;
        }

        /// <summary>
        /// Disconnects a specific client by name
        /// </summary>
        public void Disconnect(string name)
        {
            RenderNode node = GetNodeByName(name);
            if (node == null)
                throw new ArgumentException($"Node does not exist with name {name}");
            node.Disconnect();
        }
        /// <summary>
        /// Disconnect all clients
        /// </summary>
        public void DisconnectAll()
        {
            foreach (RenderNode node in Nodes)
                Disconnect(node.Name);
        }

        /// <summary>
        /// Prepare a specific version of blender on all nodes
        /// </summary>
        public async Task Prepare(string version)
        {
            await Task.WhenAll(Nodes.ToList().Select(async node =>
            {
                PrepareResponse resp = null;
                try
                {
                    resp = await node.PrepareVersion(version);
                }
                catch(Exception ex)
                {
                    node.LastStatus = $"Version Failed: {ex.Message}";
                    node.UpdateException("Version Failure");
                    return;
                }
                if (resp == null)
                    node.LastStatus = $"Version Failed: No version..";
                else
                    node.LastStatus = $"Ready";
            }).ToArray());
        }

        /// <summary>
        /// Synchronize the Blend file with a network path with all connected nodes
        /// </summary>
        public async Task Sync(string localPath, string windowsPath, string linuxPath, string macOSPath)
        {
            BlendFarmFileSession session = GetOrCreateSession(localPath);
            await session.SyncLock.WaitAsync();
            Interlocked.Increment(ref _syncCount);
            try
            {
                UpdateFileVersionLocked(session, true);

                long id = session.FileID;

                await Task.WhenAll(Nodes.ToList().Select(async node =>
                {
                    SyncResponse resp = null;
                    try
                    {
                        resp = await node.SyncNetworkFile(session.SessionID, id, windowsPath, linuxPath, macOSPath);
                    }
                    catch (Exception ex)
                    {
                        node.UpdateException(ex.Message);
                        return;
                    }
                    if (resp == null)
                        node.LastStatus = $"Sync Failed: No version..";
                    else
                        node.LastStatus = $"Ready";
                }).ToArray());
            }
            finally
            {
                Interlocked.Decrement(ref _syncCount);
                session.SyncLock.Release();
            }
        }
        /// <summary>
        /// Synchronize the Blend file with all connected nodes
        /// </summary>
        public async Task Sync(string file, bool compress = false)
        {
            BlendFarmFileSession session = GetOrCreateSession(file);
            //Held until every node is done, the watcher can't replace the snapshot while it is hashed, planned or sent
            await session.SyncLock.WaitAsync();
            Interlocked.Increment(ref _syncCount);
            try
            {
                UpdateFileVersionLocked(session, false);

                long id = session.FileID;

                //Send the snapshot copy, not the live file the user may be saving.
                //Hash it once per version: nodes that already have this content skip the transfer entirely
                string toSend = session.LocalBlendFile;
                if (session.ContentHash == null || session.ContentHashFileID != id)
                {
                    Nodes.ToList().ForEach(x => x.UpdateActivity("Checking file.."));
                    session.ContentHash = await Task.Run(() => ContentHash.ComputeFile(toSend));
                    session.ContentHashFileID = id;
                }
                //Compressing a .blend Blender already compressed only costs time
                bool useGZip = compress && !IsZstdCompressed(toSend);

                //Shared by all nodes, nodes with the same base version reuse one delta computation
                DeltaPlanner planner = new DeltaPlanner(toSend);
                await Task.WhenAll(Nodes.ToList().Select(async node =>
                {
                    try
                    {
                        await node.SyncFileDelta(session.SessionID, id, toSend, session.ContentHash, session.ProjectKey, useGZip, planner);
                    }
                    catch (Exception ex)
                    {
                        node.UpdateException(ex.Message);
                        return;
                    }
                    node.LastStatus = $"Ready";
                }).ToArray());
            }
            finally
            {
                Interlocked.Decrement(ref _syncCount);
                session.SyncLock.Release();
            }
        }

        /// <summary>
        /// Creates a RenderTask for the currently connected nodes, not yet executed
        /// </summary>
        public RenderTask GetImageTask(string file, RenderManagerSettings settings = null, Action<RenderSubTask, Image> onResultUpdated = null, Action<RenderSubTask, Image> onTileReceived = null)
        {
            BlendFarmFileSession session = GetOrCreateSession(file);
            CurrentTask = RenderTask.GetImageRenderTask(Nodes.ToList(), session.SessionID, Version, session.FileID, settings);

            if (onResultUpdated != null)
                CurrentTask.OnResultUpdated += onResultUpdated;
            if (onTileReceived != null)
                CurrentTask.OnTileProcessed += onTileReceived;

            return CurrentTask;
        }

        public RenderTask GetAnimationTask(string file, int start, int end,RenderManagerSettings settings = null, Action<RenderSubTask, SubTaskResult> onResult = null, IEnumerable<int> skipFrames = null)
        {
            BlendFarmFileSession session = GetOrCreateSession(file);
            CurrentTask = new AnimationTask(Nodes.ToList(), session.SessionID, Version, session.FileID, start, end, settings)
            {
                SkipFrames = new HashSet<int>(skipFrames ?? Enumerable.Empty<int>())
            };

            if (onResult != null)
                ((AnimationTask)CurrentTask).OnFrameResult += onResult;

            return CurrentTask;
        }

        public void SetSelectedSessionID(string sessionID)
        {
            SelectedSessionID = sessionID;
            lock (Nodes)
                foreach (RenderNode node in Nodes)
                    node.SelectSessionID(sessionID);
        }

        public void ClearLastTask()
        {
            CurrentTask = null;
        }
        /// <summary>
        /// Render with provided settings on connected prepared nodes
        /// </summary>
        public async Task<Image> Render(string file, RenderManagerSettings settings = null, Action<RenderSubTask, Image> onResultUpdated = null, Action<RenderSubTask, Image> onTileReceived = null)
        {
            if (CurrentTask != null)
                throw new InvalidOperationException("Already rendering..");
            try
            {
                CurrentTask = GetImageTask(file, settings, onResultUpdated, onTileReceived);

                await CurrentTask.Render();

                Image bitmap = ((CurrentTask is IImageTask) ? (IImageTask)CurrentTask : null)?.FinalImage;

                return bitmap;
            }
            finally
            {
                CurrentTask = null;
            }
        }

        public async Task<BlenderPeekResponse> Peek(string file)
        {
            try
            {
                RenderNode anyNode = Nodes.FirstOrDefault(x => x.IsSynced && x.IsPrepared);
                if (anyNode == null)
                    throw new InvalidOperationException("No prepared and synced node\n(Blender might still be installing)");

                string fileSessionId = GetFileSessionID(file);
                BlenderPeekResponse resp = await anyNode.Peek(new BlenderPeekRequest()
                {
                    Version = Version,
                    SessionID = fileSessionId
                });
                return resp;
            }
            finally
            {

            }
        }
    }
}
