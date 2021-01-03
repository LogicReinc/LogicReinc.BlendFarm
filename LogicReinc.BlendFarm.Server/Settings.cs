using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LogicReinc.BlendFarm.Server
{
    public class Settings
    {
        private const string SETTINGS_PATH = "Settings";

        /// <summary>
        /// Port to use for communication (May be blocked by firewall)
        /// </summary>
        public int Port { get; set; } = 15000;

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
        private static Settings _instance = null;
        public static Settings Instance
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
            File.WriteAllText(SETTINGS_PATH, JsonSerializer.Serialize(this));
        }
        public static Settings Load()
        {
            if (File.Exists(SETTINGS_PATH))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText("Settings"));
            else
                return new Settings();
        }
        #endregion
    }
}
