using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static LogicReinc.BlendFarm.BlendFarmSettings;

namespace LogicReinc.BlendFarm.Windows
{
    public class DeviceLogWindow : Window
    {
        private ScrollViewer _scroller;
        private TextBlock _log;

        public RenderNode Node { get; set; }

        public DeviceLogWindow()
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
        public DeviceLogWindow(RenderNode node)
        {
            Node = node;
            DataContext = this;
            this.InitializeComponent();

            this.Title = $"Console Output from {node.Name} ({node.Address})";

            _log.Text = node.GetCurrentLog();
            node.OnLog += HandleNewLog;
            Closing += (a, b) =>
            {
                node.OnLog -= HandleNewLog;
            };

            node.RequestConsoleActivityRedirect();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _scroller = this.Find<ScrollViewer>("scroller");
            _log = this.Find<TextBlock>("log");

            Width = 600;
            Height = 400;
            MinHeight = 400;
            MinWidth = 600;
        }

        private void HandleNewLog(RenderNode node, string log)
        {
            Dispatcher.UIThread.InvokeAsync(()=> {
                _log.Text = _log.Text + "\n" + log;
                _scroller.ScrollToEnd();
            });
        }

        public static void Show(Window owner, RenderNode node)
        {
            var window = new DeviceLogWindow(node);

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            window.Show(owner);
        }
    }
}
