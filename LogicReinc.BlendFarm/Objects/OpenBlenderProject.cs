using Avalonia.Media.Imaging;
using LogicReinc.BlendFarm.Shared;
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

        //Render Properties
        public int RenderWidth { get; set; } = 1280;
        public int RenderHeight { get; set; } = 720;
        public int ChunkSize { get; set; } = 256;
        public int Samples { get; set; } = 32;
        public string Denoiser { get; set; } = "Inherit";

        public bool UseWorkaround { get; set; } = true;

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

        private Bitmap _lastBitmap = null;
        public Bitmap LastBitmap
        {
            get
            {
                return _lastBitmap;
            }
            set
            {
                _lastBitmap = value;
                OnBitmapChanged?.Invoke(this, value);
            }
        }

        public bool IsRendering => CurrentTask != null;
        public RenderTask CurrentTask { get; private set; }


        public event Action<OpenBlenderProject, Bitmap> OnBitmapChanged;
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
