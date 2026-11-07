using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.ImageTypes;
using LogicReinc.BlendFarm.Client.Tasks;
using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Windows;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Objects
{
    public class QueueItem : INotifyPropertyChanged
    {
        private RenderWindow _owner = null;

        public string ID { get; set; }
        public OpenBlenderProject Project { get; set; }
        public RenderManagerSettings Settings { get; set; }
        public int Frames { get; set; } = 1;
        public string FrameFormat { get; set; } = "#.png";
        public string SaveTo { get; set; }

        public RenderTask Task { get; set; }

        public string State => (!string.IsNullOrEmpty(Exception)) ? Exception : (Cancelled ? "Cancelled" : (Completed ? "Completed" : (Task != null ? $"Rendering {Task.Progress * 100: 0.##}%" : "Queued")));

        public string Exception { get; set; }

        public bool Cancelled { get; set; }

        public bool IsRendering => Task != null && !Cancelled && !Completed;
        public bool IsCancelable => IsRendering;
        public bool IsDeletable => !IsRendering;
        public bool IsQueued => !Cancelled && !Completed && Task == null;


        public string Name => Project?.Name;
        public bool IsAnimation => Frames > 1;
        public string RenderKind => IsAnimation ? "Animation" : "Image";
        public string FrameRangeDisplay => IsAnimation ? $"{Settings.Frame} - {Settings.Frame + Frames - 1}" : Settings.Frame.ToString();
        public string ResolutionDisplay => $"{Settings.OutputWidth} x {Settings.OutputHeight}";
        public string SceneDisplay => string.IsNullOrWhiteSpace(Settings.Scene) ? "Default" : Settings.Scene;
        public string CameraDisplay => string.IsNullOrWhiteSpace(Settings.Camera) ? "Default" : Settings.Camera;
        public string SaveToDisplay => CompactPath(SaveTo);
        public string SettingsSummary => $"{RenderKind} | {ResolutionDisplay} | Scene: {SceneDisplay} | Camera: {CameraDisplay} | Frame: {FrameRangeDisplay} | Output: {Settings.RenderFormat}";
        public bool Active => !Cancelled && !Completed;
        public double Progress => Task?.Progress ?? 0.0;
        public double ProgressPercentage => Task?.Progress * 100 ?? 0.0;
        public bool Completed => FinishedAllFrames;

        public bool FinishedAllFrames { get; set; }

        private readonly object _lastImageLock = new object();
        private System.Drawing.Image _lastImage = null;
        /// <summary>
        /// Last rendered image; setting it disposes the previous one
        /// </summary>
        public System.Drawing.Image LastImage
        {
            get
            {
                lock (_lastImageLock)
                    return _lastImage;
            }
            set
            {
                System.Drawing.Image old;
                lock (_lastImageLock)
                {
                    old = _lastImage;
                    _lastImage = value;
                    if (old != null && !ReferenceEquals(old, value))
                        old.Dispose();
                }
            }
        }

        //Captured once per run so every frame shares the same {date}/{time} tokens.
        private DateTime _runTimestamp = DateTime.Now;

        public QueueItem(RenderWindow owner, OpenBlenderProject proj, RenderManagerSettings settings, string saveTo = null, int frames = 1)
        {
            _owner = owner;
            Frames = frames;
            Project = proj;
            Settings = settings;
            SaveTo = saveTo;
        }

        private static string CompactPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "--";

            try
            {
                string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string leaf = Path.GetFileName(trimmed);
                string parent = Path.GetFileName(Path.GetDirectoryName(trimmed));

                if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(leaf))
                    return $"{parent}\\{leaf}";
                if (!string.IsNullOrWhiteSpace(leaf))
                    return leaf;
            }
            catch
            {
            }

            return path.Length > 42 ? $"...{path.Substring(path.Length - 39)}" : path;
        }

        public QueueItemDTO ToDTO()
        {
            return new QueueItemDTO()
            {
                BlendFile = Project?.BlendFile,
                Settings = Settings,
                Frames = Frames,
                FrameFormat = FrameFormat,
                SaveTo = SaveTo,
                UseNetworkedPath = Project?.UseNetworkedPath ?? false,
                NetworkPathWindows = Project?.NetworkPathWindows,
                NetworkPathLinux = Project?.NetworkPathLinux,
                NetworkPathMacOS = Project?.NetworkPathMacOS
            };
        }

        public static QueueItem FromDTO(RenderWindow owner, OpenBlenderProject proj, QueueItemDTO dto)
        {
            //Deep-copy the settings so a loaded/duplicated item never shares an instance.
            RenderManagerSettings settings = dto.Settings;
            try
            {
                settings = JsonSerializer.Deserialize<RenderManagerSettings>(JsonSerializer.Serialize(dto.Settings));
            }
            catch
            {
                settings = dto.Settings;
            }

            return new QueueItem(owner, proj, settings, dto.SaveTo, Math.Max(1, dto.Frames))
            {
                FrameFormat = string.IsNullOrEmpty(dto.FrameFormat) ? "#.png" : dto.FrameFormat
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public async Task UpdateValues(RenderWindow window, BlendFarmManager manager, RenderManagerSettings settings)
        {
            Settings = settings;

            if (Task.Consumed && !Completed)
            {
                await Task.Cancel();
                await Execute(window, manager);
            }
        }

        //Builds the token context for output-filename templates from this item's settings.
        private OutputNameContext BuildContext(int frame, int startFrame, int endFrame)
        {
            return new OutputNameContext()
            {
                Scene = string.IsNullOrEmpty(Settings.Scene) ? Project?.Name : Settings.Scene,
                Camera = Settings.Camera,
                Engine = Settings.Engine.ToString(),
                Format = Settings.RenderFormat,
                Samples = Settings.Samples,
                Width = Settings.OutputWidth,
                Height = Settings.OutputHeight,
                Frame = frame,
                StartFrame = startFrame,
                EndFrame = endFrame,
                Timestamp = _runTimestamp
            };
        }

        private RenderCompletionInfo BuildCompletionInfo(bool success, TimeSpan elapsed, string error, BlendFarmManager manager)
        {
            int endFrame = Settings.Frame + Math.Max(1, Frames) - 1;
            return new RenderCompletionInfo()
            {
                SceneName = string.IsNullOrEmpty(Settings?.Scene) ? Project?.Name : Settings.Scene,
                BlendFile = Project?.BlendFile,
                Success = success,
                IsAnimation = IsAnimation,
                FrameStart = Settings?.Frame ?? 0,
                FrameEnd = endFrame,
                FrameCount = Math.Max(1, Frames),
                Elapsed = elapsed,
                NodeCount = manager?.Nodes?.Count(x => x.Connected) ?? 0,
                Width = Settings?.OutputWidth ?? 0,
                Height = Settings?.OutputHeight ?? 0,
                Samples = Settings?.Samples ?? 0,
                EngineOrError = success ? (Settings?.Engine.ToString() ?? "") : error,
                OutputDirectory = string.IsNullOrEmpty(SaveTo) ? null : (Directory.Exists(SaveTo) ? SaveTo : Path.GetDirectoryName(SaveTo))
            };
        }

        public async Task Execute(RenderWindow window, BlendFarmManager manager)
        {
            _runTimestamp = DateTime.Now;
            Stopwatch queueWatch = Stopwatch.StartNew();
            try
            {
                if (Frames <= 1)
                {
                    //Normal Render
                    Task = manager.GetImageTask(Project.BlendFile, Settings, (st, bitmap) =>
                    {
                        //Apply image to canvas
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Project.LastImage = bitmap.ToAvaloniaBitmap();
                            window.RefreshCurrentProject();
                            RefreshInfo();
                        });
                    });
                    Task.OnProgress += (t, p) =>
                    {
                        if (p >= 1)
                            FinishedAllFrames = true;
                        RefreshInfo();
                    };

                    Project.SetRenderTask(Task);
                    RefreshInfo();
                    window.RefreshCurrentProject();

                    Task.FileID = manager.UpdateFileVersion(Project.BlendFile, Project.UseNetworkedPath);

                    if (!Project.UseNetworkedPath)
                        await manager.Sync(Project.BlendFile, window.UseSyncCompression);
                    else
                        await manager.Sync(Project.BlendFile, Project.NetworkPathWindows, Project.NetworkPathLinux, Project.NetworkPathMacOS);
                    await System.Threading.Tasks.Task.Delay(500);

                    bool rendered = await Task.Render();
                    System.Drawing.Image final = ((Task is IImageTask) ? (IImageTask)Task : null)?.FinalImage;
                    if (!rendered || Task.Cancelled || final == null)
                    {
                        //Cancelled or produced nothing, don't save or report it as completed
                        StoppedEarly(window);
                        return;
                    }
                    await System.Threading.Tasks.Task.Delay(500);
                    //Keep an own copy, the task owns the final image
                    LastImage = (System.Drawing.Image)final.Clone();
                    FinishedAllFrames = true;

                    if (!string.IsNullOrEmpty(SaveTo))
                    {
                        //If SaveTo points at a directory, expand the project's output template into a filename.
                        string target = SaveTo;
                        if (Directory.Exists(SaveTo))
                        {
                            string template = string.IsNullOrEmpty(Project?.OutputTemplate) ? "#" : Project.OutputTemplate;
                            string name = OutputNaming.FormatOutputFileName(template, BuildContext(Settings.Frame, Settings.Frame, Settings.Frame));
                            if (!OutputNaming.HasImageExtension(name))
                                name = name + "." + (ImageFormats.GetExtension(Settings.RenderFormat) ?? "png");
                            target = Path.Combine(SaveTo, name);
                        }
                        Statics.SaveImage(final, target);
                    }

                    //Apply final to canvas
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Project.LastImage = final.ToAvaloniaBitmap();

                        Project.SetRenderTask(null);
                        RefreshInfo();
                    });
                    window.RefreshCurrentProject();
                    queueWatch.Stop();
                    _owner.NotifyRenderCompleted(BuildCompletionInfo(true, queueWatch.Elapsed, null, manager));
                }
                else
                {
                    //Animation
                    if (string.IsNullOrEmpty(FrameFormat))
                        throw new ArgumentException("Missing frameformat for animation");

                    //Normal Render
                    int endFrame = Settings.Frame + Frames - 1;
                    //Resume: leave out frames that already have an output file
                    List<int> skipFrames = (Project?.SkipExistingFrames ?? false)
                        ? OutputNaming.FindExistingFrames(SaveTo, Settings.Frame, endFrame, (f) => OutputNaming.FormatOutputFileName(FrameFormat, BuildContext(f, Settings.Frame, endFrame)))
                        : new List<int>();
                    Task = manager.GetAnimationTask(Project.BlendFile, Settings.Frame, endFrame, Settings, (task, frame) =>
                    {

                        string fileName = OutputNaming.FormatOutputFileName(FrameFormat, BuildContext(task.Frame, Settings.Frame, endFrame));
                        string filePath = Path.Combine(SaveTo, fileName);

                        try
                        {
                            File.WriteAllBytes(filePath, frame.Image);
                        }
                        catch (Exception ex)
                        {
                            //Throw so the frame is retried instead of silently lost
                            Dispatcher.UIThread.Post(() => _owner.AddActivityLog($"Queue frame {task.Frame} failed to save: {ex.Message}", ActivityLogType.Error));
                            throw;
                        }

                        try
                        {
                            using (System.Drawing.Image image = ImageConverter.Convert(frame.Image, task.Parent.Settings.RenderFormat))
                            {
                                if (image != null)
                                {
                                    Avalonia.Media.Imaging.Bitmap preview = image.ToAvaloniaBitmap();
                                    Dispatcher.UIThread.Post(() => Project.LastImage = preview);
                                    //Clone, image is disposed when leaving the using block (previous clone is disposed by the setter)
                                    LastImage = (System.Drawing.Image)image.Clone();
                                }
                                else
                                    Dispatcher.UIThread.Post(() => Project.LastImage = Statics.NoPreviewImage);
                            }
                        }
                        catch (Exception ex)
                        {
                            //The frame is already saved, a failed preview must not fail (and retry) it
                            Console.WriteLine($"Failed to preview queue frame {task.Frame}: {ex.Message}");
                        }

                        //Remove bytes
                        frame.Image = null;

                        RefreshInfo();
                        window.RefreshCurrentProject();
                    }, skipFrames);
                    Task.OnProgress += (t, p) =>
                    {
                        if (p >= 1)
                            FinishedAllFrames = true;
                        RefreshInfo();
                    };
                    Project.SetRenderTask(Task);
                    RefreshInfo();

                    Task.FileID = manager.UpdateFileVersion(Project.BlendFile, Project.UseNetworkedPath);
                    
                    if(!Project.UseNetworkedPath)
                        await manager.Sync(Project.BlendFile, window.UseSyncCompression);
                    else
                        await manager.Sync(Project.BlendFile, Project.NetworkPathWindows, Project.NetworkPathLinux, Project.NetworkPathMacOS);

                    await System.Threading.Tasks.Task.Delay(500);

                    bool renderedAnim = await Task.Render();
                    //await Task.RenderAnimation(Settings.Frame, Settings.Frame + Frames);
                    if (!renderedAnim || Task.Cancelled)
                    {
                        StoppedEarly(window);
                        return;
                    }

                    FinishedAllFrames = true;
                    Project.SetRenderTask(null);
                    RefreshInfo();
                    window.RefreshCurrentProject();
                    queueWatch.Stop();
                    _owner.NotifyRenderCompleted(BuildCompletionInfo(true, queueWatch.Elapsed, null, manager));
                }
            }
            catch (Exception ex)
            {
                if (Task != null && !Task.Consumed)
                    Task.Cancel();
                Exception = ex.Message;
                Cancelled = true;
                Project.SetRenderTask(null);
                RefreshInfo();
                window.RefreshCurrentProject();
                queueWatch.Stop();
                _owner.NotifyRenderCompleted(BuildCompletionInfo(false, queueWatch.Elapsed, ex.Message, manager));
            }
        }

        private void StoppedEarly(RenderWindow window)
        {
            Cancelled = true;
            Project.SetRenderTask(null);
            RefreshInfo();
            window?.RefreshCurrentProject();
        }

        public void RefreshInfo()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Active)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressPercentage)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Completed)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRendering)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQueued)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cancelled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDeletable)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCancelable)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SettingsSummary)));
            });
        }


        public async Task CancelQueueItem()
        {
            Cancelled = true;
            if (Task != null && Task.Consumed)
                await Task.Cancel();

            RefreshInfo();
        }
        public async Task OpenQueueItem()
        {
            try
            {
                if (Completed)
                {
                    Avalonia.Media.Imaging.Bitmap bitmap;
                    //Convert under the lock so a new frame can't dispose the image mid-conversion
                    lock (_lastImageLock)
                    {
                        if (_lastImage == null)
                            return;
                        bitmap = _lastImage.ToAvaloniaBitmap();
                    }
                    BitmapViewer.Show(_owner, Project.BlendFile, bitmap);
                }
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
            }
        }
        public async Task DeleteQueueItem()
        {
            _owner.RemoveQueueItem(this);
        }
    }
}
