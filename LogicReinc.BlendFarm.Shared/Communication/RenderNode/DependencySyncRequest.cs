using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for syncing dependencies

    [BlendFarmHeader("depSync")]
    public class DependencySyncRequest : BlendFarmMessage
    {
        public string SessionID { get; set; }
        public long DependencyFileID { get; set; }
        public string FileName { get; set; }
    }

    [BlendFarmHeader("depSyncResp")]
    public class DependencySyncResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public bool SameFile { get; set; }
        public string Message { get; set; }
        public string UploadID { get; set; }
    }

    [BlendFarmHeader("depSyncUpload")]
    public class DependencySyncUploadRequest : BlendFarmMessage
    {
        public string UploadID { get; set; }
        public byte[] Data { get; set; }
        public string Hash { get; set; }
        public int DataSize { get; set; }
        public long TotalSize { get; set; }
    }
    [BlendFarmHeader("depSyncUploadResp")]
    public class DependencySyncUploadResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    [BlendFarmHeader("depSyncComplete")]
    public class DependencySyncCompleteRequest : BlendFarmMessage
    {
        public string UploadID { get; set; }
    }
    [BlendFarmHeader("depSyncCompleteResp")]
    public class DependencySyncCompleteResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
