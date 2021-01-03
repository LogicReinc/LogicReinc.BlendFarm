using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

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
        /// Previously used blend files
        /// </summary>
        public List<HistoryEntry> History { get; set; } = new List<HistoryEntry>();
        /// <summary>
        /// Clients from previous sessions
        /// </summary>
        public Dictionary<string, HistoryClient> PastClients { get; set; } = new Dictionary<string, HistoryClient>();


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

        public void Save()
        {
            File.WriteAllText(FILE_NAME, JsonSerializer.Serialize(this));
        }
        public static BlendFarmSettings Load()
        {
            if (File.Exists(FILE_NAME))
                return JsonSerializer.Deserialize<BlendFarmSettings>(File.ReadAllText(FILE_NAME));
            else
                return new BlendFarmSettings();
        }
        #endregion


        public class HistoryClient
        {
            public string Name { get; set; }
            public string Address { get; set; }

            public RenderType RenderType { get; set; } = RenderType.CPU;
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
    }
}
