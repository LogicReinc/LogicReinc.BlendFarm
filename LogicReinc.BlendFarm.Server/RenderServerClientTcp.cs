using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using LogicReinc.BlendFarm.Shared.Exceptions;
using LogicReinc.BlendFarm.Shared.Sync;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// A connected client
    /// Incoming messages are automatically routed to methods with the BlendFarmHeader attributes
    /// </summary>
    public class RenderServerClientTcp : TcpRenderClient
    {
        private const int UPDATE_TIMING_MS = 300;

        private static Dictionary<string, int> _failedAuthIps = new Dictionary<string, int>();

        private Dictionary<string, FileUpload> _uploads = new Dictionary<string, FileUpload>();
        private Dictionary<string, DeltaUpload> _deltaUploads = new Dictionary<string, DeltaUpload>();
        private BlenderManager _blender = null;

        private List<string> sessions = new List<string>();

        private bool _isInterceptingConsole = false;


        public bool IsAuthenticated { get; private set; }

        /// <summary>
        /// Event on client disconnected
        /// </summary>
        public event Action<RenderServerClientTcp> OnDisconnect;

        public RenderServerClientTcp(BlenderManager manager, TcpClient client) : base(client)
        {
            _blender = manager;
            IsAuthenticated = string.IsNullOrEmpty(ServerSettings.Instance.BasicSecurityPassword);
        }

        private void HandleConsoleOutput(string text)
        {
            Task.Run(() =>
            {
                SendPacket(new ConsoleActivityResponse()
                {
                    Output = text
                });
            });
        }


        protected override void HandleDisconnected()
        {
            if (_isInterceptingConsole)
                Program.OnConsoleOutput -= HandleConsoleOutput;
            //SessionData.CleanUp(sessions.ToArray());
            string[] ownSessions;
            lock (sessions)
                ownSessions = sessions.ToArray();
            _ = SessionData.CleanUpDelayed(10000, ownSessions);
            //Release file handles of interrupted transfers
            lock (_deltaUploads)
            {
                foreach (DeltaUpload upload in _deltaUploads.Values)
                    upload.Abort();
                _deltaUploads.Clear();
            }
            lock (_uploads)
            {
                foreach (FileUpload upload in _uploads.Values)
                    try { upload.Dispose(); } catch { }
                _uploads.Clear();
            }
            OnDisconnect?.Invoke(this);
            //Only cancels if the ongoing render was started by this connection
            if (_blender.Cancel(this))
                Console.WriteLine("Client disconnected during its render, cancelled it");
        }

        /// <summary>
        /// Registers a session as used by this connection
        /// </summary>
        private void AddSession(string sessionID)
        {
            lock (sessions)
            {
                if (!sessions.Contains(sessionID))
                    sessions.Add(sessionID);
            }
        }

        private void EnsureAuthenticated()
        {
            if (!IsAuthenticated)
                throw new ClientStateException("Not authenticated");
        }

        #region Handlers

        [BlendFarmHeader("checkProtocol")]
        public CheckProtocolResponse Packet_CheckProtocol(CheckProtocolRequest req)
        {
            return new CheckProtocolResponse()
            {
                ClientVersionMajor = Program.VersionMajor,
                ClientVersionMinor = Program.VersionMinor,
                ClientVersionPatch = Program.VersionPatch,
                ProtocolVersion = Protocol.Version,
                RequireAuth = !IsAuthenticated
            };
        }

        [BlendFarmHeader("auth")]
        public AuthResponse Packet_Auth(AuthRequest req)
        {
            if (req.Pass != ServerSettings.Instance.BasicSecurityPassword)
                throw new ClientStateException("Wrong password");
            IsAuthenticated = true;

            return new AuthResponse()
            {
                IsAuthenticated = true
            };
        }

        /// <summary>
        /// Handler computerInfo, returns computer info
        /// </summary>
        [BlendFarmHeader("computerInfo")]
        public ComputerInfoResponse Packet_ComputerInfo(ComputerInfoRequest req)
        {
            EnsureAuthenticated();
            return new ComputerInfoResponse()
            {
                Cores = Environment.ProcessorCount,
                Name = Environment.MachineName,
                OS = SystemInfo.GetOSName(),
                OSDescription = SystemInfo.GetOSDescription(),
                Architecture = SystemInfo.GetArchitecture(),
                ProcessorName = SystemInfo.GetProcessorName(),
                GpuNames = SystemInfo.GetGpuNames(),
                MemoryBytes = SystemInfo.GetMemoryBytes(),
                RuntimeDescription = SystemInfo.GetRuntimeDescription()
            };
        }

        /// <summary>
        /// Handler isVersionAvailable, returns if a specified Blender versions is available
        /// </summary>
        [BlendFarmHeader("isVersionAvailable")]
        public IsVersionAvailableResponse Packet_Available(IsVersionAvailableRequest req)
        {
            EnsureAuthenticated();
            return new IsVersionAvailableResponse()
            {
                Success = _blender.IsVersionAvailable(req.Version)
            };
        }

        /// <summary>
        /// Handler isVersionAvailable, returns if a specified Blender versions is available
        /// </summary>
        [BlendFarmHeader("consoleActivityRequest")]
        public void Packet_ConsoleActivity(ConsoleActivityRequest req)
        {
            EnsureAuthenticated();
            if (_isInterceptingConsole)
                return;
            _isInterceptingConsole = true;
            Program.StartIntercepting();
            Program.OnConsoleOutput += HandleConsoleOutput;
        }

        /// <summary>
        /// Handler checkSync, returns if file is synced by FileID
        /// </summary>
        [BlendFarmHeader("checkSync")]
        public CheckSyncResponse Packet_CheckSync(CheckSyncRequest req)
        {
            EnsureAuthenticated();
            SessionData session = SessionData.GetOrCreate(req.SessionID);
            session.InUse = true;
            return new CheckSyncResponse()
            {
                Success = session.FileID == req.FileID
            };
        }

        [BlendFarmHeader("recover")]
        public RecoverResponse Packet_Recover(RecoverRequest req)
        {
            EnsureAuthenticated();
            if (req.SessionIDs != null)
            {
                List<SessionData> datas = new List<SessionData>();
                try
                {
                    foreach (string ses in req.SessionIDs)
                    {
                        SessionData sesData = SessionData.Get(ses);
                        if (sesData == null)
                            return new RecoverResponse()
                            {
                                Success = false,
                                Message = $"Failed to recover [{ses}], Session no longer exists..",
                                SessionIDs = req.SessionIDs
                            };
                        sesData.InUse = true;
                        datas.Add(sesData);
                    }
                }
                catch(Exception ex)
                {
                    SessionData.CleanUpDelayed(10000, datas.Select(x => x.SessionID).ToArray());
                }

                foreach(SessionData data in datas)
                    AddSession(data.SessionID);
            }

            return new RecoverResponse()
            {
                Success = true,
                SessionIDs = req.SessionIDs
            };
        }

        [BlendFarmHeader("syncNetwork")]
        public SyncResponse Packet_SyncNetwork(SyncNetworkRequest req)
        {
            EnsureAuthenticated();
            try
            {
                AddSession(req.SessionID);

                SessionData session = SessionData.GetOrCreate(req.SessionID);
                session.InUse = true;

                string uploadID = Guid.NewGuid().ToString();
                if (req.FileID != session.FileID)
                {
                    session.UpdatingFile();

                    string path = null;
                    switch (SystemInfo.GetOSName())
                    {
                        case SystemInfo.OS_WINDOWS64:
                            path = req.WindowsPath;
                            break;
                        case SystemInfo.OS_LINUX64:
                            path = req.LinuxPath;
                            break;
                        case SystemInfo.OS_MACOS:
                        case SystemInfo.OS_MACOSARM64:
                            path = req.MacOSPath;
                            break;
                        default:
                            throw new NotImplementedException("Unknown OS");
                    }
                    string sessionPath = SessionUtil.GetSessionNetworkPath(path, req.SessionID);
                    if (!File.Exists(sessionPath))
                        throw new InvalidOperationException($"File does not exist [{sessionPath}]");

                    session.IsNetworked = true;
                    session.CacheHash = null;
                    session.NetworkedPath = sessionPath;
                    session.UpdatedFile(req.FileID);
                }

                return new SyncResponse()
                {
                    Success = true,
                    SameFile = req.FileID == session.FileID,
                    UploadID = uploadID
                };
            }
            catch (Exception ex)
            {
                return new SyncResponse()
                {
                    Success = false,
                    Message = "Failed due to exception:" + ex.Message
                };
            }
        }
        /// <summary>
        /// Handler sync, Starts a Sync process, registering a file upload
        /// </summary>
        [BlendFarmHeader("sync")]
        public SyncResponse Packet_Sync(SyncRequest req)
        {
            EnsureAuthenticated();
            try
            {
                AddSession(req.SessionID);

                SessionData session = SessionData.GetOrCreate(req.SessionID);
                session.InUse = true;

                string uploadID = Guid.NewGuid().ToString();
                if (req.FileID != session.FileID)
                {
                    Directory.CreateDirectory(SystemInfo.RelativeToApplicationDirectory(ServerSettings.Instance.BlenderFiles));
                    session.CacheHash = null; //Legacy upload writes its own per-session copy
                    FileUpload newUpload = new FileUpload(session.GetBlendFilePath(), req, req.Compression);
                    lock (_uploads)
                        _uploads.Add(uploadID, newUpload);
                    session.UpdatingFile();
                    session.IsNetworked = false;
                }

                return new SyncResponse()
                {
                    Success = true,
                    SameFile = req.FileID == session.FileID,
                    UploadID = uploadID
                };
            }
            catch(Exception ex)
            {
                return new SyncResponse()
                {
                    Success = false,
                    Message = "Failed due to exception:" + ex.Message
                };
            }
        }
        /// <summary>
        /// Handler syncUpload, Process chunk of Blendfile
        /// </summary>
        [BlendFarmHeader("syncUpload")]
        public SyncUploadResponse Packet_SyncUpload(SyncUploadRequest req)
        {
            EnsureAuthenticated();
            try
            {
                FileUpload upload = null;
                lock (_uploads)
                    _uploads.TryGetValue(req.UploadID ?? "", out upload);
                if (upload == null)
                    return new SyncUploadResponse()
                    {
                        Success = false,
                        Message = "Upload does not exist"
                    };

                /* //Used during debugging
                string hash = Hash.ComputeSyncHash(req.Data);
                if (hash != req.Hash)
                {
                    Console.WriteLine("Expected [" + req.Hash + "], Received: [" + hash + "]");
                    return new SyncUploadResponse()
                    {
                        Success = false,
                        Message = "Corrupted upload"
                    };
                }*/

                lock (upload)
                {
                    //upload.WriteBase64(req.Data);
                    upload.Write(req.Data, 0, req.Data.Length);

                    return new SyncUploadResponse()
                    {
                        Success = true
                    };
                }
            }
            catch(Exception ex)
            {
                return new SyncUploadResponse()
                {
                    Success = false,
                    Message = "Failed due to exception:" + ex.Message
                };
            }
        }

        /// <summary>
        /// Handler syncComplete, Finalize Sync process
        /// </summary>
        [BlendFarmHeader("syncComplete")]
        public SyncCompleteResponse Packet_Complete(SyncCompleteRequest complete)
        {
            EnsureAuthenticated();
            try
            {
                FileUpload upload = null;
                lock (_uploads)
                {
                    if (_uploads.TryGetValue(complete.UploadID ?? "", out upload))
                        _uploads.Remove(complete.UploadID);
                }
                if (upload == null)
                    return new SyncCompleteResponse()
                    {
                        Success = false
                    };
                lock (upload)
                {

                    upload.FinalWrite();

                    SyncRequest obj = upload.GetContext<SyncRequest>();
                    string fileName = upload.TargetPath;
                    upload.Dispose();

                    SessionData session = SessionData.GetOrCreate(obj.SessionID);
                    session.InUse = true;

                    session.UpdatedFile(obj.FileID);

                    return new SyncCompleteResponse()
                    {
                        Success = true
                    };
                }
            }
            catch (Exception ex)
            {
                return new SyncCompleteResponse()
                {
                    Success = false,
                    Message = "Failed due to exception:" + ex.Message
                };
            }
        }

        /// <summary>
        /// Handler syncHash, checks the blend cache for the content and otherwise offers a delta base
        /// </summary>
        [BlendFarmHeader("syncHash")]
        public SyncHashResponse Packet_SyncHash(SyncHashRequest req)
        {
            EnsureAuthenticated();
            try
            {
                if (!ContentHash.IsValid(req.Hash))
                    throw new ArgumentException("Invalid content hash");
                AddSession(req.SessionID);
                SessionData session = SessionData.GetOrCreate(req.SessionID);
                session.InUse = true;

                if (BlendCache.Has(req.Hash))
                {
                    session.UseCachedFile(req.Hash, req.FileID);
                    Console.WriteLine($"Sync: {req.SessionID} already cached ({req.Hash.Substring(0, 12)})");
                    return new SyncHashResponse() { Success = true, Cached = true };
                }

                //Previous version of the same project, or what this session last rendered
                string baseHash = BlendCache.GetProjectBase(req.ProjectKey);
                if (baseHash == null && session.CacheHash != null && BlendCache.Has(session.CacheHash))
                    baseHash = session.CacheHash;

                if (baseHash == null)
                    return new SyncHashResponse() { Success = true, BlockSize = BlockDelta.DEFAULT_BLOCK_SIZE };

                return new SyncHashResponse()
                {
                    Success = true,
                    BaseHash = baseHash,
                    BlockSize = BlockDelta.DEFAULT_BLOCK_SIZE,
                    Signatures = BlendCache.GetSignatures(baseHash, BlockDelta.DEFAULT_BLOCK_SIZE)
                };
            }
            catch (Exception ex)
            {
                return new SyncHashResponse() { Success = false, Message = "Failed due to exception:" + ex.Message };
            }
        }

        /// <summary>
        /// Handler syncDelta, starts rebuilding a file from a cached base (or from nothing)
        /// </summary>
        [BlendFarmHeader("syncDelta")]
        public SyncResponse Packet_SyncDelta(SyncDeltaRequest req)
        {
            EnsureAuthenticated();
            try
            {
                if (!ContentHash.IsValid(req.Hash))
                    throw new ArgumentException("Invalid content hash");
                if (req.BaseHash != null && !BlendCache.Has(req.BaseHash))
                    throw new InvalidOperationException("Delta base is no longer cached");
                if (req.BlockSize <= 0 || req.BlockSize > 16 * 1024 * 1024)
                    throw new ArgumentException("Invalid block size");
                AddSession(req.SessionID);
                SessionData.GetOrCreate(req.SessionID).InUse = true;

                string uploadID = Guid.NewGuid().ToString();
                DeltaUpload upload = new DeltaUpload(req, uploadID);
                lock (_deltaUploads)
                    _deltaUploads.Add(uploadID, upload);
                return new SyncResponse() { Success = true, UploadID = uploadID };
            }
            catch (Exception ex)
            {
                return new SyncResponse() { Success = false, Message = "Failed due to exception:" + ex.Message };
            }
        }

        [BlendFarmHeader("syncDeltaData")]
        public SyncUploadResponse Packet_SyncDeltaData(SyncDeltaDataRequest req)
        {
            EnsureAuthenticated();
            DeltaUpload upload = null;
            lock (_deltaUploads)
                _deltaUploads.TryGetValue(req.UploadID ?? "", out upload);
            if (upload == null)
                return new SyncUploadResponse() { Success = false, Message = "Upload does not exist" };
            try
            {
                lock (upload)
                    upload.Applier.ApplyChunk(req.Data);
                return new SyncUploadResponse() { Success = true };
            }
            catch (Exception ex)
            {
                lock (_deltaUploads)
                    _deltaUploads.Remove(req.UploadID);
                upload.Abort();
                return new SyncUploadResponse() { Success = false, Message = "Failed due to exception:" + ex.Message };
            }
        }

        /// <summary>
        /// Handler syncDeltaDone, verifies the rebuilt file against its hash and adds it to the cache
        /// </summary>
        [BlendFarmHeader("syncDeltaDone")]
        public SyncCompleteResponse Packet_SyncDeltaDone(SyncDeltaDoneRequest req)
        {
            EnsureAuthenticated();
            DeltaUpload upload = null;
            lock (_deltaUploads)
            {
                if (_deltaUploads.TryGetValue(req.UploadID ?? "", out upload))
                    _deltaUploads.Remove(req.UploadID);
            }
            if (upload == null)
                return new SyncCompleteResponse() { Success = false, Message = "Upload does not exist" };
            try
            {
                string hash;
                lock (upload)
                    hash = upload.Finish();
                if (hash != upload.Request.Hash)
                {
                    upload.Abort();
                    return new SyncCompleteResponse() { Success = false, Message = $"Rebuilt file does not match (hash {hash.Substring(0, 12)}), resend in full" };
                }

                BlendCache.Add(upload.TempPath, hash, upload.Request.ProjectKey, SessionData.GetCacheHashesInUse());
                SessionData session = SessionData.GetOrCreate(upload.Request.SessionID);
                session.InUse = true;
                session.UseCachedFile(hash, upload.Request.FileID);
                Console.WriteLine($"Sync: {upload.Request.SessionID} received {(upload.Request.BaseHash != null ? "delta" : "full file")} ({hash.Substring(0, 12)})");
                return new SyncCompleteResponse() { Success = true };
            }
            catch (Exception ex)
            {
                upload.Abort();
                return new SyncCompleteResponse() { Success = false, Message = "Failed due to exception:" + ex.Message };
            }
        }

        /// <summary>
        /// An incoming delta/full transfer being rebuilt into a temp file
        /// </summary>
        private class DeltaUpload
        {
            public SyncDeltaRequest Request { get; }
            public string TempPath { get; }
            public DeltaApplier Applier { get; }
            private FileStream _base;
            private FileStream _output;

            public DeltaUpload(SyncDeltaRequest req, string uploadID)
            {
                Request = req;
                TempPath = BlendCache.GetTempPath(uploadID);
                if (req.BaseHash != null)
                    _base = new FileStream(BlendCache.GetPath(req.BaseHash), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
                _output = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
                Applier = new DeltaApplier(_base, _output, req.BlockSize, req.Compression == Compression.GZip);
            }

            /// <summary>
            /// Closes the files, returns the hash of the rebuilt file
            /// </summary>
            public string Finish()
            {
                string hash = Applier.FinishHash();
                Close();
                return hash;
            }

            public void Abort()
            {
                Close();
                try { File.Delete(TempPath); } catch { }
            }

            private void Close()
            {
                try { _output?.Dispose(); } catch { }
                try { _base?.Dispose(); } catch { }
                try { Applier.Dispose(); } catch { }
                _output = null;
                _base = null;
            }
        }

        /// <summary>
        /// Handler isBusy, Checks if RenderNode is busy
        /// </summary>
        [BlendFarmHeader("isBusy")]
        public IsBusyResponse Packet_IsBusy(IsBusyRequest req)
        {
            EnsureAuthenticated();
            return new IsBusyResponse()
            {
                IsBusy = _blender.Busy
            };
        }

        /// <summary>
        /// Handler prepare, Prepare a specific Blender version
        /// </summary>
        [BlendFarmHeader("prepare")]
        public PrepareResponse Packet_Prepare(PrepareRequest req)
        {
            EnsureAuthenticated();
            if (!_blender.TryPrepare(req.Version, out string error, (activity, progress) =>
            {
                SendPacket(new ActivityRequest()
                {
                    Activity = $"{activity}{((progress > 0) ? $" {Math.Floor(progress * 100).ToString()}%" : "")} [{req.Version}]",
                    Progress = (progress > 0) ? progress * 100 : -1
                });
            }))
                return new PrepareResponse()
                {
                    Message = $"Failed to prepare version {req.Version}: {error}",
                    Success = false
                };

            return new PrepareResponse()
            {
                Success = true
            };
        }

        [BlendFarmHeader("peek")]
        public BlenderPeekResponse Packet_Peek(BlenderPeekRequest req)
        {
            EnsureAuthenticated();
            if (!_blender.IsVersionAvailable(req.Version))
                return new BlenderPeekResponse()
                {
                    Success = false,
                    Message = "Version not prepared.."
                };

            try
            {
                //Validate Settings
                string filePath = SessionData.GetFilePath(req.SessionID);
                if (filePath == null)
                    return new BlenderPeekResponse()
                    {
                        Success = false,
                        Message = "Blend file was not available"
                    };

                return _blender.Peek(req.Version, filePath, req.FileID);
            }
            catch (Exception ex)
            {
                return new BlenderPeekResponse()
                {
                    Success = false,
                    Message = "Exception:" + ex.Message
                };
            }
        }

        /// <summary>
        /// Handler renderBatch, render multiple requests using a single Blender instance
        /// </summary>
        [BlendFarmHeader("renderBatch")]
        public RenderBatchResponse Packet_RenderBatch(RenderBatchRequest req)
        {
            EnsureAuthenticated();
            BlenderRenderSettings[] batch = null;
            if (!_blender.IsVersionAvailable(req.Version))
                return new RenderBatchResponse()
                {
                    TaskID = req.TaskID,
                    Success = false,
                    Message = "Version not prepared.."
                };

            try
            {
                //Validate Settings
                string filePath = SessionData.GetFilePath(req.SessionID);
                if (filePath == null)
                    return new RenderBatchResponse()
                    {
                        TaskID = req.TaskID,
                        Success = false,
                        Message = "Blend file was not available"
                    };

                for (int i = 0; i < req.Settings.Count; i++)
                {
                    Shared.RenderPacketModel settings = req.Settings[i];

                    if (settings == null)
                        settings = new Shared.RenderPacketModel();
                    if (settings.Cores <= 0)
                        settings.Cores = Environment.ProcessorCount;

                    settings.Cores = Math.Min(Environment.ProcessorCount, settings.Cores);
                }

                batch = req.Settings.Select(x => BlenderRenderSettings.FromRenderSettings(x)).ToArray();

                DateTime lastUpdate = DateTime.Now;

                List<string> exceptions = new List<string>();

                //Render
                Action<BlenderProcess.Status> onStatus = (status) =>
                {
                    if (DateTime.Now.Subtract(lastUpdate).TotalMilliseconds > UPDATE_TIMING_MS)
                    {
                        lastUpdate = DateTime.Now;
                        SendPacket(new RenderInfoResponse()
                        {
                            TaskID = req.TaskID,
                            TilesFinished = status.TilesFinish,
                            TilesTotal = status.TilesTotal,
                            Time = status.Time,
                            TimeRemaining = status.TimeRemaining
                        });
                    }
                };
                Action<string> onCompletion = (taskID) =>
                {
                    BlenderRenderSettings settings = batch.FirstOrDefault(x => x.TaskID == taskID);
                    if (settings != null)
                    {
                        try
                        {
                            string output = BlenderManager.FindOutput(settings.Output);
                            if (output == null)
                                throw new FileNotFoundException("Rendered output not found for " + settings.Output);

                            SendPacket(new RenderBatchResult()
                            {
                                Data = File.ReadAllBytes(output),
                                Success = true,
                                TaskID = settings.TaskID
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to send render result for task {taskID}: {ex.Message}");
                        }
                    }
                };
                Action<string> onException = (excp) => exceptions.Add(excp);

                List<string> files = _blender.RenderBatch(req.Version, filePath, batch,
                    req.FileID,
                    (process) =>
                    {
                        process.OnBlenderStatus += onStatus;
                        process.OnBlenderCompleteTask += onCompletion;
                        process.OnBlenderException += onException;
                    },
                    (process)=>
                    {
                        process.OnBlenderStatus -= onStatus;
                        process.OnBlenderCompleteTask -= onCompletion;
                        process.OnBlenderException -= onException;
                    }, this);

                //Handle Result
                if (files  == null || files.Count != req.Settings.Count)
                {
                    if (exceptions.Count == 0)
                        return new RenderBatchResponse()
                        {
                            TaskID = req.TaskID,
                            Success = false,
                            Message = "Missing Files?"
                        };
                    else
                        return new RenderBatchResponse()
                        {
                            TaskID = req.TaskID,
                            Success = false,
                            Message = string.Join(", ", exceptions)
                        };
                }
                else
                {
                    if (exceptions.Count > 0)
                    {
                        return new RenderBatchResponse()
                        {
                            Success = false,
                            TaskID = req.TaskID,
                            SubTaskIDs = req.Settings.Select(X => X.TaskID).ToList(),
                            Message = string.Join(", ", exceptions)
                        };
                    }
                    else
                    {
                        return new RenderBatchResponse()
                        {
                            Success = true,
                            TaskID = req.TaskID,
                            SubTaskIDs = req.Settings.Select(X => X.TaskID).ToList()
                        };
                    }
                };
            }
            catch (Exception ex)
            {
                return new RenderBatchResponse()
                {
                    TaskID = req.TaskID,
                    Success = false,
                    Message = "Exception:" + ex.Message
                };
            }
            finally
            {
                //Results were already sent per task, remove whatever outputs exist (also for partial/failed batches)
                if (batch != null)
                    foreach (BlenderRenderSettings settings in batch)
                    {
                        try
                        {
                            string output = settings?.Output != null ? BlenderManager.FindOutput(settings.Output) : null;
                            if (output != null)
                                File.Delete(output);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to delete render output for task {settings?.TaskID}: {ex.Message}");
                        }
                    }
            }
        }

        /// <summary>
        /// Handler render, Render a single request
        /// </summary>
        [BlendFarmHeader("render")]
        public RenderResponse Packet_Render(RenderRequest req)
        {
            EnsureAuthenticated();
            if (!_blender.IsVersionAvailable(req.Version))
                return new RenderResponse()
                {
                    TaskID = req.TaskID,
                    Success = false,
                    Message = "Version not prepared.."
                };

            try
            {
                //Validate Settings
                string filePath = SessionData.GetFilePath(req.SessionID);
                if (filePath == null)
                    return new RenderResponse()
                    {
                        TaskID = req.TaskID,
                        Success = false,
                        Message = "Blend file was not available"
                    };

                if (req.Settings == null)
                    req.Settings = new Shared.RenderPacketModel();
                if (req.Settings.Cores <= 0)
                    req.Settings.Cores = Environment.ProcessorCount;

                req.Settings.Cores = Math.Min(Environment.ProcessorCount, req.Settings.Cores);

                DateTime lastUpdate = DateTime.Now;

                List<string> exceptions = new List<string>();

                //Render
                Action<BlenderProcess.Status> onStatus = (status) =>
                {
                    if (DateTime.Now.Subtract(lastUpdate).TotalMilliseconds > UPDATE_TIMING_MS)
                    {
                        lastUpdate = DateTime.Now;
                        SendPacket(new RenderInfoResponse()
                        {
                            TaskID = req.TaskID,
                            TilesFinished = status.TilesFinish,
                            TilesTotal = status.TilesTotal,
                            Time = status.Time,
                            TimeRemaining = status.TimeRemaining
                        });
                    }
                };
                Action<string> onException = (excp) => exceptions.Add(excp);
                string file = _blender.Render(req.Version, filePath, 
                    BlenderRenderSettings.FromRenderSettings(req.Settings), 
                    req.FileID,
                    (process)=>
                    {
                        process.OnBlenderStatus += onStatus;
                        process.OnBlenderException += onException;
                    },
                    (process)=>
                    {
                        process.OnBlenderStatus -= onStatus;
                        process.OnBlenderException -= onException;
                    }, this);

                //Handle Result
                if (file == null || !File.Exists(file))
                {

                    if (exceptions.Count == 0)
                        return new RenderResponse()
                        {
                            TaskID = req.TaskID,
                            Success = false,
                            Message = "Missing Files?"
                        };
                    else
                        return new RenderResponse()
                        {
                            TaskID = req.TaskID,
                            Success = false,
                            Message = string.Join(", ", exceptions)
                        };
                }
                else
                {
                    byte[] data = File.ReadAllBytes(file);

                    File.Delete(file);
                    return new RenderResponse()
                    {
                        Success = true,
                        TaskID = req.TaskID,
                        Data = data
                    };
                };
            }
            catch(Exception ex)
            {
                return new RenderResponse()
                {
                    TaskID = req.TaskID,
                    Success = false,
                    Message = "Exception:" + ex.Message
                };
            }
        }

        /// <summary>
        /// Handler cancelRender, Cancels an ongoing render
        /// </summary>
        [BlendFarmHeader("cancelRender")]
        public void Packet_Cancel_Render(CancelRenderRequest req)
        {
            EnsureAuthenticated();
            //Only cancel a render this connection started, not another client's
            _blender.Cancel(this);
        }
        #endregion

    }
}
