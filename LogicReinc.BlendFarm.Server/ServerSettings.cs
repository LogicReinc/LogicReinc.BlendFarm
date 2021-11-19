using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LogicReinc.BlendFarm.Server
{
    public class ServerSettings
    {
        private const string SETTINGS_PATH = "ServerSettings.json";

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
        public string BlenderData { get; set; } = "BlenderData";

        /// <summary>
        /// False if a non-relative path for the blender installs should be used
        /// </summary>
        public bool BlenderDataRelative { get; set; } = true;

        /// <summary>
        /// Used for storing temporary render data
        /// </summary>
        public string RenderData { get; set; } = "RenderData";

        /// <summary>
        /// False if a non-relative path for the render data should be used
        /// </summary>
        public bool RenderDataRelative { get; set; } = true;

        /// <summary>
        /// Used for storing temporary .blend files (sessions)
        /// </summary>
        public string BlenderFiles { get; set; } = "BlenderFiles";

        /// <summary>
        /// False if a non-relative path for the blender files should be used
        /// </summary>
        public bool BlenderFilesRelative { get; set; } = true;

        /// <summary>
        /// Determines if blender files should be deleted when the server exits
        /// </summary>
        public bool DeleteBlenderFilesOnExit { get; set; } = true;

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
            File.WriteAllText(SystemInfo.RelativeToApplicationDirectory(SETTINGS_PATH), JsonSerializer.Serialize(this, new JsonSerializerOptions() { WriteIndented = true }));
        }
        public static ServerSettings Load()
        {
            string path = SystemInfo.RelativeToApplicationDirectory(SETTINGS_PATH);
            if (File.Exists(path))
                return JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(path));
            else
            {
                ServerSettings settings = new ServerSettings();
                settings.Save();
                return settings;
               // return new ServerSettings();
            }
        }
        #endregion

        public string GetBlenderDataPath()
		{
            return BlenderDataRelative ? SystemInfo.RelativeToApplicationDirectory(BlenderData) : BlenderData;
        }

        public string GetRenderDataPath()
        {
            return RenderDataRelative ? SystemInfo.RelativeToApplicationDirectory(RenderData) : RenderData;
        }

        public string GetBlenderFilesPath()
        {
            return BlenderFilesRelative ? SystemInfo.RelativeToApplicationDirectory(BlenderFiles) : BlenderFiles;
        }
    }
}
