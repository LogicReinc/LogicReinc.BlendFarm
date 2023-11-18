using LogicReinc.BlendFarm.Client.Tasks;
using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
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

        public BlendFarmFileSession(string file, string localDir, string sessionID = null)
        {
            BlendFile = file;
            if (sessionID != null)
                SessionID = sessionID;
            LocalBlendFile = Path.GetFullPath(Path.Combine(localDir, SessionID + ".blend"));
            FileID = new FileInfo(LocalBlendFile).LastWriteTime.Ticks;
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
        public bool Syncing { get; private set; } = false;

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

        public RenderNode AddNode(string name, string address, RenderType type = RenderType.CPU, string pass = null) => AddNode(new RenderNode() { Name = name, Address = address, RenderType = type, Pass = pass });
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
            RenderNode existing = GetNodeByAddress($"{addressPort}");
            if (existing != null)
                return existing;

            return AddNode(name, addressPort, RenderType.CPU);
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
                                if (!Syncing && (CurrentTask == null || AlwaysUpdateFile))
                                {
                                    UpdateFileVersion(session.BlendFile, isNetworking);
                                    OnFileChanged?.Invoke(this);
                                }
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
        /// Updates the file version and copy it to a local directory
        /// </summary>
        public long UpdateFileVersion(BlendFarmFileSession session, bool shared)
        {
            long oldID = session.FileID;
            FileInfo info = new FileInfo(session.BlendFile);
            session.FileID = info.LastWriteTime.Ticks;
            session.Networked = shared;

            string sharedPath = SessionUtil.GetSessionNetworkPath(info.FullName, session.SessionID);
            if (oldID != session.FileID)
            {
                if (!shared)
                    File.Copy(session.BlendFile, session.LocalBlendFile, true);
                else
                    File.Copy(session.BlendFile, sharedPath, true);
                foreach (RenderNode node in Nodes)
                    node.UpdateSyncedStatus(session.SessionID, false);
            }
            else if (shared && !File.Exists(sharedPath))
            {
                File.Copy(session.BlendFile, sharedPath, true);
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
            try
            {
                BlendFarmFileSession session = GetOrCreateSession(localPath);

                UpdateFileVersion(session, true);

                Syncing = true;

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
                Syncing = false;
            }
        }
        /// <summary>
        /// Synchronize the Blend file with all connected nodes
        /// </summary>
        public async Task Sync(string file, bool compress = false)
        {
            try
            {
                BlendFarmFileSession session = GetOrCreateSession(file);

                UpdateFileVersion(session, false);

                Syncing = true;
                //Optimize
                long id = session.FileID;

                string toSend = file;

                if(compress)
                    using (FileStream mem = new FileStream("compressed.zip", FileMode.Create))
                    {
                        byte[] buffer = new byte[4096];

                        await Task.Run(() =>
                        {
                            Nodes.ToList().ForEach(x => x.UpdateActivity("Compressing.."));

                            using (FileStream str = new FileStream(session.BlendFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (Stream zip = new GZipStream(mem, CompressionMode.Compress, true))
                            {
                                int read = 0;
                                while ((read = str.Read(buffer, 0, buffer.Length)) > 0)
                                    zip.Write(buffer, 0, buffer.Length);
                            }

                            Nodes.ToList().ForEach(x => x.UpdateActivity(""));
                        });
                        toSend = "compressed.zip";
                    }
                await Task.WhenAll(Nodes.ToList().Select(async node =>
                {
                    SyncResponse resp = null;
                    try
                    {
                        using(FileStream str = new FileStream(toSend, FileMode.Open, FileAccess.Read, FileShare.Read))
                            resp = await node.SyncFile(session.SessionID, id, str, (compress) ? Compression.GZip : Compression.Raw);
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
                Syncing = false;
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

        public RenderTask GetAnimationTask(string file, int start, int end,RenderManagerSettings settings = null, Action<RenderSubTask, SubTaskResult> onResult = null)
        {
            BlendFarmFileSession session = GetOrCreateSession(file);
            CurrentTask = new AnimationTask(Nodes.ToList(), session.SessionID, Version, session.FileID, start, end, settings);

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
