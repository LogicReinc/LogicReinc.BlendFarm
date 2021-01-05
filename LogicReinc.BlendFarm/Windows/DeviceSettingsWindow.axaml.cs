using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static LogicReinc.BlendFarm.BlendFarmSettings;

namespace LogicReinc.BlendFarm.Windows
{
    public class DeviceSettingsWindow : Window
    {
        private ComboBox selectRenderType = null;

        public RenderType[] RenderTypes { get; } = (RenderType[])Enum.GetValues(typeof(RenderType));
        public RenderNode Node { get; set; }

        public DeviceSettingsWindow()
        {
            Node = new RenderNode()
            {
                Name = "Some Device Name",
                Activity = "SomeActivity",
                Cores = 16,
                ComputerName = "SomeDesktopName",
                OS = "windows64",
                RenderType = RenderType.CPU,
                Address = "192.168.1.123:15000"
            };
            DataContext = this;
            this.InitializeComponent();
        }
        public DeviceSettingsWindow(RenderNode node)
        {
            Node = node;
            DataContext = this;
            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            selectRenderType = this.Find<ComboBox>("selectRenderType");
            selectRenderType.SelectedItem = Node.RenderType;
            Width = 300;
            Height = 300;
            MinHeight = 300;
            MaxHeight = 300;
            MinWidth = 300;
            MaxWidth = 300;
        }


        public async void Save()
        {
            HistoryClient entry = BlendFarmSettings.Instance.PastClients?.FirstOrDefault(x => x.Key == Node.Name).Value;
            if(entry == null)
            {
                if (!await YesNoWindow.Show(this, "Node not saved yet", "The node was not yet saved, would you like to save it?"))
                    return;
                else
                {
                    entry = new HistoryClient()
                    {
                        Name = Node.Name,
                        Address = Node.Address,
                        RenderType = Node.RenderType
                    };
                    BlendFarmSettings.Instance.PastClients.Add(Node.Name, entry);
                }
            }
            Node.RenderType = ((RenderType)selectRenderType.SelectedItem);
            entry.RenderType = Node.RenderType;
            BlendFarmSettings.Instance.Save();
        }

        public static async Task Show(Window owner, RenderNode node)
        {
            var window = new DeviceSettingsWindow(node);

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            await window.ShowDialog(owner);
        }
    }
}
