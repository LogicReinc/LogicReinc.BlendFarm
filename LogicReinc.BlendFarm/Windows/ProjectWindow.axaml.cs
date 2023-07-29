using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Meta;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static LogicReinc.BlendFarm.BlendFarmSettings;

namespace LogicReinc.BlendFarm.Windows
{
    /// <summary>
    /// Assumes only one on startup
    /// </summary>
    public class ProjectWindow : Window, INotifyPropertyChanged
    {
        static List<BlenderVersion> versions = new List<BlenderVersion>();

        private TextBox fileSelection = null;
        private ComboBox comboVersions = null;
        private ListBox history = null;
        private StackPanel loadingUI = null;
        private TextBlock loadingText = null;
        private StackPanel loadProjectUI = null;
        private CheckBox useAssetSync = null;
        private CheckBox connectLocal = null;
        private CheckBox importSettings = null;

        private bool _startedNew = false;

        private string _os = null;
        private BlendFarmManager _manager = null;

        private Dictionary<string, (string,string,int)> _previouslyFoundNodes = new Dictionary<string, (string, string, int)>();

        private bool _noServer = false;

        public List<Announcement> Announcements { get; set; } = new List<Announcement>();
        public Announcement LastAnnouncement { get; set; } = null;

        public event PropertyChangedEventHandler PropertyChanged;

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

            new Thread(() =>
            {
                try
                {
                    Console.WriteLine("Fetching announcements from repo");
                    List<Announcement> announcements = Announcement.GetAnnouncements(Constants.AnnouncementUrl);
                    if (announcements == null)
                        throw new InvalidDataException("No valid data found");
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Announcements = announcements?.OrderByDescending(x => x.Date).ToList() ?? new List<Announcement>();
                        Announcement lastAnn = Announcements?.OrderByDescending(x => x.Date)?.FirstOrDefault();
                        LastAnnouncement = lastAnn;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastAnnouncement)));

                        if (lastAnn != null && lastAnn.Date > BlendFarmSettings.Instance.LastAnnouncementDate)
                        {
                            new AnnouncementWindow(announcements).Show();
                            BlendFarmSettings.Instance.LastAnnouncementDate = lastAnn.Date;
                            BlendFarmSettings.Instance.Save();
                        }
                    });
                }
                catch(Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine($"Failed to fetch announcements due to \"{ex.Message}\" (Attempted to fetch from {Constants.AnnouncementUrl})");
                    Console.ResetColor();
                }

            }).Start();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            Width = 600;
            Height = 700;
            MinHeight = 700;
            MinWidth = 600;
            MaxHeight = 700;
            MaxWidth = 600;

            fileSelection = this.FindControl<TextBox>("fileSelect");

            loadingUI = this.FindControl<StackPanel>("loadingUI");
            loadingText = this.FindControl<TextBlock>("loadingText");
            loadProjectUI = this.FindControl<StackPanel>("loadProjectUI");
            useAssetSync = this.FindControl<CheckBox>("useAssetSync");
            connectLocal = this.FindControl<CheckBox>("connectLocal");
            importSettings = this.FindControl<CheckBox>("importSettings");

            useAssetSync.IsChecked = BlendFarmSettings.Instance.Option_UseAssetsSync;
            connectLocal.IsChecked = BlendFarmSettings.Instance.Option_ConnectLocal;
            importSettings.IsChecked = BlendFarmSettings.Instance.Option_ImportSettings;
            if(_noServer)
            {
                connectLocal.IsChecked = false;
                connectLocal.IsEnabled = false;
            }
            if (!(connectLocal.IsChecked ?? false))
                importSettings.IsEnabled = false;

            connectLocal.Unchecked += (a, b) =>
            {
                importSettings.IsChecked = false;
                importSettings.IsEnabled = false;
            };
            connectLocal.Checked += (a, b) => importSettings.IsEnabled = true;

            comboVersions = this.FindControl<ComboBox>("versionSelect");
            ReloadVersions();

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

        public void ReloadVersions()
        {
            try
            {
                Console.WriteLine("Fetching versions from cache or remote");

                string versionCache = SystemInfo.RelativeToApplicationDirectory("VersionCache");
                if (!new FileInfo(versionCache).FullName.ToLower().StartsWith("c:\\windows\\system32"))
                    versions = BlenderVersion.GetBlenderVersions(versionCache, SystemInfo.RelativeToApplicationDirectory("VersionCustom"))
                        .OrderByDescending(x => x.Name)
                        .ToList();
                else
                    versions = new List<BlenderVersion>()
                    {
                        new BlenderVersion()
                        {
                            Name = "Fake Version"
                        }
                    };
            }
            catch (Exception ex)
            {
                MessageWindow.Show(this, "Exception retrieving versions", 
                    $"Failed to retrieve versions due to {ex.Message}, this may be due to no internet connection.An internet connection is required to retrieve version info and download Blender installations. Restart application with internet connection for Blender versions..",
                    600, 250);
            }
            comboVersions.Items = versions;
            int selectedIndex = 0;
            if (!string.IsNullOrEmpty(BlendFarmSettings.Instance.LastVersion))
            {
                BlenderVersion lastVersion = versions.FirstOrDefault(x => x.Name == BlendFarmSettings.Instance.LastVersion);
                if (lastVersion != null)
                    selectedIndex = versions.IndexOf(lastVersion);
            }
            comboVersions.SelectedIndex = selectedIndex;
        }
        public async void ShowCustomWizard()
        {
            await CustomBlenderBuildWizard.Show(this);
            ReloadVersions();
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

            results = results?.Select(x => Statics.SanitizePath(x)).ToArray();
            

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

        private void ShowLoadProjectUI(bool show)
        {
            if(show)
            {
                loadProjectUI.IsVisible = true;
                loadingUI.IsVisible = false;
            }
            else
            {
                loadProjectUI.IsVisible = false;
                loadingUI.IsVisible = true;
            }
        }

        /// <summary>
        /// Assumes only one call
        /// </summary>
        public async Task LoadProject()
        {
            loadingText.Text = "Loading project";
            ShowLoadProjectUI(false);
            string file = fileSelection.Text;
            BlenderVersion version = (BlenderVersion)comboVersions.SelectedItem;

            if (!File.Exists(file))
            {
                MessageWindow.Show(this, "File not found", $"{file} does not exist");
                ShowLoadProjectUI(true);
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
            BlendFarmSettings.Instance.LastVersion = version.Name;
            BlendFarmSettings.Instance.History = BlendFarmSettings.Instance.History.OrderByDescending(x => x.Date).Take(10).ToList();
            BlendFarmSettings.Instance.Option_ConnectLocal = connectLocal.IsChecked ?? false;
            BlendFarmSettings.Instance.Option_ImportSettings = importSettings.IsChecked ?? false;
            BlendFarmSettings.Instance.Option_UseAssetsSync = useAssetSync.IsChecked ?? false;
            BlendFarmSettings.Instance.Save();

            _startedNew = true;

            string localPath = SystemInfo.RelativeToApplicationDirectory(BlendFarmSettings.Instance.LocalBlendFiles);
            //Setup manager
            _manager = new BlendFarmManager(path, version.Name, null, localPath);

            if(!_noServer && !BlendFarmSettings.Instance.PastClients.Any(x=>x.Key == BlendFarmManager.LocalNodeName))
                _manager.AddNode(BlendFarmManager.LocalNodeName, $"localhost:{LocalServer.ServerPort}", RenderType.CPU, ServerSettings.Instance.BasicSecurityPassword);

            foreach (var pair in BlendFarmSettings.Instance.PastClients.ToList())
                _manager.AddNode(pair.Key, pair.Value.Address, pair.Value.RenderType, pair.Value.Pass);

            if (useAssetSync.IsChecked.Value)
            {
                if (!await YesNoNeverWindow.Show(this, "Disclaimer", "Asset sync is an work in progress feature.\nWould you like to use it?", "wipAssetSync"))
                {
                    ShowLoadProjectUI(true);
                    return;
                }

                loadingText.Text = "Preparing Blender " + version.Name + "\n(This might take a minute depending on your connection speed, only required once per version)";

                _ = Task.Run(() =>
                {
                    if (!LocalServer.Manager.TryPrepare(version.Name))
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            MessageWindow.Show(this, "Failed to prepare Blender version", "Asset sync requires local Blender, but failed to download it");
                            ShowLoadProjectUI(true);
                        });
                    }
                    else
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            new RenderWindow(_manager, version, path, null, new RenderWindowOptions()
                            {
                                WithAssetSync = true,
                                ConnectLocal = connectLocal.IsChecked ?? false,
                                ImportSettings = (connectLocal.IsChecked ?? false) && (importSettings.IsChecked ?? false)
                            }).Show();
                            this.Close();
                        });
                    }
                });
            }
            else {
                loadingText.Text = "Starting";

                //Start render window
                //new RenderWindow();
                new RenderWindow(_manager, version, path, null, new RenderWindowOptions()
                {
                    WithAssetSync = false,
                    ConnectLocal = connectLocal.IsChecked ?? false,
                    ImportSettings = (connectLocal.IsChecked ?? false) && (importSettings.IsChecked ?? false)
                }).Show();

                this.Close();
            }
        }

        public void OpenLastAnnouncement()
        {
            if(Announcements != null)
            new AnnouncementWindow(Announcements).Show();
        }
    }
}
