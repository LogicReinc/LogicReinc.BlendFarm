using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client
{
    public class BlendFarmManager
    {
        public const string LocalNodeName = "Local";

        public string SessionID { get; private set; }
        public string Version { get; private set; }
        public long FileID { get; private set; }
        public string BlendFile { get; private set; }
        public string LocalBlendFile { get; private set; }

        public List<RenderNode> Nodes { get; private set; } = new List<RenderNode>();
        public int Connected => Nodes.ToList().Where(x => x.Connected).Count();


        public int WatchInterval { get; set; } = 1000;
        public bool IsWatchingFile { get; private set; } = false;

        public bool AlwaysUpdateFile { get; set; }
        public bool Syncing { get; private set; } = false;
        public RenderTask CurrentTask { get; private set; } = null;

        public event Action<BlendFarmManager> OnFileChanged;

        public event Action<BlendFarmManager, RenderNode> OnNodeAdded;
        public event Action<BlendFarmManager, RenderNode> OnNodeRemoved;


        public BlendFarmManager(string file, string version, string sessionID = null, string localDir = "LocalBlendFiles")
        {
            BlendFile = file;
            Directory.CreateDirectory(localDir);
            LocalBlendFile = Path.GetFullPath(Path.Combine(localDir, sessionID + ".blend"));
            Version = version;
            SessionID = sessionID ?? Guid.NewGuid().ToString();
            UpdateFileVersion();
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

        public RenderNode AddNode(string name, string address, RenderType type = RenderType.CPU) => AddNode(new RenderNode() { Name = name, Address = address, RenderType = type });
        public RenderNode AddNode(RenderNode node)
        {
            RenderNode existing = GetNodeByName(node.Name);

            if (string.IsNullOrEmpty(node.Name))
                throw new ArgumentException("Node needs a name");
            if (string.IsNullOrEmpty(node.Address))
                throw new ArgumentException("Node needs an address");
            if (existing != null)
                throw new ArgumentException($"Already have a node with name {node.Name}");

            if (node.Name == LocalNodeName)
                Nodes.Insert(0, node);
            else
                Nodes.Add(node);
            OnNodeAdded?.Invoke(this, node);

            return node;
        }
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
            new Thread(() =>
            {
                while (IsWatchingFile)
                {
                    if (new FileInfo(BlendFile).LastWriteTime.Ticks != FileID)
                    {
                        if (!Syncing && (CurrentTask == null || AlwaysUpdateFile))
                        {
                            UpdateFileVersion();
                            OnFileChanged?.Invoke(this);
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
        public long UpdateFileVersion()
        {
            long oldID = FileID;
            FileID = new FileInfo(BlendFile).LastWriteTime.Ticks;
            if (oldID != FileID)
            {
                File.Copy(BlendFile, LocalBlendFile, true);
                foreach (RenderNode node in Nodes)
                    node.UpdateSyncedStatus(false);
            }
            return FileID;
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
        /// Synchronize a the Blend file with all connected nodes
        /// </summary>
        /// <returns></returns>
        public async Task Sync()
        {
            try
            {
                Syncing = true;
                //Optimize
                long id = FileID;
                await Task.WhenAll(Nodes.ToList().Select(async node =>
                {
                    SyncResponse resp = null;
                    try
                    {
                        using (FileStream str = new FileStream(BlendFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                            resp = await node.SyncFile(SessionID, id, str);
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
        public RenderTask GetRenderTask(RenderManagerSettings settings = null, Action<RenderSubTask, Bitmap> onResultUpdated = null, Action<RenderSubTask, Bitmap> onTileReceived = null)
        {
            CurrentTask = new RenderTask(Nodes.ToList(), SessionID, Version, FileID, settings);

            if (onResultUpdated != null)
                CurrentTask.OnResultUpdated += onResultUpdated;
            if (onTileReceived != null)
                CurrentTask.OnTileProcessed += onTileReceived;

            return CurrentTask;
        }

        public void ClearLastTask()
        {
            CurrentTask = null;
        }
        /// <summary>
        /// Render with provided settings on connected prepared nodes
        /// </summary>
        public async Task<Bitmap> Render(RenderManagerSettings settings = null, Action<RenderSubTask, Bitmap> onResultUpdated = null, Action<RenderSubTask, Bitmap> onTileReceived = null)
        {
            if (CurrentTask != null)
                throw new InvalidOperationException("Already rendering..");
            try
            {
                CurrentTask = GetRenderTask(settings, onResultUpdated, onTileReceived);

                return await CurrentTask.Render();
            }
            finally
            {
                CurrentTask = null;
            }
        }
    }
}
