using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using static LogicReinc.BlendFarm.BlendFarmSettings;

namespace LogicReinc.BlendFarm.Windows
{
    /// <summary>
    /// Assumes only one on startup
    /// </summary>
    public class ProjectWindow : Window
    {
        static List<BlenderVersion> versions = null;

        private TextBox fileSelection = null;
        private ComboBox comboVersions = null;
        private ListBox history = null;

        private bool _startedNew = false;

        private BlendFarmManager _manager = null;

        private Dictionary<string, (string,string,int)> _previouslyFoundNodes = new Dictionary<string, (string, string, int)>();

        public ProjectWindow()
        {
            versions = BlenderVersion.GetBlenderVersions();
            DataContext = this;

            LocalServer.OnServerException += (a, b) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MessageWindow.Show(this, "Local Server Failure", 
                        $@"Local server failed to start, if you're already using a port, change it in settings. 
Or if you're running this program twice, ignore I guess. 
(TCP: {ServerSettings.Instance.Port}, UDP: {ServerSettings.Instance.BroadcastPort})");
                });
            };
            LocalServer.OnBroadcastException += (a, b) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MessageWindow.Show(this, "Local Broadcast Failure",
                        $@"Local Server failed to broadcast or receive broadcasts for auto-discovery.  It can be changed in settings.
This may have to do with the port being in use. Note that to discover other pcs their broadcast port needs to be the same..
(TCP: {ServerSettings.Instance.Port}, UDP: {ServerSettings.Instance.BroadcastPort})");
                });
            };
            LocalServer.OnDiscoveredServer += (name, address, port) =>
            {
                if (_manager != null)
                    _manager.TryAddDiscoveryNode(name, address, port);
            };
            LocalServer.Start();
            Closed += (a, b) =>
            {
                if (!_startedNew)
                    LocalServer.Stop();
            };

            this.InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            Width = 600;
            Height = 570;

            fileSelection = this.FindControl<TextBox>("fileSelect");

            comboVersions = this.FindControl<ComboBox>("versionSelect");
            comboVersions.Items = versions;
            comboVersions.SelectedIndex = 0;

            history = this.FindControl<ListBox>("history");
            history.Items = BlendFarmSettings.Instance.History.ToList();
            history.SelectedItem = null;
            history.SelectionChanged += (a, b) =>{
                if (b.AddedItems.Count > 0)
                {
                    string list = ((HistoryEntry)b.AddedItems[0]).Path;
                    //history.SelectedItems = null;

                    fileSelection.Text = list;
                }
            };
        }

        public async void ShowFileDialog()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filters.Add(new FileDialogFilter()
            {
                Name = "Blender File",
                Extensions = { "blend" }
            });
            dialog.AllowMultiple = false;
            string[] results = await dialog.ShowAsync(this);
            if(results.Length > 0)
            {
                fileSelection.Text = results[0];
            }
        }

        /// <summary>
        /// Assumes only one call
        /// </summary>
        public void LoadProject()
        {
            string file = fileSelection.Text;
            BlenderVersion version = (BlenderVersion)comboVersions.SelectedItem;

            if (!File.Exists(file))
            {
                MessageWindow.Show(this, "File not found", $"{file} does not exist");
                return;
            }

            string path = Path.GetFullPath(file);

            HistoryEntry entry = BlendFarmSettings.Instance.History.FirstOrDefault(x => x.Path == path);
            if (entry != null)
                BlendFarmSettings.Instance.History.Remove(entry);

            BlendFarmSettings.Instance.History.Add(new BlendFarmSettings.HistoryEntry()
            {
                Name = Path.GetFileName(file),
                Path = file,
                Date = DateTime.Now
            });
            BlendFarmSettings.Instance.History = BlendFarmSettings.Instance.History.OrderByDescending(x => x.Date).Take(10).ToList();
            BlendFarmSettings.Instance.Save();

            _startedNew = true;


            //Setup manager
            _manager = new BlendFarmManager(path, version.Name, null, BlendFarmSettings.Instance.LocalBlendFiles);

            if(!BlendFarmSettings.Instance.PastClients.Any(x=>x.Key == BlendFarmManager.LocalNodeName))
                _manager.AddNode(BlendFarmManager.LocalNodeName, $"localhost:{LocalServer.ServerPort}");

            foreach (var pair in BlendFarmSettings.Instance.PastClients.ToList())
                _manager.AddNode(pair.Key, pair.Value.Address, pair.Value.RenderType);

            //Start render window
            new RenderWindow(_manager, version, path).Show();

            this.Close();
        }
    }
}
