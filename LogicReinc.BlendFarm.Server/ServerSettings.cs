using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LogicReinc.BlendFarm.Server
{
    public class ServerSettings
    {
        private const string SETTINGS_PATH = "ServerSettings";

        /// <summary>
        /// Port to use for communication (May be blocked by firewall)
        /// </summary>
        public int Port { get; set; } = 15000;
        /// <summary>
        /// Port to use for broadcasting (May be blocked by firewall),
        /// -1 generally implies no broadcasting
        /// </summary>
        public int BroadcastPort { get; set; } = 16342;

        /// <summary>
        /// Used for storing different versions of Blender
        /// </summary>
        public string BlenderData = "BlenderData";
        /// <summary>
        /// Used for storing temporary render data
        /// </summary>
        public string RenderData = "RenderData";
        /// <summary>
        /// Used for storing temporary .blend files (sessions)
        /// </summary>
        public string BlenderFiles = "BlenderFiles";

        /// <summary>
        /// Prevents software from replacing the .py if it changed
        /// </summary>
        public bool BypassScriptUpdate { get; set; } = false;



        #region Boilerplate
        private static ServerSettings _instance = null;
        public static ServerSettings Instance
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
            File.WriteAllText(SystemInfo.RelativeToApplicationDirectory(SETTINGS_PATH), JsonSerializer.Serialize(this));
        }
        public static ServerSettings Load()
        {
            string path = SystemInfo.RelativeToApplicationDirectory(SETTINGS_PATH);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(path));
            else
                return new ServerSettings();
        }
        #endregion
    }
}
