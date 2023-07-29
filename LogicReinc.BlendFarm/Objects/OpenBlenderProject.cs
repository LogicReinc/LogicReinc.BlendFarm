using Avalonia.Media.Imaging;
using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace LogicReinc.BlendFarm.Objects
{

    public class OpenBlenderProject : INotifyPropertyChanged
    {
        public string SessionID { get; set; } = Guid.NewGuid().ToString();
        public string FileID { get; set; } = Guid.NewGuid().ToString();
        public string BlendFile { get; set; }
        public string Name => BlendFile != null ? Path.GetFileNameWithoutExtension(BlendFile) : "Unknown?";

        //Networked Path
        private bool _useNetworkedPath = false;
        public bool UseNetworkedPath
        {
            get
            {
                return _useNetworkedPath;
            }
            set
            {
                bool changed = _useNetworkedPath != value;
                _useNetworkedPath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UseNetworkedPath)));
                if (changed)
                    OnNetworkedChanged?.Invoke(this, value);
            }
        }
        public string NetworkPathWindows { get; set; }
        public string NetworkPathLinux { get; set; }
        public string NetworkPathMacOS { get; set; }

        //Render Properties
        public int RenderWidth { get; set; } = 1280;
        public int RenderHeight { get; set; } = 720;
        public int ChunkSize { get; set; } = 256;
        public int Samples { get; set; } = 32;
        public string Denoiser { get; set; } = "Inherit";
        public EngineType Engine { get; set; } = EngineType.Cycles;
        public string RenderFormat { get; set; } = "PNG";

        public bool UseWorkaround { get; set; } = true;

        public string Scene { get; set; } = "";
        public string Camera { get; set; } = "";
        public string AnimationFileFormat { get; set; } = "#.png";
        public int FrameStart { get; set; } = 0;
        public int FrameEnd { get; set; } = 60;
        public int FPS { get; set; } = 0;
        private bool _useFPS = false;
        public bool UseFPS
        {
            get => _useFPS;
            set
            {
                bool old = _useFPS;
                _useFPS = value;
                //RaisePropertyChanged(UseFPSProperty, old, value);
            }
        }

        private Bitmap _lastImage = null;
        public Bitmap LastImage
        {
            get
            {
                return _lastImage;
            }
            set
            {
                _lastImage = value;
                OnBitmapChanged?.Invoke(this, value);
            }
        }

        public bool IsRendering => CurrentTask != null;
        public RenderTask CurrentTask { get; private set; }

        public List<string> CamerasAvailable { get; private set; } = new List<string>();
        public List<string> ScenesAvailable { get; private set; } = new List<string>();

        public List<FileDependency> Dependencies { get; private set; } = null;



        public event Action<OpenBlenderProject, Bitmap> OnBitmapChanged;
        public event Action<OpenBlenderProject, bool> OnNetworkedChanged;
        public event PropertyChangedEventHandler PropertyChanged;
        public OpenBlenderProject(string blendfile, string sessionID = null)
        {
            BlendFile = blendfile;
            if (sessionID != null)
                SessionID = sessionID;

            if (BlendFarmSettings.Instance.UISettings != null)
            {
                try
                {
                    ApplyUISettings(BlendFarmSettings.Instance.UISettings);
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Failed to apply default settings..due to {ex.Message}. Ignoring defaults");
                }
            }
        }

        public void SetRenderTask(RenderTask task)
        {
            CurrentTask = task;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTask)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRendering)));
        }

        public void SetWindowsNetworkPath(string path)
        {
            NetworkPathWindows = path;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NetworkPathWindows)));
        }
        public void SetLinuxNetworkPath(string path)
        {
            NetworkPathLinux = path;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NetworkPathLinux)));
        }
        public void SetMacOSNetworkPath(string path)
        {
            NetworkPathMacOS = path;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NetworkPathMacOS)));
        }


        public void ApplyUISettings(UISettings settings)
        {
            FrameStart = settings?.FrameStart ?? 0;
            FrameEnd = settings?.FrameEnd ?? 60;
            RenderHeight = settings?.RenderHeight ?? 1280;
            RenderWidth = settings?.RenderWidth ?? 720;
            ChunkSize = settings?.ChunkSize ?? 256;
            Denoiser = settings?.Denoiser ?? "Inherit";
            UseWorkaround = settings?.UseWorkaround ?? true;
            FPS = settings?.FPS ?? 0;
            Samples = settings?.Samples ?? 32;
        }
        public void SaveAsDefault()
        {
            BlendFarmSettings.Instance.UISettings = new UISettings()
            {
                FrameStart = FrameStart,
                FrameEnd = FrameEnd,
                RenderHeight = RenderHeight,
                RenderWidth = RenderWidth,
                ChunkSize = ChunkSize,
                Denoiser = Denoiser,
                UseWorkaround = UseWorkaround,
                FPS = FPS,
                Samples = Samples
            };
            BlendFarmSettings.Instance.Save();
        }


        public BlendFarmSettings.UIProjectSettings GetProjectSettings()
        {
            return new BlendFarmSettings.UIProjectSettings()
            {
                UseNetworked = UseNetworkedPath,
                NetworkPathWindows = NetworkPathWindows,
                NetworkPathLinux = NetworkPathLinux,
                NetworkPathMacOS = NetworkPathMacOS
            };
        }


        internal void TriggerPropertyChange(params string[] props)
        {
            foreach(string prop in props)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public class UISettings
        {
            public int FrameStart { get; set; }
            public int FrameEnd { get; set; }
            public int RenderHeight { get; set; }
            public int RenderWidth { get; set; }
            public int ChunkSize { get; set; }
            public int Samples { get; set; }
            public int FPS { get; set; }
            public string Denoiser { get; set; }
            public bool UseWorkaround { get; set; }
        }

    }
}
