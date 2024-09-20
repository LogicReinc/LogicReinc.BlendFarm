using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Client.ImageTypes;
using LogicReinc.BlendFarm.Client.Tasks;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageConverter = LogicReinc.BlendFarm.Client.ImageTypes.ImageConverter;

namespace LogicReinc.BlendFarm.Shared
{
    /// <summary>
    /// Describes a render task for a specific blender version and blend file
    /// </summary>
    public abstract class RenderTask : INotifyPropertyChanged
    {
        /// <summary>
        /// Used to identify the task
        /// </summary>
        public string ID { get; set; } = Guid.NewGuid().ToString();
        /// <summary>
        /// Used to differentiate between different instances of the program
        /// </summary>
        public string SessionID { get; set; }
        /// <summary>
        /// Version of blender to use
        /// </summary>
        public string Version { get; set; }
        /// <summary>
        /// FileID of the blender file
        /// </summary>
        public long FileID { get; set; }

        /// <summary>
        /// Settings on what to render
        /// </summary>
        public RenderManagerSettings Settings { get; set; }

        public List<RenderNode> Nodes = new List<RenderNode>();

        /// <summary>
        /// Progress of render
        /// </summary>
        public double Progress { get; set; }

        /// <summary>
        /// Event whenever a tile has finished rendering
        /// </summary>
        public event Action<RenderSubTask, Image> OnTileProcessed;
        /// <summary>
        /// Event whenever the result bitmap is changed
        /// </summary>
        public event Action<RenderSubTask, Image> OnResultUpdated;
        /// <summary>
        /// Event whenever progress is made
        /// </summary>
        public event Action<RenderTask, double> OnProgress;
        /// <summary>
        /// Event whenever relevant ui properties change
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// If the task has been executed
        /// </summary>
        public bool Consumed { get; set; }
        /// <summary>
        /// If the task has been cancelled
        /// </summary>
        public bool Cancelled { get; set; }

        protected List<RenderNode> _usedNodes = new List<RenderNode>();

        public RenderTask(List<RenderNode> nodes, string session, string version, long fileId, RenderManagerSettings settings = null)
        {
            if (settings == null)
                settings = new RenderManagerSettings();
            Settings = settings;
            Nodes = nodes;
            SessionID = session;
            Version = version;
            FileID = fileId;
        }


        public async Task<bool> Render()
        {
            if (Consumed)
                throw new InvalidOperationException("Already started render..");
            Consumed = true;

            List<RenderNode> pool = Nodes.Where(x => x.Connected).ToList();
            List<RenderNode> validNodes = new List<RenderNode>();

            await Task.WhenAll(pool.Select(async x =>
            {
                bool hasVersion = await x.CheckVersion(Version);
                if (!hasVersion)
                    return;

                bool hasFile = await x.CheckSyncFile(SessionID, FileID);
                if (!hasFile)
                    return;

                bool isBusy = await x.IsBusy();
                if (isBusy)
                    return;

                lock (validNodes)
                {
                    validNodes.Add(x);
                }
            }));

            if (validNodes.Count == 0)
                throw new InvalidOperationException("No ready nodes available");
            _usedNodes = validNodes;

            foreach (RenderNode useNode in _usedNodes)
                useNode.UpdateException("");

            bool result = await Execute();

            return result;
        }
        protected abstract Task<bool> Execute();


        /// <summary>
        /// Determines device performance based on past renders or default
        /// </summary>
        public Dictionary<RenderNode, decimal> GetRelativePerformance(List<RenderNode> nodes)
        {
            Dictionary<RenderNode, decimal> perfs = new Dictionary<RenderNode, decimal>();
            if (nodes.Count == 0)
                return new Dictionary<RenderNode, decimal>();
            if (Settings.UseAutoPerformance && !nodes.Any(x=>x.PerformanceScorePP <= 0))
            {
                decimal total = (decimal)nodes.Sum(x => (x.PerformanceScorePP > 0) ? x.PerformanceScorePP : x.Cores);
                foreach (RenderNode node in nodes)
                {
                    decimal perf = (node.PerformanceScorePP > 0) ? node.PerformanceScorePP : node.Cores;
                    perfs.Add(node,  perf / total);
                }
            }
            else
            {
                decimal total = (decimal)nodes.Sum(x => (x.Performance > 0) ? x.Performance : x.Cores);
                foreach(RenderNode node in nodes)
                {
                    decimal perf = (node.Performance > 0) ? (decimal)node.Performance : node.Cores;
                    perfs.Add(node, perf / total);
                }
            }
            return perfs;
        }

        
        /// <summary>
        /// Cancel ongoing rendering
        /// </summary>
        public async Task Cancel()
        {
            Cancelled = true;
            if(Consumed)
                await Task.WhenAll(_usedNodes.Select(async x =>
                {
                    await x.CancelRender(SessionID);
                }));
        }


        //SubTask Split
        /// <summary>
        /// Splits up file into subtasks among validNodes based on performance
        /// Single subtask per node (see RenderSplit description)
        /// </summary>
        private Dictionary<RenderNode, RenderSubTask> GetSplitSubTasks(List<RenderNode> validNodes, bool isVertical = false, decimal overlap = 0.01m)
        {
            Dictionary<RenderNode, decimal> shares = GetRelativePerformance(validNodes);

            Dictionary<RenderNode, RenderSubTask> tasks = new Dictionary<RenderNode, RenderSubTask>();
            decimal offsetX = 0;
            foreach (RenderNode node in validNodes)
            {
                decimal share = shares[node];

                if (node == validNodes.Last())
                    share = 1 - offsetX;

                decimal startX = offsetX;
                decimal endX = offsetX + share;

                if (overlap > 0)
                {
                    startX = Math.Max(0, startX - overlap);
                    endX = Math.Min(1, endX + overlap);
                }

                if (!isVertical)
                    tasks.Add(node, new RenderSubTask(this, startX, endX, 0, 1, Settings.Frame));
                else
                    tasks.Add(node, new RenderSubTask(this, 0, 1, startX, endX, Settings.Frame));

                offsetX += share;
            }
            return tasks;
        }

        //SubTask Chunked
        /// <summary>
        /// Splits up file into subtasks based on Settings.ChunkWidth/Height
        /// </summary>
        protected List<RenderSubTask> GetChunkedSubTasks(decimal overlap = 0.003m)
        {
            decimal blockSizeX = Settings.ChunkWidth;
            decimal blockSizeY = Settings.ChunkHeight;
            if (blockSizeX <= 0m)
                blockSizeX = 0.05m;
            if (blockSizeY <= 0)
                blockSizeY = 0.05m;


            int tilesHorizontal = (int)Math.Ceiling((decimal)1 / blockSizeX);
            int tilesVertical = (int)Math.Ceiling((decimal)1 / blockSizeY);


            //This workaround strategy requires equal size tiles..
            if (Settings.Strategy == RenderStrategy.SplitChunked && Settings.BlenderUpdateBugWorkaround)
            {
                int tileXPix = (int)Math.Floor(Settings.OutputWidth * Settings.ChunkWidth);
                int tileYPix = (int)Math.Floor(Settings.OutputHeight * Settings.ChunkHeight);

                blockSizeX = (decimal)1 / ((int)Settings.OutputWidth / tileXPix);
                blockSizeY = (decimal)1 / ((int)Settings.OutputHeight / tileYPix);

                tilesHorizontal = (int)Math.Floor((decimal)1 / blockSizeX);
                tilesVertical = (int)Math.Floor((decimal)1 / blockSizeY);
            }

            List<RenderSubTask> tasks = new List<RenderSubTask>();

            for (int x = 0; x < tilesHorizontal; x++)
                for (int y = 0; y < tilesVertical; y++)
                {
                    decimal startX = x * blockSizeX;
                    decimal endX = Math.Min(1, (x + 1) * blockSizeX);
                    decimal startY = y * blockSizeY;
                    decimal endY = Math.Min(1, (y + 1) * blockSizeY);

                    startX = Math.Max(0, startX - overlap);
                    endX = Math.Min(1, endX + overlap);
                    startY = Math.Max(0, startY - overlap);
                    endY = Math.Min(1, endY + overlap);

                    if (startX >= 1 || startY >= 1)
                        continue;


                    tasks.Add(new RenderSubTask(this, startX, endX, startY, endY, Settings.Frame));
                }

            return tasks;
        }
        /// <summary>
        /// Creates a queue from a list of subtasks based on the provided order (eg. Center)
        /// </summary>
        protected ConcurrentQueue<RenderSubTask> GetTaskQueueInOrder(List<RenderSubTask> queue, TaskOrder order)
        {
            List<RenderSubTask> newOrder = new List<RenderSubTask>();

            switch (order)
            {
                case TaskOrder.Center:
                    newOrder = queue.OrderBy(x => {

                        decimal posX = x.X + (x.X2 - x.X) / 2;
                        decimal posY = x.Y + (x.Y2 - x.Y) / 2;

                        decimal distanceX = 0.5m - posX;
                        decimal distanceY = 0.5m - posY;

                        double distance = (double)Math.Sqrt(Math.Pow((double)distanceX, 2) + Math.Pow((double)distanceY, 2));

                        return distance;
                    }).ToList();
                    break;

                default:
                    newOrder = queue.ToList();
                    break;
            }


            return new ConcurrentQueue<RenderSubTask>(newOrder);
        }


        //Execution
        protected async Task HandleQueueAsync(RenderNode[] nodes, RenderSubTask[] tasks, Action<RenderSubTask, SubTaskResult> onFinished, Action<RenderNode, RenderSubTask, Exception> onException)
        {
            await Task.Run(() => HandleQueue(nodes, tasks, onFinished, onException));
        }
        protected void HandleQueue(RenderNode[] nodes, RenderSubTask[] tasks, Action<RenderSubTask, SubTaskResult> onFinished, Action<RenderNode, RenderSubTask, Exception> onException)
        {
            ConcurrentQueue<RenderSubTask> subtasks = new ConcurrentQueue<RenderSubTask>(tasks);

            ForceParallel(nodes, (node) =>
            {
                while (subtasks.Count > 0 && !Cancelled)
                {
                    RenderSubTask task = null;
                    if (!subtasks.TryDequeue(out task))
                        continue;
                    SubTaskResult taskPart = null;
                    try
                    {
                        taskPart = ExecuteSubTask(node, task);

                        if (taskPart.Image == null)
                            throw new Exception(taskPart.Exception?.Message ?? "Unknown Remote Exception");

                        //ProcessTile(task, (Bitmap)taskPart.Image, ref g, ref result, ref drawLock);

                        onFinished(task, taskPart);
                    }
                    catch (TaskCanceledException ex)
                    {
                        if (Cancelled)
                            return;
                    }
                    catch (Exception ex)
                    {
                        if (task != null)
                            subtasks.Enqueue(task);
                        node.UpdateException($"Render fail: {ex.Message}");
                        onException(node, task, ex);
                        return;
                    }
                }
            });

        }

        /// <summary>
        /// Blocking executes a batch of subtasks on node (calls async underneath)
        /// </summary>
        protected SubTaskBatchResult ExecuteSubTasks(RenderNode node, Action<RenderSubTask, RenderBatchResult> onResult, params RenderSubTask[] tasks)
        {
            return  ExecuteSubTasksAsync(node, onResult, tasks).GetAwaiter().GetResult();
        }
        /// <summary>
        /// Async executes a batch of subtasks on node
        /// </summary>
        protected async Task<SubTaskBatchResult> ExecuteSubTasksAsync(RenderNode node, Action<RenderSubTask, RenderBatchResult> onResult, params RenderSubTask[] tasks)
        {
            List<RenderRequest> reqs = tasks.Select(x => x.GetRenderRequest()).ToList();

            List<RenderBatchResult> results = new List<RenderBatchResult>();
            Action<RenderNode, RenderBatchResult> onAnyResult = (bnode, result) =>
            {
                RenderSubTask task = tasks.FirstOrDefault(x => x.ID == result.TaskID);
                if (task != null)
                {
                    lock (results)
                        results.Add(result);
                    onResult(task, result);
                }
            };

            Stopwatch time = new Stopwatch();
            time.Start();
            try
            {
                node.OnBatchResult += onAnyResult;


                RenderBatchRequest req = RenderSubTask.GetRenderBatchRequest(ID, tasks);
                req.Settings.ForEach(x => x.RenderType = node.RenderType);
                RenderBatchResponse resp = await node.RenderBatch(req);

                if (resp == null)
                    return new SubTaskBatchResult(new Exception("Render fail: (null)"));

                if (resp.Success == false)
                    return new SubTaskBatchResult(new Exception("Render fail: " + resp.Message));

                if (req.Settings.Count > 0) 
                {
                    decimal pixelsRendered = req.Settings.Sum(x => (x.Height * (x.Y2 - x.Y)) * (x.Width * (x.X2 - x.X)));
                    node.UpdatePerformance((int)pixelsRendered, (int)time.ElapsedMilliseconds);
                }

            }
            finally
            {
                node.OnBatchResult -= onAnyResult;
                time.Stop();
            }
            return new SubTaskBatchResult(results.ToArray());
        }
        /// <summary>
        /// Blocking executes a subtask on node (calls async underneath)
        /// </summary>
        protected SubTaskResult ExecuteSubTask(RenderNode node, RenderSubTask task)
        {
            return ExecuteSubTaskAsync(node, task).GetAwaiter().GetResult();
        }
        /// <summary>
        /// Async executes a subtask on node
        /// </summary>
        protected async Task<SubTaskResult> ExecuteSubTaskAsync(RenderNode node, RenderSubTask task)
        {
            RenderRequest req = task.GetRenderRequest();

            byte[] result = null;

            Stopwatch time = new Stopwatch();
            time.Start();
            try
            {

                req.Settings.RenderType = node.RenderType;

                RenderResponse resp = await node.Render(req);


                if (resp == null)
                    return new SubTaskResult(new Exception("Render fail: (null)"));

                if (resp.Success == false)
                    return new SubTaskResult(new Exception("Render fail: " + resp.Message));

                //Update Performance
                node.UpdatePerformance((int)((req.Settings.Height * (req.Settings.Y2 - req.Settings.Y)) * (req.Settings.Width * (req.Settings.X2 - req.Settings.X))), 
                    (int)time.ElapsedMilliseconds);

                result = resp.Data;

                resp = null;
            }
            finally
            {
                time.Stop();
            }

            return new SubTaskResult(result);
        }

        protected void ProcessTile(RenderSubTask task, SubTaskResult tresult, ref Graphics g, ref Bitmap result, ref object drawLock, bool dontDraw = false)
        {
            using(Image img = ImageConverter.Convert(tresult.Image, task.Parent.Settings.RenderFormat))
            {
                ProcessTile(task, img, ref g, ref result, ref drawLock, dontDraw);
            }
        }
        /// <summary>
        /// Handles an incoming tile and trigger the events as well as drawing the tile to an image/graphics
        /// </summary>
        protected void ProcessTile(RenderSubTask task, Image part, ref Graphics g, ref Bitmap result, ref object drawLock, bool dontDraw = false)
        {
            if (Cancelled)
                return;

            if (!dontDraw)
            {
                lock (drawLock)
                {
                    if (result == null)
                    {
                        result = new Bitmap(part.Width, part.Height);
                        g = Graphics.FromImage(result);
                    }
                    if (task.Crop)
                    {
                        int tileWidth = ((int)((task.X2 - task.X) * task.Parent.Settings.OutputWidth));
                        int tileHeight = ((int)((task.Y2 - task.Y) * task.Parent.Settings.OutputHeight));
                        int posX = (int)(task.X * task.Parent.Settings.OutputWidth);
                        int posY = (int)(task.Parent.Settings.OutputHeight - task.Y * task.Parent.Settings.OutputHeight - tileHeight);

                        g.DrawImage(part, posX, posY, tileWidth, tileHeight);
                    }
                    else
                        g.DrawImage(part, 0, 0, task.Parent.Settings.OutputWidth, task.Parent.Settings.OutputHeight);
                }
            if (OnResultUpdated != null)
                OnResultUpdated(task, result);
            }
            if (OnTileProcessed != null)
                OnTileProcessed(task, part);
        }

        //Util
        protected void TriggerPropUpdate(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        protected void ChangeProgress(double progress)
        {
            Progress = (double)progress;
            TriggerPropUpdate(nameof(Progress));
            OnProgress?.Invoke(this, progress);
        }

        protected void ForceParallel<T>(IEnumerable<T> collections, Action<T> act)
        {
            List<Thread> threads = collections.Select(node =>
            {
                Thread thread = new Thread(() =>
                {
                    act(node);
                });
                thread.Start();
                return thread;
            }).ToList();

            foreach (Thread t in threads)
                t.Join();
        }

        public static RenderTask GetImageRenderTask(List<RenderNode> nodes, string session, string version, long fileId, RenderManagerSettings settings = null)
        {
            RenderTask task = null;
            switch(settings?.Strategy)
            {
                case RenderStrategy.Chunked:
                    task = new ChunkedTask(nodes, session, version, fileId, settings);
                    break;
                case RenderStrategy.SplitChunked:
                    task = new SplitChunkedTask(nodes, session, version, fileId, settings);
                    break;
                default:
                case RenderStrategy.SplitHorizontal:
                case RenderStrategy.SplitVertical:
                    task = new SplittedTask(nodes, session, version, fileId, settings, settings?.Strategy == RenderStrategy.SplitVertical);
                    break;
            }
            return task;
        }


        /// <summary>
        /// Internally manages batch subtask results or error
        /// </summary>
        protected class SubTaskBatchResult
        {
            public RenderBatchResult[] Results { get; set; }
            public Exception Exception { get; set; }

            public SubTaskBatchResult(Exception ex)
            {
                Exception = ex;
            }
            public SubTaskBatchResult(params RenderBatchResult[] result)
            {
                Results = result;
            }
        }
    }

    /// <summary>
    /// Manages subtask results or error
    /// </summary>
    public class SubTaskResult
    {
        public byte[] Image { get; set; }
        public Exception Exception { get; set; }

        public SubTaskResult(Exception ex)
        {
            Exception = ex;
        }
        public SubTaskResult(byte[] image)
        {
            Image = image;
        }
    }

}
