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
using System.Reflection;
using System.Threading.Tasks;
using static LogicReinc.BlendFarm.BlendFarmSettings;

namespace LogicReinc.BlendFarm.Windows
{
    /// <summary>
    /// Assumes only one on startup
    /// </summary>
    public class ProjectWindow : Window
    {
        static List<BlenderVersion> versions = new List<BlenderVersion>();

        private TextBox fileSelection = null;
        private ComboBox comboVersions = null;
        private ListBox history = null;

        private bool _startedNew = false;

        private string _os = null;
        private BlendFarmManager _manager = null;

        private Dictionary<string, (string,string,int)> _previouslyFoundNodes = new Dictionary<string, (string, string, int)>();

        private bool _noServer = false;

        public ProjectWindow()
        {
            DataContext = this;
            using(Stream icoStream = Program.GetIconStream())
            {
                this.Icon = new WindowIcon(icoStream);
            }

            try
            {
                _os = SystemInfo.GetOSName();
                _noServer = false;// _os == SystemInfo.OS_MACOS;
            }
            catch(Exception ex)
            {
                Console.WriteLine("No server due to:" + ex.Message);
                _noServer = true;
            }

            if (!_noServer)
            {
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
            }

            this.InitializeComponent();
#if DEBUG
            //this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            Width = 600;
            Height = 570;

            fileSelection = this.FindControl<TextBox>("fileSelect");

            try
            {
                versions = BlenderVersion.GetBlenderVersions(SystemInfo.RelativeToApplicationDirectory("VersionCache"), SystemInfo.RelativeToApplicationDirectory("VersionCustom"));
            }
            catch(Exception ex)
            {
                /*/
                MessageWindow.Show(this, "Exception retrieving versions", 
                    $"Failed to retrieve versions due to {ex.Message}, this may be due to no internet connection.An internet connection is required to retrieve version info and download Blender installations. Restart application with internet connection for Blender versions..",
                    600, 250);

                /*/
            }

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

            if(_noServer)
                MessageWindow.Show(this, "OSX Rendering", "Rendering using Blender is disabled for OSX due to it not being implemented fully yet. You can however render using other machines in your network. (Local render node will not be available)");
        }

        public async void ShowCustomWizard()
        {
            await CustomBlenderBuildWizard.Show(this);
        }
        public async void ShowFileDialog()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Select your Blendfile";
            dialog.Filters.Add(new FileDialogFilter()
            {
                Name = "Blender File",
                Extensions = { "blend" }
            });
            dialog.AllowMultiple = false;

            Console.Write("Showing OpenFileDialog");

            //Workaround for Linux?

            string[] results = null;

            //if (_os == SystemInfo.OS_LINUX64)
            //    results = await Avalonia.Dialogs.ManagedFileDialogExtensions.ShowManagedAsync(dialog, this, new Avalonia.Dialogs.ManagedFileDialogOptions()
            //    {
            //        AllowDirectorySelection = false
            //    });
            //else
                results = await dialog.ShowAsync(this);

            

            if (results == null)
                Console.WriteLine("ShowFileDialog Results: null");
            else
            {
                Console.Write("ShowFileDialog Results: " + string.Join(", ", results));
                if (results.Length > 0)
                {
                    fileSelection.Text = results[0];
                }
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

            string localPath = SystemInfo.RelativeToApplicationDirectory(BlendFarmSettings.Instance.LocalBlendFiles);
            //Setup manager
            _manager = new BlendFarmManager(path, version.Name, null, localPath);

            if(!_noServer && !BlendFarmSettings.Instance.PastClients.Any(x=>x.Key == BlendFarmManager.LocalNodeName))
                _manager.AddNode(BlendFarmManager.LocalNodeName, $"localhost:{LocalServer.ServerPort}");

            foreach (var pair in BlendFarmSettings.Instance.PastClients.ToList())
                _manager.AddNode(pair.Key, pair.Value.Address, pair.Value.RenderType);

            //Start render window
            new RenderWindow(_manager, version, path).Show();

            this.Close();
        }
    }
}
