using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// A connected client
    /// Incoming messages are automatically routed to methods with the BlendFarmHeader attributes
    /// </summary>
    public class RenderServerClientTcp : TcpRenderClient
    {
        private const int UPDATE_TIMING_MS = 300;

        private Dictionary<string, FileUpload> _uploads = new Dictionary<string, FileUpload>();
        private BlenderManager _blender = null;

        private List<string> sessions = new List<string>();

        /// <summary>
        /// Event on client disconnected
        /// </summary>
        public event Action<RenderServerClientTcp> OnDisconnect;

        public RenderServerClientTcp(BlenderManager manager, TcpClient client) : base(client)
        {
            _blender = manager;
        }


        protected override void HandleDisconnected()
        {
            SessionData.CleanUp(sessions.ToArray());
            OnDisconnect?.Invoke(this);
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
                ProtocolVersion = Protocol.Version
            };
        }

        /// <summary>
        /// Handler computerInfo, returns computer info
        /// </summary>
        [BlendFarmHeader("computerInfo")]
        public ComputerInfoResponse Packet_ComputerInfo(ComputerInfoRequest req)
        {
            return new ComputerInfoResponse()
            {
                Cores = Environment.ProcessorCount,
                Name = Environment.MachineName,
                OS = SystemInfo.GetOSName()
            };
        }

        /// <summary>
        /// Handler isVersionAvailable, returns if a specified Blender versions is available
        /// </summary>
        [BlendFarmHeader("isVersionAvailable")]
        public IsVersionAvailableResponse Packet_Available(IsVersionAvailableRequest req)
        {
            return new IsVersionAvailableResponse()
            {
                Success = _blender.IsVersionAvailable(req.Version)
            };
        }

        /// <summary>
        /// Handler checkSync, returns if file is synced by FileID
        /// </summary>
        [BlendFarmHeader("checkSync")]
        public CheckSyncResponse Packet_CheckSync(CheckSyncRequest req)
        {
            return new CheckSyncResponse()
            {
                Success = SessionData.GetOrCreate(req.SessionID).FileID == req.FileID
            };
        }

        /// <summary>
        /// Handler sync, Starts a Sync process, registering a file upload
        /// </summary>
        [BlendFarmHeader("sync")]
        public SyncResponse Packet_Sync(SyncRequest req)
        {
            try
            {
                if (!sessions.Contains(req.SessionID))
                    sessions.Add(req.SessionID);

                SessionData session = SessionData.GetOrCreate(req.SessionID);

                string uploadID = Guid.NewGuid().ToString();
                if (req.FileID != session.FileID)
                {
                    //Directory.CreateDirectory(SystemInfo.RelativeToApplicationDirectory(ServerSettings.Instance.BlenderFiles));
                    Directory.CreateDirectory(ServerSettings.Instance.GetBlenderFilesPath());
                    _uploads.Add(uploadID, new FileUpload(session.GetBlendFilePath(), req, req.Compression));
                    session.UpdatingFile();
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
                    Success = true,
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
            try
            {
                FileUpload upload = _uploads.ContainsKey(req.UploadID) ? _uploads[req.UploadID] : null;
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
            try
            {
                FileUpload upload = _uploads.ContainsKey(complete.UploadID) ? _uploads[complete.UploadID] : null;
                if (upload == null)
                    return new SyncCompleteResponse()
                    {
                        Success = false
                    };
                _uploads.Remove(complete.UploadID);
                lock (upload)
                {

                    upload.FinalWrite();

                    SyncRequest obj = upload.GetContext<SyncRequest>();
                    string fileName = upload.TargetPath;
                    upload.Dispose();

                    SessionData session = SessionData.GetOrCreate(obj.SessionID);

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
        /// Handler isBusy, Checks if RenderNode is busy
        /// </summary>
        [BlendFarmHeader("isBusy")]
        public IsBusyResponse Packet_IsBusy(IsBusyRequest req)
        {
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
            if (!_blender.TryPrepare(req.Version))
                return new PrepareResponse()
                {
                    Message = $"Failed to prepare version {req.Version}",
                    Success = false
                };

            return new PrepareResponse()
            {
                Success = true
            };
        }

        /// <summary>
        /// Handler renderBatch, render multiple requests using a single Blender instance
        /// </summary>
        [BlendFarmHeader("renderBatch")]
        public RenderBatchResponse Packet_RenderBatch(RenderBatchRequest req)
        {
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

                BlenderRenderSettings[] batch = req.Settings.Select(x => BlenderRenderSettings.FromRenderSettings(x)).ToArray();

                DateTime lastUpdate = DateTime.Now;

                List<string> exceptions = new List<string>();

                //Render
                List<string> files = _blender.RenderBatch(req.Version, filePath, batch,
                    (process) =>
                    {
                        process.OnBlenderStatus += (status)=>
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
                        process.OnBlenderCompleteTask += (taskID) =>
                        {
                            BlenderRenderSettings settings = batch.FirstOrDefault(x => x.TaskID == taskID);
                            if (settings != null)
                            {
                                SendPacket(new RenderBatchResult()
                                {
                                    Data = File.ReadAllBytes(settings.Output),
                                    Success = true,
                                    TaskID = settings.TaskID
                                });
                            }
                        };
                        process.OnBlenderException += (excp) => exceptions.Add(excp);
                    });

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
                    //Cleanup
                    //string data = Convert.ToBase64String(File.ReadAllBytes(file));
                    foreach (string file in files)
                        File.Delete(file);
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
        }

        /// <summary>
        /// Handler render, Render a single request
        /// </summary>
        [BlendFarmHeader("render")]
        public RenderResponse Packet_Render(RenderRequest req)
        {
            if(!_blender.IsVersionAvailable(req.Version))
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
                string file = _blender.Render(req.Version, filePath, 
                    BlenderRenderSettings.FromRenderSettings(req.Settings), 
                    (process)=>
                    {
                        process.OnBlenderStatus += (state) =>
                        {
                            if (DateTime.Now.Subtract(lastUpdate).TotalMilliseconds > UPDATE_TIMING_MS)
                            {
                                lastUpdate = DateTime.Now;
                                SendPacket(new RenderInfoResponse()
                                {
                                    TaskID = req.TaskID,
                                    TilesFinished = state.TilesFinish,
                                    TilesTotal = state.TilesTotal,
                                    Time = state.Time,
                                    TimeRemaining = state.TimeRemaining
                                });
                            }
                        };
                        process.OnBlenderException += (excp) => exceptions.Add(excp);
                    });

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
            _blender.Cancel();
        }
        #endregion

    }
}
