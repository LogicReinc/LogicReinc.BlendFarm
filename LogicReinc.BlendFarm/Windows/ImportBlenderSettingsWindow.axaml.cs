using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Markup.Xaml;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Objects;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static LogicReinc.BlendFarm.BlendFarmSettings;

namespace LogicReinc.BlendFarm.Windows
{
    public class ImportBlenderSettingsWindow : Window
    {

        public BlenderImportSettings Settings { get; private set; } = new();

        public ImportBlenderSettingsWindow()
        {
            Settings.UseWidth = true;
            Settings.UseHeight = true;
            Settings.UseFrameStart = true;
            Settings.UseFrameEnd = true;
            Settings.UseSamples = true;
            Settings.UseEngine = true;
            Settings.UseCameras = true;
            DataContext = this;
            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            Width = 300;
            Height = 300;
            MinHeight = 300;
            MaxHeight = 300;
            MinWidth = 300;
            MaxWidth = 300;
        }


        public async void Import()
        {
            RenderWindow renderer = ((RenderWindow) this.Owner);
            await renderer.SyncAll();
            RenderNode node = renderer.Manager.Nodes.First();
            if (node != null && node.Connected)
            {
                ImportSettingsResponse result = await node.ImportSettings(new ImportSettingsRequest()
                {
                    Settings = this.Settings,
                    Version = renderer.Manager.Version,
                    File = renderer.CurrentProject.BlendFile
                });
                OpenBlenderProject project = renderer.CurrentProject;
                project.RenderHeight = result.Settings.UseHeight ? result.Settings.Height : project.RenderHeight;
                project.RenderWidth = result.Settings.UseWidth ? result.Settings.Width : project.RenderWidth;
                project.Samples = result.Settings.UseSamples ? result.Settings.Samples : project.Samples;
                project.Engine = result.Settings.UseEngine ? result.Settings.Engine : project.Engine;
                project.FrameStart = result.Settings.UseFrameStart ? result.Settings.FrameStart : project.FrameStart;
                project.FrameEnd = result.Settings.UseFrameEnd ? result.Settings.FrameEnd : project.FrameEnd;
                project.TriggerPropertyChange(nameof(project.RenderHeight));
                project.TriggerPropertyChange(nameof(project.RenderWidth));
                project.TriggerPropertyChange(nameof(project.Samples));
                project.TriggerPropertyChange(nameof(project.Engine));
                project.TriggerPropertyChange(nameof(project.FrameStart));
                project.TriggerPropertyChange(nameof(project.FrameEnd));
                this.Close();
            }
        }

        public static new async Task Show(Window owner)
        {
            var window = new ImportBlenderSettingsWindow();

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            await window.ShowDialog(owner);
        }
    }
}
