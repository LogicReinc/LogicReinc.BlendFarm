using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Image = Avalonia.Controls.Image;

namespace LogicReinc.BlendFarm.Windows
{

    public class OpenBlenderProject 
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
        public RenderTask CurrentTask = null;

        public OpenBlenderProject(string blendfile)
        {
            BlendFile = blendfile;
        }

        public event Action<OpenBlenderProject, Bitmap> OnBitmapChanged;
    }


    public class QueueItem : INotifyPropertyChanged
    {
        private RenderWindow _owner = null;

        public string ID { get; set; }
        public OpenBlenderProject Project { get; set; }
        public RenderManagerSettings Settings { get; set; }
        public RenderTask Task { get; set; }

        public string State => (!Cancelled ? (Task != null ? $"Rendering {Task.Progress * 100: 0.##}%" : "Queued") : "Cancelled");

        public bool Cancelled { get; set; }

        public bool IsCancelable => !Cancelled && !Completed;
        public bool IsDeletable => Cancelled || Completed;
        public bool IsQueued => !Cancelled && !Completed && ((Task?.Progress ?? 0) == 0);


        public string Name => Project?.Name;
        public bool Active => !Cancelled && !Completed;
        public double Progress => Task?.Progress ?? 0.0;
        public double ProgressPercentage => Task?.Progress * 100 ?? 0.0;
        public bool Completed => FinishedAllFrames;

        public bool FinishedAllFrames { get; set; }

        public QueueItem(RenderWindow owner, OpenBlenderProject proj, RenderManagerSettings settings)
        {
            _owner = owner;
            Project = proj;
            Settings = settings;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public async Task UpdateValues(BlendFarmManager manager, RenderManagerSettings settings)
        {
            Settings = settings;

            if (Task.Consumed && !Completed)
                await Task.Cancel();
            await Execute(manager);
        }

        public async Task Execute(BlendFarmManager manager)
        {
            await MessageWindow.Show(_owner, "Not Implemented", "Queue execution not implemented at the moment");
        }


        public async Task CancelQueueItem()
        {
            Cancelled = true;
            if (Task != null && Task.Consumed)
                await Task.Cancel();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cancelled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDeletable)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCancelable)));
        }
        public async Task OpenQueueItem()
        {
            await MessageWindow.Show(_owner, "Not Implemented", "Opening queue item not implemented at the moment");
        }
        public async Task DeleteQueueItem()
        {
            _owner.RemoveQueueItem(this);
        }
    }

    public class RenderWindow : Window
    {
        private static DirectProperty<RenderWindow, bool> IsRenderingProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(IsRendering), (x) => x.IsRendering);
        private static DirectProperty<RenderWindow, bool> IsLiveChangingProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(IsLiveChanging), (x) => x.IsLiveChanging);
        //private static DirectProperty<RenderWindow, bool> UseFPSProperty =
        //    AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(UseFPS), (x) => x.UseFPS, (w, v) => w.UseFPS = v);
        private static DirectProperty<RenderWindow, bool> IsQueueingProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, bool>(nameof(IsQueueing), (x) => x.IsQueueing);

        private static DirectProperty<RenderWindow, OpenBlenderProject> CurrentProjectProperty =
            AvaloniaProperty.RegisterDirect<RenderWindow, OpenBlenderProject>(nameof(CurrentProject), (x) => x.CurrentProject, (w, v) => w.CurrentProject = v);

        //public string File { get; set; }
        public BlenderVersion Version { get; set; }

        public ObservableCollection<OpenBlenderProject> Projects { get; set; } = new ObservableCollection<OpenBlenderProject>();

        public ObservableCollection<QueueItem> Queue { get; set; } = new ObservableCollection<QueueItem>();


        public bool IsClientConnecting { get; set; }
        public string InputClientName { get; set; }
        public string InputClientAddress { get; set; }

        public bool UseAutomaticPerformance { get; set; } = true;
        public bool UseSyncCompression { get; set; } = false;

        /*
        //Render Properties
        public int RenderWidth { get; set; } = 1280;
        public int RenderHeight { get; set; } = 720;
        public int ChunkSize { get; set; } = 256;
        public int Samples { get; set; } = 32;
        public string Denoiser { get; set; } = "Inherit";

        public bool UseWorkaround { get; set; } = true;
        public bool UseAutomaticPerformance { get; set; } = true;
        public bool UseSyncCompression { get; set; } = false;

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
                RaisePropertyChanged(UseFPSProperty, old, value);
            }
        }
        */

        public OpenBlenderProject CurrentProject { get; set; } = null;

        //State
        public bool IsLiveChanging { get; set; } = false;

        public bool IsQueueing { get; set; } = false;

        public ObservableCollection<RenderNode> Nodes { get; private set; } = new ObservableCollection<RenderNode>();
        public BlendFarmManager Manager { get; set; } = null;

        public bool IsRendering => CurrentTask != null;
        public RenderTask CurrentTask = null;

        //Options
        protected string[] DenoiserOptions { get; } = new string[] { "Inherit", "None", "NLM", "OPTIX", "OPENIMAGEDENOISE" };

        //Dialogs
        private string _lastAnimationDirectory = null;


        //Views
        private ListBox _nodeList = null;
        private Image _image = null;
        private ProgressBar _imageProgress = null;
        private TextBlock _lastRenderTime = null;
        private ComboBox _selectStrategy = null;
        private ComboBox _selectOrder = null;


        //Debug data
        private ObservableCollection<RenderNode> _testNodes = new ObservableCollection<RenderNode>(new List<RenderNode>()
        {
            new RenderNode()
            {
                Name = "Local",
                Address = "Localhost"
            },
            new RenderNode()
            {
                Name = "WhateverPC",
                Address = "192.168.1.212"
            }
        });



        public RenderWindow()
        {
            Projects = new ObservableCollection<OpenBlenderProject>()
            {
                new OpenBlenderProject("C://some/blend/dir/Example Project.blend"),
                new OpenBlenderProject("C://some/blend/dir/Some other project.blend"),
                new OpenBlenderProject("C://some/blend/dir/asdf1234.blend"),
                new OpenBlenderProject("C://some/blend/dir/testing.blend"),
            };
            Queue = new ObservableCollection<QueueItem>()
            {
                new QueueItem(this, new OpenBlenderProject("C://whatever/testproject.blend"), new RenderManagerSettings()
                {

                }){
                        Task = new RenderTask(null, null, null, 0)
                        {
                            Progress = 0.43
                        }
                    },
                new QueueItem(this, new OpenBlenderProject("C://whatever/asdfdsag.blend"), new RenderManagerSettings()
                {

                })
            };
            //File = "path/to/some/blendfile.blend";
            CurrentProject = LoadProject("path/to/some/blendfile.blend");
            Version = new Shared.BlenderVersion()
            {
                Name = "blender-2.9.2"
            };
            Init();
        }
        public RenderWindow(BlendFarmManager manager, BlenderVersion version, string blenderFile, string sessionID = null)
        {
            Manager = manager;
            //File = blenderFile;
            CurrentProject = LoadProject(blenderFile);
            Version = version;

            using (Stream icoStream = Program.GetIconStream())
            {
                this.Icon = new WindowIcon(icoStream);
            }

            Init();
        }
        private void Init()
        {
            if(Manager?.Nodes != null)
            {
                foreach(RenderNode node in Manager.Nodes.ToList())
                    Nodes.Add(node);
                Manager.OnNodeAdded += (manager, node) => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Nodes.Add(node);
                });
                Manager.OnNodeRemoved += (manager, node) => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Nodes.Remove(node);
                });
            }
            else 
                Nodes =  _testNodes;
            DataContext = this;

            this.Closed += (a, b) =>
            {
                LocalServer.Stop();
                Manager.StopFileWatch();
            };
            Manager?.StartFileWatch();

            this.InitializeComponent();
#if DEBUG
            //this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            MinHeight = 600;
            MinWidth = 500;
            Width = 1400;
            Height = 975;

            System.Version version = Assembly.GetExecutingAssembly().GetName().Version;
            this.Title = $"BlendFarm by LogicReinc [{version.Major}.{version.Minor}.{version.Build}]";

            _nodeList = this.Find<ListBox>("listNodes");
            _image = this.Find<Image>("render");
            _imageProgress = this.Find<ProgressBar>("renderProgress");
            _lastRenderTime = this.Find<TextBlock>("lastRenderTime");
            _selectStrategy = this.Find<ComboBox>("selectStrategy");
            _selectOrder = this.Find<ComboBox>("selectOrder");

            _selectStrategy.Items = Enum.GetValues(typeof(RenderStrategy));
            _selectStrategy.SelectedIndex = 0;
            _selectOrder.Items = Enum.GetValues(typeof(TaskOrder));
            _selectOrder.SelectedIndex = 0;

            _image.KeyDown += async (a, b) =>
            {
                if (b.Key == Avalonia.Input.Key.Delete)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        CurrentProject.LastBitmap = FromDrawingBitmap(new System.Drawing.Bitmap(1, 1));
                        RaisePropertyChanged(CurrentProjectProperty, null, CurrentProject);
                        //_image.Source = _lastBitmap;
                        _lastRenderTime.Text = "";
                    });
                }
            };
        }


        public async Task OpenProjectDialog()
        {
            OpenFileDialog dialog = new OpenFileDialog()
            {
                Title = "Select a Blendfile",
                Filters = new List<FileDialogFilter>()
                {
                    new FileDialogFilter()
                    {
                        Name = "Blender File (.blend)",
                        Extensions = new List<string>()
                        {
                            "blend"
                        }
                    }
                }
            };

            string[] paths = await dialog.ShowAsync(this);
            paths = paths?.Select(x => Statics.SanitizePath(x)).ToArray();

            if (paths != null)
                foreach (string path in paths)
                {
                    if (!File.Exists(path))
                        await MessageWindow.Show(this, "Invalid Path", $"Path {path} does not exist, and is ignored.");
                    else
                        LoadProject(path);
                }
        }

        public OpenBlenderProject LoadProject(string blendFile)
        {
            OpenBlenderProject proj = new OpenBlenderProject(blendFile);
            proj.OnBitmapChanged += async (proj, bitmap) =>
            {

                if (proj == CurrentProject)
                    await Dispatcher.UIThread.InvokeAsync(() => _image.Source = bitmap); ;
            };
            Projects.Add(proj);
            return proj;   
        }

        public async Task SwitchProject(OpenBlenderProject proj)
        {
            OpenBlenderProject oldProj = CurrentProject;
            CurrentProject = proj;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RaisePropertyChanged(CurrentProjectProperty, oldProj, proj);

                _image.Source = proj.LastBitmap;
            });
        }

        public async void ConnectAll()
        {
            try
            {
                await Manager.ConnectAndPrepareAll();
            }
            catch { }
        }
        public async Task SyncAll()
        {
            await Manager?.Sync(CurrentProject.BlendFile, UseSyncCompression);
        }

        public void AddNewNode()
        {
            if (!string.IsNullOrEmpty(InputClientAddress) && !string.IsNullOrEmpty(InputClientName))
            {
                if (BlendFarmSettings.Instance.PastClients.Any(x => x.Key == InputClientName || x.Value.Address == InputClientAddress))
                {
                    MessageWindow.Show(this, "Node already exists", "Node already exists, use a different name and address");
                    return;
                }
                if(!Regex.IsMatch(InputClientAddress, "^([a-zA-Z0-9\\.]*?):[0-9][0-9]?[0-9]?[0-9]?[0-9]?$"))
                {
                    MessageWindow.Show(this, "Invalid Address", "The address provided seems to be invalid, expected format is {hostname}:{port} or {ip}{port}, eg. 192.168.1.123:15000");
                    return;
                }

                Manager.AddNode(InputClientName, InputClientAddress);

                BlendFarmSettings.Instance.PastClients.Add(InputClientName, new BlendFarmSettings.HistoryClient()
                {
                    Address = InputClientAddress,
                    Name = InputClientName,
                    RenderType = RenderType.CPU
                });
                BlendFarmSettings.Instance.Save();
            }
            else
                MessageWindow.Show(this, "No name or address", "A node requires both a name and an address");
        }

        public void DeleteNode(RenderNode node)
        {
            Manager.RemoveNode(node.Name);

            var nodeEntry = BlendFarmSettings.Instance.PastClients.FirstOrDefault(x => x.Key == node.Name).Key;
            if (nodeEntry != null)
            {
                BlendFarmSettings.Instance.PastClients.Remove(nodeEntry);
                BlendFarmSettings.Instance.Save();
            }
        }
        public async void ConfigureNode(RenderNode node)
        {
            DeviceSettingsWindow.Show(this, node);
        }


        //Singular
        public async Task Render() => await Render(false, false);
        public async Task Render(bool noSync, bool noExcep = false)
        {
            OpenBlenderProject currentProject = CurrentProject;

            if (currentProject.CurrentTask != null)
                return;

            //Show Progressbar
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                this._imageProgress.IsVisible = true;
                this._imageProgress.IsIndeterminate = true;
            });

            //Check if any unsynced nodes
            if(!noSync && Manager.Nodes.Any(x=> x.Connected && !x.IsSynced))
            {
                if(await YesNoNeverWindow.Show(this, "Unsynced nodes", "You have nodes that are not yet synced, would you like to sync them to use for rendering?", "syncBeforeRendering"))
                    await Manager.Sync(CurrentProject.BlendFile, UseSyncCompression);
            }

            //Start rendering thread
            await Task.Run(async () =>
            {
                try
                {
                    Stopwatch watch = new Stopwatch();
                    watch.Start();


                    //Create Task
                    CurrentTask = Manager.GetRenderTask(CurrentProject.BlendFile, GetSettingsFromUI(), async (task, updated) =>
                    {
                        //Apply image to canvas
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            CurrentProject.LastBitmap = FromDrawingBitmap(updated);
                            RaisePropertyChanged(CurrentProjectProperty, null, CurrentProject);
                            //_lastBitmap = FromDrawingBitmap(updated);
                                //_image.Source = _lastBitmap;
                                _lastRenderTime.Text = watch.Elapsed.ToString();
                        });
                    });

                    //Progress Updating
                    CurrentTask.OnProgress += async (task, progress) =>
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            this._imageProgress.IsIndeterminate = false;
                            this._imageProgress.Value = progress * 100;
                        });
                    };

                    //Update view
                    await Dispatcher.UIThread.InvokeAsync(() => RaisePropertyChanged(IsRenderingProperty, false, true));

                    //Render
                    var finalBitmap = await CurrentTask.Render();

                    //Finalize
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (finalBitmap != null)
                        {
                            CurrentProject.LastBitmap = FromDrawingBitmap(finalBitmap);
                            RaisePropertyChanged(CurrentProjectProperty, null, CurrentProject);
                            //_lastBitmap = FromDrawingBitmap(finalBitmap);
                            //_image.Source = _lastBitmap;
                            finalBitmap.Save("lastRender.png");
                        }
                        _lastRenderTime.Text = watch.Elapsed.ToString();
                        this._imageProgress.IsVisible = false;
                    });
                    watch.Stop();

                }
                catch (Exception ex)
                {
                    if(!noExcep)
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            MessageWindow.Show(this, "Failed Render", "Failed render due to:" + ex.Message);
                        });
                }
                finally
                {
                    Manager.ClearLastTask();
                    CurrentTask = null;
                    Dispatcher.UIThread.InvokeAsync(() => RaisePropertyChanged(IsRenderingProperty, true, false));
                }
            });
        }
        public async void RenderAnimation()
        {
            if (CurrentTask != null)
                return;

            OpenBlenderProject currentProject = CurrentProject;

            //Validate provided fileformat
            if(!currentProject.AnimationFileFormat.Contains("#"))
            {
                await MessageWindow.Show(this, "Invalid file format", "File format should contain a '#' for frame number");
                return;
            }
            string validAnimationFileName = currentProject.AnimationFileFormat.Replace("#", "");
            if(Path.GetInvalidFileNameChars().Any(x=>validAnimationFileName.Contains(x)))
            {
                await MessageWindow.Show(this, "Invalid file format", "File name for animation frames contains illegal characters");
                return;
            }
            string animationFileFormat = currentProject.AnimationFileFormat;



            string outputDir = "Animation";

            //Request output directory and UI
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                outputDir = null;

                OpenFolderDialog dialog = new OpenFolderDialog()
                {
                    Title = "Select folder to save animation frames to"
                };

                if (!string.IsNullOrEmpty(_lastAnimationDirectory))
                    dialog.Directory = _lastAnimationDirectory;

                outputDir = await dialog.ShowAsync(this);
                outputDir = Statics.SanitizePath(outputDir);

                this._imageProgress.IsVisible = true;
                this._imageProgress.IsIndeterminate = true;
            });

            if (string.IsNullOrEmpty(outputDir))
                return;
            else
                outputDir = Path.GetFullPath(outputDir);

            _lastAnimationDirectory = outputDir;

            if (Manager.Nodes.Any(x => x.Connected && !x.IsSynced))
            {
                if (await YesNoNeverWindow.Show(this, "Unsynced nodes", "You have nodes that are not yet synced, would you like to sync them to use for rendering?", "syncBeforeRendering"))
                    await Manager.Sync(CurrentProject.BlendFile);
            }

            await Task.Run(async () =>
            {
                try
                {
                    Stopwatch watch = new Stopwatch();
                    watch.Start();


                    //Create Task
                    CurrentTask = Manager.GetRenderTask(CurrentProject.BlendFile, GetSettingsFromUI(), null, async (task, frame)=>
                    {
                        string filePath = Path.Combine(outputDir, animationFileFormat.Replace("#", task.Frame.ToString()));

                        try
                        {
                            frame.Save(filePath);
                        }
                        catch(Exception ex)
                        {
                            await MessageWindow.ShowOnUIThread(this, "Frame Save Error", $"Animation frame {task.Frame} failed to save due to:" + ex.Message);
                            return;
                        }

                        //Apply image to canvas
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                currentProject.LastBitmap = new Bitmap(filePath);
                                if(currentProject == CurrentProject)
                                    RaisePropertyChanged(CurrentProjectProperty, null, CurrentProject);
                                //_lastBitmap = new Bitmap(filePath);
                                //_image.Source = _lastBitmap;
                            }
                            catch(Exception ex)
                            {
                                _ = MessageWindow.Show(this, "GUI Exception", "An error occured trying to load animation Bitmap in GUI.\n(Animation frame should still be saved)");
                            }
                            _lastRenderTime.Text = watch.Elapsed.ToString();
                        });
                    });

                    //Progress Updating
                    CurrentTask.OnProgress += async (task, progress) =>
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            this._imageProgress.IsIndeterminate = false;
                            this._imageProgress.Value = progress * 100;
                        });
                    };

                    await Dispatcher.UIThread.InvokeAsync(() => RaisePropertyChanged(IsRenderingProperty, false, true));

                    //Render
                    var success = await CurrentTask.RenderAnimation(currentProject.FrameStart, currentProject.FrameEnd);
                    if (success)
                        _ = MessageWindow.ShowOnUIThread(this, "Animation Rendered", $"Frames {currentProject.FrameStart} to {currentProject.FrameEnd} rendered.\nLocated at {outputDir}.");

                    watch.Stop();

                }
                catch (Exception ex)
                {
                    await MessageWindow.ShowOnUIThread(this, "Failed Render", "Failed render due to:" + ex.Message);
                }
                finally
                {
                    Manager.ClearLastTask();
                    CurrentTask = null;
                    await Dispatcher.UIThread.InvokeAsync(() => RaisePropertyChanged(IsRenderingProperty, true, false));
                }
            });
        }

        public async Task CancelRender()
        {
            await CurrentProject.CurrentTask?.Cancel();
            CurrentProject.CurrentTask = null;
        }

        //Queue
        public async Task AddToQueueReplace()
        {
            OpenBlenderProject proj = CurrentProject;

            QueueItem existing = GetProjectQueueItem(proj);

            if(existing != null)
            {
                if (existing.Active)
                    await existing.UpdateValues(Manager, GetSettingsFromUI(proj));
            }
            else
                await AddToQueueNew();
        }
        public async Task AddToQueueNew()
        {
            OpenBlenderProject proj = CurrentProject;

            RenderManagerSettings settings = GetSettingsFromUI(proj);

            QueueItem item = new QueueItem(this, proj, settings);

            lock(Queue)
                Queue.Add(item);
        }
        public void RemoveQueueItem(QueueItem item)
        {
            lock(Queue)
                Queue.Remove(item);
            if (item.Task != null && !item.Completed && item.Active)
                item.CancelQueueItem();
        }


        private static Bitmap FromDrawingBitmap(System.Drawing.Bitmap bitmap)
        {
            //TODO: This needs to be better..
            using(MemoryStream str = new MemoryStream())
            {
                bitmap.Save(str, ImageFormat.Png);
                str.Position = 0;
                return new Bitmap(str);
            }
        }

        public async void SaveImage()
        {
            SaveFileDialog dialog = new SaveFileDialog()
            {
                Title = "Save current BlendFarm render"
            };
            dialog.InitialFileName = "render.png";

            string result = await dialog.ShowAsync(this);
            result = Statics.SanitizePath(result);
            if (result != null && CurrentProject.LastBitmap != null)
                CurrentProject.LastBitmap.Save(result);
        }


        public void StartLiveRender()
        {
            if (!IsLiveChanging)
            {
                IsLiveChanging = true;
                Manager.OnFileChanged += RenderOnFileChange;
                Manager.AlwaysUpdateFile = true;
                RaisePropertyChanged(IsLiveChangingProperty, false, true);
            }
        }
        public void StopLiveRender()
        {
            Manager.AlwaysUpdateFile = false;
            Manager.OnFileChanged -= RenderOnFileChange;
            IsLiveChanging = false;
            RaisePropertyChanged(IsLiveChangingProperty, true, false);
        }

        //Buttons Top
        public void Github()
        {
            OpenUrl("https://github.com/LogicReinc/LogicReinc.BlendFarm");
        }
        public void Patreon()
        {
            OpenUrl("https://www.patreon.com/LogicReinc");
        }
        public void Help()
        {
            OpenUrl("https://www.youtube.com/watch?v=EXdwD5t53wc");
        }
        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }

        //Util
        private QueueItem GetProjectQueueItem(OpenBlenderProject proj)
        {
            lock (Queue)
            {
                return Queue.FirstOrDefault(x => x.Project == proj);
            }
        }
        private RenderManagerSettings GetSettingsFromUI(OpenBlenderProject proj = null)
        {
            proj = proj ?? CurrentProject;
            return new RenderManagerSettings()
            {
                Frame = proj.FrameStart,
                Strategy = (RenderStrategy)_selectStrategy.SelectedItem,
                Order = (TaskOrder)_selectOrder?.SelectedItem,
                OutputHeight = proj.RenderHeight,
                OutputWidth = proj.RenderWidth,
                ChunkHeight = ((decimal)proj.ChunkSize / proj.RenderHeight),
                ChunkWidth = ((decimal)proj.ChunkSize / proj.RenderWidth),
                Samples = proj.Samples,
                FPS = (proj.UseFPS) ? proj.FPS : 0,
                Denoiser = (proj.Denoiser == "Inherit") ? "" : proj.Denoiser ?? "",
                BlenderUpdateBugWorkaround = proj.UseWorkaround,
                UseAutoPerformance = UseAutomaticPerformance
            };
        }

        private void RenderOnFileChange(BlendFarmManager manager)
        {
            if (CurrentTask?.Progress <= 0)
                return;
            Task.Run(async () =>
            {
                if (IsRendering)
                {
                    await CancelRender();
                }
                if (!IsRendering)
                {
                    await SyncAll();
                    await Render(true, true);
                }
            });
        }




        //Events
        public async void CheckUseQueue(object sender, RoutedEventArgs args)
        {
            ToggleSwitch sw = sender as ToggleSwitch;

            if(sw.IsChecked ?? false)
            {
                IsQueueing = true;
                await Dispatcher.UIThread.InvokeAsync(() => RaisePropertyChanged(IsQueueingProperty, false, true));
            }
            else
            {
                if(Queue.Count > 0)
                {
                    sw.IsChecked = true;
                    IsQueueing = true;
                    await MessageWindow.Show(this, "Cannot disable Queue", "Your queue is not empty, and thus cannot be disabled");
                }
                else
                {
                    IsQueueing = false;
                    await Dispatcher.UIThread.InvokeAsync(() => RaisePropertyChanged(IsQueueingProperty, true, false));
                }
            }
        }

        public void ProjectTabChanged(object sender, SelectionChangedEventArgs args)
        {
            if(args.AddedItems.Count == 1 && Projects.Contains(args.AddedItems[0]))
            {
                SwitchProject(args.AddedItems[0] as OpenBlenderProject);
            }
        }

    }
}
