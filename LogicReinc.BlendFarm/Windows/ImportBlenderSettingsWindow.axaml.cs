using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Objects;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Collections.Generic;
using System;
using Avalonia.Threading;

namespace LogicReinc.BlendFarm.Windows
{
    public class ImportBlenderSettingsWindow : Window, INotifyPropertyChanged
    {

        public BlenderImportSettings Settings { get; private set; } = new();

        public string ButtonText { get; set; } = "Import";

        public bool IsImporting => ButtonText != "Import";

        public event PropertyChangedEventHandler PropertyChanged;

        public ImportBlenderSettingsWindow()
        {
            //Everything starts selected by default
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
            ButtonText = "Importing";
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ButtonText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsImporting)));
            });
            RenderWindow renderer = ((RenderWindow)this.Owner);
            List<RenderNode> conNodes = renderer.Manager.Nodes.Where((x) => x.Connected).ToList();
            renderer.Manager.DisconnectAll();
            RenderNode node = renderer.Manager.Nodes[0];
            if (node == null)
            {
                Console.WriteLine("At least one node must exist and be able to open Blender for importing to work.");
                return;
            }
            foreach(RenderNode possibleNode in renderer.Manager.Nodes) {
                if(await renderer.Manager.ConnectAndPrepare(possibleNode.Name))
                {
                    node = possibleNode;
                    break;
                }
                
            }
            if (!node.Connected)
            {
                Console.WriteLine("Could not connect to any nodes. They must be reachable in order to import settings.");
                return;
            }
            await renderer.Manager.Sync(renderer.CurrentProject.BlendFile);
            BlendFarmFileSession session = renderer.Manager.GetOrCreateSession(renderer.CurrentProject.BlendFile);
            Console.WriteLine(session.LocalBlendFile);
            ImportSettingsResponse result = await node.ImportSettings(new ImportSettingsRequest()
            {
                Settings = this.Settings,
                Version = renderer.Manager.Version,
                sessionID = session.SessionID
            }) ;
            OpenBlenderProject project = renderer.CurrentProject;

            //Is there a better way to do this? I don't have enough experience to know...
            if (result.Settings != null)
            {
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

                renderer.CameraOptions = result.Settings.UseCameras ? result.Settings.Cameras : renderer.CameraOptions;
            }

            node.Disconnect();
            conNodes.ForEach(node => renderer.Manager.Connect(node.Name));
            this.Close();

        }

        public static new async Task Show(Window owner)
        {
            var window = new ImportBlenderSettingsWindow();

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            await window.ShowDialog(owner);
        }
    }
}
