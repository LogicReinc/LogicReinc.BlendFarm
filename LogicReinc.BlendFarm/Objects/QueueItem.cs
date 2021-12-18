using Avalonia.Threading;
using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Windows;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
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

        public string State => (!string.IsNullOrEmpty(Exception)) ? Exception : (!Cancelled ? (Task != null ? $"Rendering {Task.Progress * 100: 0.##}%" : "Queued") : "Cancelled");

        public string Exception { get; set; }

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

        public System.Drawing.Bitmap LastBitmap { get; set; }

        public QueueItem(RenderWindow owner, OpenBlenderProject proj, RenderManagerSettings settings, string saveTo = null, int frames = 1)
        {
            _owner = owner;
            Frames = frames;
            Project = proj;
            Settings = settings;
            SaveTo = saveTo;
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

        public async Task Execute(RenderWindow window, BlendFarmManager manager)
        {
            try
            {
                if (Frames <= 1)
                {
                    //Normal Render
                    Task = manager.GetRenderTask(Project.BlendFile, Project.BlendFileDependencyPath, Settings, (st, bitmap) =>
                    {
                        //Apply image to canvas
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Project.LastBitmap = bitmap.ToAvaloniaBitmap();
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

                    Task.FileID = manager.UpdateFileVersion(Project.BlendFile);

                    await manager.Sync(Project.BlendFile, window.UseSyncCompression);
                    Thread.Sleep(500);

                    System.Drawing.Bitmap final = await Task.Render();
                    LastBitmap = final;

                    if (!string.IsNullOrEmpty(SaveTo))
                        final.Save(SaveTo);

                    //Apply final to canvas
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Project.LastBitmap = final.ToAvaloniaBitmap();

                        Project.SetRenderTask(null);
                        RefreshInfo();
                    });
                    window.RefreshCurrentProject();
                }
                else
                {
                    //Animation
                    if (string.IsNullOrEmpty(FrameFormat))
                        throw new ArgumentException("Missing frameformat for animation");

                    //Normal Render
                    Task = manager.GetRenderTask(Project.BlendFile, Project.BlendFileDependencyPath, Settings, null, async (task, frame) =>
                    {

                        string filePath = Path.Combine(SaveTo, FrameFormat.Replace("#", task.Frame.ToString()));

                        try
                        {
                            frame.Save(filePath);
                        }
                        catch (Exception ex)
                        {
                            MessageWindow.Show(_owner, "Frame Save Error", $"Animation frame {task.Frame} failed to save due to:" + ex.Message);
                            return;
                        }

                        Project.LastBitmap = frame.ToAvaloniaBitmap();

                        LastBitmap = frame;
                        RefreshInfo();
                        window.RefreshCurrentProject();
                    });
                    Task.OnProgress += (t, p) =>
                    {
                        if (p >= 1)
                            FinishedAllFrames = true;
                        RefreshInfo();
                    };
                    Project.SetRenderTask(Task);
                    RefreshInfo();

                    Task.FileID = manager.UpdateFileVersion(Project.BlendFile);

                    await manager.Sync(Project.BlendFile, window.UseSyncCompression);

                    Thread.Sleep(500);

                    await Task.RenderAnimation(Settings.Frame, Settings.Frame + Frames);

                    FinishedAllFrames = true;
                    Project.SetRenderTask(null);
                    RefreshInfo();
                    window.RefreshCurrentProject();
                }
            }
            catch (Exception ex)
            {
                if (!Task.Consumed)
                    Task.Cancel();
                Exception = ex.Message;
                Cancelled = true;
                Project.SetRenderTask(null);
                RefreshInfo();
                window.RefreshCurrentProject();
            }
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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsQueued)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cancelled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDeletable)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCancelable)));
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
                    if (!string.IsNullOrEmpty(SaveTo))
                        BitmapViewer.Show(_owner, Project.BlendFile, LastBitmap.ToAvaloniaBitmap());
                    else
                        BitmapViewer.Show(_owner, Project.BlendFile, LastBitmap.ToAvaloniaBitmap());
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
