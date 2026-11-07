using LogicReinc.BlendFarm.Objects;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using static LogicReinc.BlendFarm.BlendFarmSettings;
using static LogicReinc.BlendFarm.Windows.RenderWindow;

namespace LogicReinc.BlendFarm
{
    public class BlendFarmSettings
    {
        private static string FILE_NAME = "ClientSettings";


        /// <summary>
        /// Used to store copies of used blend files
        /// </summary>
        public string LocalBlendFiles { get; set; } = "LocalBlendFiles";


        /// <summary>
        /// Determines if the application should listen for servers over udp broadcasts
        /// </summary>
        public bool ListenForBroadcasts { get; set; } = true;

        public OpenBlenderProject.UISettings UISettings { get; set; } = null;

        /// <summary>
        /// Previously used version
        /// </summary>
        public string LastVersion { get; set; }

        /// <summary>
        /// Previously used blend files
        /// </summary>
        public List<HistoryEntry> History { get; set; } = new List<HistoryEntry>();
        /// <summary>
        /// Clients from previous sessions
        /// </summary>
        public Dictionary<string, HistoryClient> PastClients { get; set; } = new Dictionary<string, HistoryClient>();

        public DateTime LastAnnouncementDate { get; set; } = DateTime.MinValue;

        public Dictionary<string, UIProjectSettings> ProjectSettings { get; set; } = new Dictionary<string, UIProjectSettings>();


        public bool Option_UseAssetsSync { get; set; }
        public bool Option_ConnectLocal { get; set; } = true;
        public bool Option_ImportSettings { get; set; } = true;

        /// <summary>
        /// Render-completion notification preferences
        /// </summary>
        public bool NotificationsEnabled { get; set; } = true;
        public bool WebhookEnabled { get; set; } = false;
        public string WebhookUrl { get; set; } = "";

        /// <summary>
        /// Persisted render queue (only items that were still queued when saved)
        /// </summary>
        public List<QueueItemDTO> RenderQueue { get; set; } = new List<QueueItemDTO>();


        public void ApplyProjectSettings(string fileName, UIProjectSettings settings)
        {
            if (ProjectSettings.ContainsKey(fileName))
                ProjectSettings[fileName] = settings;
            else
                ProjectSettings.Add(fileName, settings);
            Save();
        }
        public UIProjectSettings GetProjectSettings(string file)
        {
            if (ProjectSettings.ContainsKey(file))
                return ProjectSettings[file];
            return null;
        }


        #region Boilerplate

        private static BlendFarmSettings _instance = null;
        public static BlendFarmSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        private static readonly object _saveLock = new object();
        public void Save()
        {
            string path = AppPaths.Resolve(FILE_NAME);
            //Write to a temp file first so an interrupted save can't leave a corrupt settings file
            lock (_saveLock)
            {
                //Serialize inside the lock so concurrent saves can't interleave a half-updated snapshot
                string json = JsonSerializer.Serialize(this);
                File.WriteAllText(path + ".tmp", json);
                File.Move(path + ".tmp", path, true);
            }
        }
        /// <summary>
        /// Save that never throws (logs instead), for callers where a failed save must not break the UI
        /// </summary>
        public bool TrySave()
        {
            try
            {
                Save();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save {FILE_NAME}: {ex.Message}");
                return false;
            }
        }
        public static BlendFarmSettings Load()
        {
            string path = AppPaths.Resolve(FILE_NAME);
            if (!File.Exists(path))
                return new BlendFarmSettings();
            try
            {
                return JsonSerializer.Deserialize<BlendFarmSettings>(File.ReadAllText(path)) ?? new BlendFarmSettings();
            }
            catch (Exception ex)
            {
                //Keep the broken file for recovery and start with defaults instead of failing to launch
                Console.WriteLine($"Failed to load {FILE_NAME}, using defaults: {ex.Message}");
                try { File.Copy(path, path + ".corrupt", true); } catch { }
                return new BlendFarmSettings();
            }
        }
        #endregion


        public class HistoryClient
        {
            public string Name { get; set; }
            public string Address { get; set; }

            public RenderType RenderType { get; set; } = RenderType.OPTIX_GPUONLY;
            public double Performance { get; set; }
            //Auto benchmark score (pixels/ms); restored so work distribution survives restarts.
            public decimal PerformanceScorePP { get; set; }
            public string Pass { get; set; }
        }

        /// <summary>
        /// Used to keep track of previously used blend files
        /// </summary>
        public class HistoryEntry
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public DateTime Date { get; set; }
        }



        public class UIProjectSettings
        {
            public bool UseNetworked { get; set; }
            public string NetworkPathWindows { get; set; }
            public string NetworkPathLinux { get; set; }
            public string NetworkPathMacOS { get; set; }
        }
    }
}
