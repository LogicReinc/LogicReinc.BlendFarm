using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for syncing

    public enum Compression
    {
        Raw = 0,
        GZip = 1
    }

    [BlendFarmHeader("sync")]
    public class SyncRequest : BlendFarmMessage
    {
        public string SessionID { get; set; }
        public long FileID { get; set; }
        public Compression Compression { get; set; }
    }

    [BlendFarmHeader("syncNetwork")]
    public class SyncNetworkRequest : BlendFarmMessage
    {
        public string SessionID { get; set; }
        public long FileID { get; set; }
        public string WindowsPath { get; set; }
        public string LinuxPath { get; set; }
        public string MacOSPath { get; set; }
    }

    [BlendFarmHeader("syncResp")]
    public class SyncResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public bool SameFile { get; set; }
        public string Message { get; set; }
        public string UploadID { get; set; }
    }

    [BlendFarmHeader("syncUpload")]
    public class SyncUploadRequest : BlendFarmMessage
    {
        public string UploadID { get; set; }
        public byte[] Data { get; set; }
        public string Hash { get; set; }
    }
    [BlendFarmHeader("syncUploadResp")]
    public class SyncUploadResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    [BlendFarmHeader("syncComplete")]
    public class SyncCompleteRequest : BlendFarmMessage
    {
        public string UploadID { get; set; }
    }
    [BlendFarmHeader("syncCompleteResp")]
    public class SyncCompleteResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
