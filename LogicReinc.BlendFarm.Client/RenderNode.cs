﻿using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client
{
    /// <summary>
    /// Top-level class to interact with an individual node
    /// Also has some PropertyChanged events for UI (not all)
    /// </summary>
    public class RenderNode : INotifyPropertyChanged
    {
        //Info
        public string Name { get; set; }
        public string Address { get; set; }
        public string Status { get; set; }
        public string OS { get; set; }

        private RenderClient Client { get; set; }

        //ClientDerived
        public string ComputerName { get; set; }
        public int Cores { get; set; } = -1;

        public double Performance { get; set; } = 0;
        public decimal PerformanceScorePP { get; set; } = 0;

        public RenderType RenderType { get; set; } = RenderType.CPU;

        //State
        public bool IsIdle => string.IsNullOrEmpty(Activity);
        public bool Connected => Client != null && Client.Connected;

        public long LastFileID { get; set; } = -1;
        public string Activity { get; set; } = null;
        public double ActivityProgress { get; set; } = 0;
        public bool HasActivityProgress => ActivityProgress > 0;

        public string Exception { get; set; } = null;
        public string LastStatus { get; set; }

        public bool IsPrepared { get; set; }
        public bool IsSynced { get; set; }

        public string CurrentTask { get; set; }
        private CancellationTokenSource _taskCancelToken = null;

        public List<string> AvailableVersions { get; set; } = new List<string>();


        //Events
        public event Action<RenderNode> OnConnected;
        public event Action<RenderNode> OnDisconnected;

        public event Action<RenderNode, string> OnActivityChanged;
        public event Action<RenderNode, long> OnFileIDChanged;
        public event Action<RenderNode, RenderBatchResult> OnBatchResult;

        public event PropertyChangedEventHandler PropertyChanged;


        public RenderNode()
        {
            OnConnected += (n) => 
                TriggerPropChange(nameof(Connected));
            OnDisconnected += (n) => 
                TriggerPropChange(nameof(Connected));
        }

        public void UpdatePerformance(int pixelsRendered, int ms)
        {
            decimal msPerPixel = (decimal)((decimal)pixelsRendered / ms);
            PerformanceScorePP = msPerPixel;
        }

        //Connection
        /// <summary>
        /// Connects to node and prepare version
        /// </summary>
        public async Task ConnectAndPrepare(string version)
        {
            await Connect();

            if (Connected)
                await PrepareVersion(version);
        }
        /// <summary>
        /// Connect to node
        /// </summary>
        public async Task Connect()
        {
            if (!Connected)
            {
                UpdateActivity("Connecting");

                try
                {
                    Client = await RenderClient.Connect(Address);
                    Client.OnConnected += (a) => OnConnected?.Invoke(this);
                    Client.OnDisconnected += (a) => OnDisconnected?.Invoke(this);
                    Client.OnPacket += HandlePacket;

                    ComputerInfoResponse compData = await GetComputerInfo();
                    OS = compData.OS;
                    Cores = compData.Cores;
                    ComputerName = compData.Name;

                    UpdateException("");
                    OnConnected?.Invoke(this);
                }
                catch(Exception ex)
                {
                    UpdateException(ex.Message);
                    Client = null;
                    throw;
                }
                finally
                {
                    UpdateActivity("");
                }
            }
        }
        /// <summary>
        /// Disconnect from node
        /// </summary>
        public void Disconnect()
        {
            if (Connected)
                Client.Disconnect();
            Client = null;
            UpdateException("");
            TriggerPropChange(nameof(Connected));
        }

        /// <summary>
        /// Handle package from client
        /// </summary>
        private void HandlePacket(RenderClient client, BlendFarmMessage p)
        {
            if (p is RenderInfoResponse)
            {
                RenderInfoResponse renderResp = ((RenderInfoResponse)p);

                double progress = Math.Round(((double)renderResp.TilesFinished / renderResp.TilesTotal) * 100, 1);
                if (renderResp.TaskID == CurrentTask)
                    UpdateActivity($"Rendering ({renderResp.TilesFinished}/{renderResp.TilesTotal})", progress);
            }
            if(p is RenderBatchResult)
            {
                RenderBatchResult renderBatchResult = ((RenderBatchResult)p);

                OnBatchResult?.Invoke(this, renderBatchResult);
               
            }
        }

        //Remote Tasks


        /// <summary>
        /// Prepare a version of blender on node
        /// </summary>
        public async Task<PrepareResponse> PrepareVersion(string version)
        {
            if (Client == null)
                throw new InvalidOperationException("Client not connected");

            UpdateActivity("Downloading Version");

            PrepareResponse resp = await Client.Send<PrepareResponse>(new PrepareRequest()
            {
                Version = version
            }, CancellationToken.None);

            if (resp != null && resp.Success)
            {
                if (!AvailableVersions.Contains(version))
                    AvailableVersions.Add(version);
                UpdatePreparedStatus(true);
            }
            else
                UpdateException($"Failed Version {version}");

            UpdateActivity("");

            return resp;
        }
        /// <summary>
        /// Transfer blender file to node
        /// </summary>
        /// <param name="sess">An identifier for the session</param>
        /// <param name="fileid">An identifier used to differentiate versions</param>
        /// <returns></returns>
        public async Task<SyncResponse> SyncFile(string sess, long fileid, Stream file, Compression compression)
        {
            if (Client == null)
                throw new InvalidOperationException("Client not connected");
            SyncResponse resp = null;
            try
            {
                UpdateActivity("Syncing");

                //Start Sync

                //Initialize Sync
                resp = await Client.Send<SyncResponse>(new SyncRequest()
                {
                    SessionID = sess,
                    FileID = fileid,
                    Compression = compression
                }, CancellationToken.None);

                if (!resp.Success)
                    throw new Exception(resp.Message);
                if (resp.SameFile)
                    return resp;

                //Transfer file
                byte[] chunk = new byte[1024 * 1024 * 10];
                int read = 0;
                int written = 0;
                while ((read = file.Read(chunk, 0, chunk.Length)) > 0)
                {
                    //Send chunk
                    var uploadResp = await Client.Send<SyncUploadResponse>(new SyncUploadRequest()
                    {
                        Data = (read == chunk.Length) ? chunk : chunk.AsSpan(0, read).ToArray(), //Convert.ToBase64String(chunk, 0, read),
                        UploadID = resp.UploadID
                    }, CancellationToken.None);

                    if (!uploadResp.Success)
                        throw new Exception(uploadResp.Message);

                    written += read;

                    double progress = (double)written / file.Length;
                    double p = Math.Round(progress * 100, 1);
                    UpdateActivity($"Syncing ({p}%)", p);
                }

                //Indicate Transfer Complete
                var complete = await Client.Send<SyncCompleteResponse>(new SyncCompleteRequest()
                {
                    UploadID = resp.UploadID
                }, CancellationToken.None);

                //End Sync

                if (await CheckSyncFile(sess, fileid))
                    UpdateSyncedStatus(true);
                else
                    UpdateSyncedStatus(false);
            }
            finally
            {
                UpdateActivity("");
            }

            return resp;
        }
        /// <summary>
        /// Render a batch of RenderSettings
        /// </summary>
        public async Task<RenderBatchResponse> RenderBatch(RenderBatchRequest req)
        {
            if (Client == null)
                throw new InvalidOperationException("Client not connected");
            if (CurrentTask != null)
                throw new InvalidOperationException("Already rendering");

            RenderBatchResponse resp = null;
            _taskCancelToken = new CancellationTokenSource();
            try
            {

                CurrentTask = req.TaskID;

                UpdateActivity("Render Loading..");

                resp = await Client.Send<RenderBatchResponse>(req, _taskCancelToken.Token);
            }
            finally
            {
                UpdateActivity("");
                CurrentTask = null;
                _taskCancelToken = null;
            }
            return resp;
        }
        /// <summary>
        /// Render a single RenderSettings
        /// </summary>
        public async Task<RenderResponse> Render(RenderRequest req)
        {
            if (Client == null)
                throw new InvalidOperationException("Client not connected");
            RenderResponse resp = null;
            _taskCancelToken = new CancellationTokenSource();
            try
            {
                CurrentTask = req.TaskID;

                UpdateActivity("Render Loading..");

                if (Client != null)
                    resp = await Client.Send<RenderResponse>(req, _taskCancelToken.Token);
            }
            finally
            {
                UpdateActivity("");
                CurrentTask = null;
                _taskCancelToken = null;
            }

            return resp;
        }
        
        /// <summary>
        /// Cancels an ongoing render with SesionID
        /// </summary>
        /// <returns></returns>
        public async Task CancelRender(string sessionID)
        {
            if (_taskCancelToken != null)
            {
                _taskCancelToken.Cancel();
                await Task.Run(() =>
                {
                    Client.Send(new CancelRenderRequest()
                    {
                        Session = sessionID
                    });
                });
            }
        }


        //Check Client Data

        /// <summary>
        /// Returns information about this machine
        /// </summary>
        public async Task<ComputerInfoResponse> GetComputerInfo()
        {
            if (!Connected)
                throw new InvalidOperationException("Not connected");

            ComputerInfoResponse resp = await Client.Send<ComputerInfoResponse>(new ComputerInfoRequest(), CancellationToken.None);

            if (resp != null)
            {
                ComputerName = resp.Name;
                Cores = resp.Cores;
                OS = resp.OS;
            }

            return resp;
        }

        public async Task<bool> IsBusy()
        {
            return (await Client.Send<IsBusyResponse>(new IsBusyRequest(), CancellationToken.None))?.IsBusy ?? false;
        }

        /// <summary>
        /// Check if a version is present on node
        /// </summary>
        public async Task<bool> CheckVersion(string version)
        {
            if (Client == null)
                throw new InvalidOperationException("Client not connected");

            if (!AvailableVersions.Contains(version))
            {

                var resp = await Client.Send<IsVersionAvailableResponse>(new IsVersionAvailableRequest()
                {
                    Version = version
                }, CancellationToken.None);
                if (resp.Success)
                {
                    if (!AvailableVersions.Contains(version))
                        AvailableVersions.Add(version);
                    return true;
                }
                return false;
            }
            else
                return true;
        }
        /// <summary>
        /// Check if provided file id is the current version of the file
        /// </summary>
        /// <param name="sess">An identifier for the session</param>
        /// <param name="id">An identifier for the file(.blend)</param>
        /// <returns></returns>
        public async Task<bool> CheckSyncFile(string sess, long id)
        {
            if (Client == null)
                throw new InvalidOperationException("Client not connected");


            var resp = await Client.Send<CheckSyncResponse>(new CheckSyncRequest()
            {
                FileID = id,
                SessionID = sess
            }, CancellationToken.None);

            if (resp?.Success ?? false)
            {
                UpdateFileID(id);
                UpdateSyncedStatus(true);
                return true;
            }
            else
                return false;
        }


        //PropertyChanges (generally for UI)
        public void UpdateActivity(string activity, double progress = -1)
        {
            if (Activity != activity)
            {
                ActivityProgress = -1;
                Activity = activity;
                OnActivityChanged?.Invoke(this, activity);
                TriggerPropChange(nameof(Activity), nameof(IsIdle));
            }
            if(ActivityProgress != progress)
            {
                ActivityProgress = progress;
                TriggerPropChange(nameof(ActivityProgress), nameof(HasActivityProgress));
            }
        }
        public void UpdateFileID(long id)
        {
            if (LastFileID != id)
            {
                LastFileID = id;
                OnFileIDChanged?.Invoke(this, id);
                TriggerPropChange(nameof(LastFileID));
            }
        }
        public void UpdateException(string excp)
        {
            if (Exception != excp)
            {
                Exception = excp;
                TriggerPropChange(nameof(Exception));
            }
        }
        public void UpdateLastStatus(string status)
        {
            LastStatus = status;
            TriggerPropChange(nameof(LastStatus));
        }
        public void UpdateSyncedStatus(bool val)
        {
            IsSynced = val;
            TriggerPropChange(nameof(IsSynced));
        }
        public void UpdatePreparedStatus(bool val)
        {
            IsPrepared = val;
            TriggerPropChange(nameof(IsSynced));
        }

        /// <summary>
        /// Trigger PropertyChanged for all provided property names
        /// </summary>
        private void TriggerPropChange(params string[] names)
        {
            foreach (string name in names)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
