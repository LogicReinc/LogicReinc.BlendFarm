using LogicReinc.BlendFarm.Client;
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

namespace LogicReinc.BlendFarm.Shared
{
    /// <summary>
    /// Describes a render task for a specific blender version and blend file
    /// </summary>
    public class RenderTask : INotifyPropertyChanged
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

        public RenderManagerSettings Settings { get; set; }

        public List<RenderNode> Nodes = new List<RenderNode>();

        public double Progress { get; set; }

        public event Action<RenderSubTask, Bitmap> OnTileProcessed;
        public event Action<RenderSubTask, Bitmap> OnResultUpdated;
        public event Action<RenderTask, double> OnProgress;
        public event PropertyChangedEventHandler PropertyChanged;

        public bool Consumed { get; set; }
        public bool Cancelled { get; set; }

        private List<RenderNode> _usedNodes = new List<RenderNode>();

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

        /// <summary>
        /// Execute task and render the image on valid nodes
        /// </summary>
        public async Task<Bitmap> Render()
        {
            if (Consumed)
                throw new InvalidOperationException("Already started render..");
            Consumed = true;
            try
            {
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

                Bitmap result = null;

                Action<RenderSubTask> onSubTaskFinished = (task) =>
                {
                    Progress += task.Value;
                    TriggerPropUpdate(nameof(Progress));
                    OnProgress?.Invoke(this, Progress);
                };
                Progress = 0;

                switch (Settings.Strategy)
                {
                    case RenderStrategy.Chunked:
                        result = await RenderChunked(validNodes, onSubTaskFinished);
                        break;
                    case RenderStrategy.Split:
                        result = await RenderSplit(validNodes, onSubTaskFinished);
                        break;
                    case RenderStrategy.SplitChunked:
                        result = await RenderSplitChunked(validNodes, onSubTaskFinished);
                        break;
                }

                return result;
            }
            catch(Exception ex)
            {
                throw;
            }
            finally
            {
                //Consumed = false;
            }
        }

        public async Task<bool> RenderAnimation(int start, int end)
        {
            if (Consumed)
                throw new InvalidOperationException("Already started render..");
            Consumed = true;
            try
            {
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

                int framesFinished = 0;
                int framesTotal = end - start;

                Action<RenderSubTask> onSubTaskFinished = (task) =>
                {
                    framesFinished++;
                    Progress = (double)framesFinished / framesTotal;
                    TriggerPropUpdate(nameof(Progress));
                    OnProgress?.Invoke(this, Progress);
                };
                Progress = 0;

                //StartRenderSplit

                object drawLock = new object();
                Bitmap result = new Bitmap(Settings.OutputWidth, Settings.OutputHeight);
                Graphics g = Graphics.FromImage(result);


                ConcurrentQueue<RenderSubTask> queue = new ConcurrentQueue<RenderSubTask>();
                for(int i = start; i < end; i++)
                    queue.Enqueue(new RenderSubTask(this, 0, 1, 0, 1, i));


                int finished = 0;
                List<string> exceptions = new List<string>();
                return await Task.Run(() =>
                {
                    ForceParallel(validNodes, (node) =>
                    {
                        string lastException = null;

                        while (queue.Count > 0)
                        {
                            RenderSubTask task = null;
                            if (!queue.TryDequeue(out task))
                                continue;
                            SubTaskResult taskPart = null;
                            try
                            {
                                taskPart = ExecuteSubTask(node, task);

                                ProcessTile(task, (Bitmap)taskPart.Image, ref g, ref result, ref drawLock);

                                onSubTaskFinished?.Invoke(task);
                            }
                            catch (TaskCanceledException ex)
                            {
                                if (Cancelled)
                                    return;
                            }
                            catch (Exception ex)
                            {
                                if (task != null)
                                    queue.Enqueue(task);
                                node.UpdateException($"Render fail: {ex.Message}");
                                exceptions.Add(ex.Message);
                                return;
                            }
                            if (taskPart.Exception != null)
                            {
                                node.UpdateException(taskPart.Exception.Message);
                                lastException = taskPart.Exception.Message;
                                Thread.Sleep(1000);
                                continue;
                            }

                            Image part = taskPart.Image;

                            ProcessTile(task, (Bitmap)part, ref g, ref result, ref drawLock);
                        }
                    });

                    if (finished != validNodes.Count)
                        throw new AggregateException("Not all frames rendered", exceptions.Select(x => new Exception(x)));

                    return true;
                });
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
                //Consumed = false;
            }
        }


        //Strategies
        /// <summary>
        /// Renders file with Settings in maximum chunks based on Cores and Performance
        /// eg. 3 valid nodes of equal performance will render a single part on each node with a 0.33 ratio
        /// </summary>
        private async Task<Bitmap> RenderSplit(List<RenderNode> validNodes, Action<RenderSubTask> onSubTaskFinished = null)
        {
            object drawLock = new object();
            Bitmap result = new Bitmap(Settings.OutputWidth, Settings.OutputHeight);
            Graphics g = Graphics.FromImage(result);

            Dictionary<RenderNode, RenderSubTask> assignment = GetSplitSubTasks(validNodes);

            int finished = 0;
            List<string> exceptions = new List<string>();
            return await Task.Run(() =>
            {
                ForceParallel(validNodes, (node) =>
                {
                    RenderSubTask task = assignment[node];
                    if (task == null)
                        return;

                    string lastException = null;
                    bool rendered = false;
                    for (int i = 0; i < 3; i++)
                    {
                        if (Cancelled)
                            return;

                        SubTaskResult taskPart = null;
                        try
                        {
                            taskPart = ExecuteSubTask(node, task);
                        }
                        catch(TaskCanceledException ex)
                        {
                            if (Cancelled)
                                return;
                        }
                        catch (Exception ex)
                        {
                            node.UpdateException($"[{i + 1}/3] " + ex.Message);
                            lastException = ex.Message;
                            Thread.Sleep(1000);
                            continue;
                        }
                        if (taskPart.Exception != null)
                        {
                            node.UpdateException($"[{i+1}/3] " + taskPart.Exception.Message);
                            lastException = taskPart.Exception.Message;
                            Thread.Sleep(1000);
                            continue;
                        }

                        Image part = taskPart.Image;

                        ProcessTile(task, (Bitmap)part, ref g, ref result, ref drawLock);

                        onSubTaskFinished?.Invoke(task);
                        finished++;
                        rendered = true;
                        return;
                    }
                    if (!rendered && lastException != null)
                        exceptions.Add(lastException);
                });

                if (finished != validNodes.Count)
                    throw new AggregateException("Not all tiles rendered", exceptions.Select(x => new Exception(x)));

                return result;
            });
        }
        /// <summary>
        /// Renders file with Settings in pre-defined chunksizes. Each chunk will be rendered independently without batching
        /// Very slow due to blender being initialized for every chunk. But tasks are consumed optimally
        /// Benefits from live update as tiles finish they are send back to client
        /// </summary>
        private async Task<Bitmap> RenderChunked(List<RenderNode> validNodes, Action<RenderSubTask> onSubTaskFinished = null)
        {
            object drawLock = new object();
            Bitmap result = new Bitmap(Settings.OutputWidth, Settings.OutputHeight);
            Graphics g = Graphics.FromImage(result);


            List<RenderSubTask> tasks = GetChunkedSubTasks();
            ConcurrentQueue<RenderSubTask> queue = GetTaskQueueInOrder(tasks, Settings.Order);

            List<string> exceptions = new List<string>();

            //Force parallelization
            return await Task.Run(() =>
            {
                ForceParallel(validNodes, (node) =>
                {
                    int errorCount = 0;
                    while (queue.Count > 0)
                    {
                        RenderSubTask task = null;
                        if (!queue.TryDequeue(out task))
                            continue;
                        try
                        {
                            SubTaskResult taskResult = ExecuteSubTask(node, task);

                            ProcessTile(task, (Bitmap)taskResult.Image, ref g, ref result, ref drawLock);

                            onSubTaskFinished?.Invoke(task);
                        }
                        catch(Exception ex)
                        {
                            errorCount++;
                            if (task != null)
                                queue.Enqueue(task);
                            node.UpdateException($"Render fail [{errorCount+1}/3]: {ex.Message}");
                            exceptions.Add(ex.Message);
                            if (errorCount > 2)
                                return;
                        }
                    }
                });

                if (queue.Count > 0)
                    throw new AggregateException("Not all tiles rendered", exceptions.Select(x=>new Exception(x)));

                if (g != null)
                    g.Dispose();

                return result;
            });
        }
        /// <summary>
        /// Renders file with Settings in pre-defined chunksizes. Chunks will be assigned based on Cores and Performance.
        /// Renders relatively quick but some overhead, will render using a single blender instance.
        /// Benefits from live update as tiles finish they are send back to the client
        /// eg. 3 valid nodes of equal performance with chunk size of about 10%
        /// Each node will get x chunks of 10% fitting in 33% of the image.
        /// </summary>
        private async Task<Bitmap> RenderSplitChunked(List<RenderNode> validNodes, Action<RenderSubTask> onSubTaskFinished = null)
        {
            object drawLock = new object();
            Bitmap result = new Bitmap(Settings.OutputWidth, Settings.OutputHeight);
            Graphics g = Graphics.FromImage(result);

            Dictionary<RenderNode, decimal> shares = GetRelativePerformance(validNodes);

            List<RenderSubTask> tasks = GetChunkedSubTasks();

            ConcurrentQueue<RenderSubTask> queue = GetTaskQueueInOrder(tasks, Settings.Order);

            Dictionary<RenderNode, List<RenderSubTask>> assignment = validNodes.ToDictionary(x => x, y => new List<RenderSubTask>());

            //Divide Tasks
            //Assume every core has the same performance..
            while(queue.Count > 0)
            {
                RenderSubTask nextTask = null;
                queue.TryDequeue(out nextTask); //Single threaded, always true

                RenderNode nextNode = validNodes.OrderBy(node =>
                {
                    int nrTiles = assignment[node].Count;
                    return nrTiles * (1 / shares[node]);
                }).FirstOrDefault();
                assignment[nextNode].Add(nextTask);
            }

            //Run tasks over all rendernodes
            return await Task.Run(() =>
            {
                ForceParallel(validNodes, (node) =>
                {
                    List<RenderSubTask> nodeTasks = assignment[node];
                    if (nodeTasks.Count == 0)
                        return;

                    try
                    {
                        SubTaskBatchResult resp = ExecuteSubTasks(node, (rsbt, rbr) =>
                        {

                            Image bitmap = null;
                            using (MemoryStream str = new MemoryStream(rbr.Data))
                                bitmap = Bitmap.FromStream(str);

                            ProcessTile(rsbt, (Bitmap)bitmap, ref g, ref result, ref drawLock);

                            onSubTaskFinished?.Invoke(rsbt);
                        }, assignment[node].ToArray());
                        if (resp?.Exception != null)
                            node.UpdateException(resp.Exception.Message);
                    }
                    catch(TaskCanceledException ex)
                    {
                        node.UpdateException("Cancalled");
                        return;
                    }
                    catch(AggregateException ex)
                    {
                        node.UpdateException(string.Join(", ", ex.InnerExceptions.Select(x => x.Message)));
                    }
                    catch(Exception ex)
                    {
                        node.UpdateException(ex.Message);
                    }
                });

                if (g != null)
                    g.Dispose();
                return result;
            });
        }

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
        private Dictionary<RenderNode, RenderSubTask> GetSplitSubTasks(List<RenderNode> validNodes)
        {
            Dictionary<RenderNode, decimal> shares =  GetRelativePerformance(validNodes);

            Dictionary<RenderNode, RenderSubTask> tasks = new Dictionary<RenderNode, RenderSubTask>();
            decimal offsetX = 0;
            foreach (RenderNode node in validNodes)
            {
                decimal share = shares[node];

                if (node == validNodes.Last())
                    share = 1 - offsetX;
                tasks.Add(node, new RenderSubTask(this, offsetX, offsetX + share, 0, 1, Settings.Frame));
                offsetX += share;
            }
            return tasks;
        }

        //SubTask Chunked
        /// <summary>
        /// Splits up file into subtasks based on Settings.ChunkWidth/Height
        /// </summary>
        private List<RenderSubTask> GetChunkedSubTasks()
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

                    if (startX >= 1 || startY >= 1)
                        continue;

                    tasks.Add(new RenderSubTask(this, startX, endX, startY, endY, Settings.Frame));
                }

            return tasks;
        }
        /// <summary>
        /// Creates a queue from a list of subtasks based on the provided order (eg. Center)
        /// </summary>
        private ConcurrentQueue<RenderSubTask> GetTaskQueueInOrder(List<RenderSubTask> queue, TaskOrder order)
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
        /// <summary>
        /// Blocking executes a batch of subtasks on node (calls async underneath)
        /// </summary>
        private SubTaskBatchResult ExecuteSubTasks(RenderNode node, Action<RenderSubTask, RenderBatchResult> onResult, params RenderSubTask[] tasks)
        {
            return  ExecuteSubTasksAsync(node, onResult, tasks).GetAwaiter().GetResult();
        }
        /// <summary>
        /// Async executes a batch of subtasks on node
        /// </summary>
        private async Task<SubTaskBatchResult> ExecuteSubTasksAsync(RenderNode node, Action<RenderSubTask, RenderBatchResult> onResult, params RenderSubTask[] tasks)
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
        private SubTaskResult ExecuteSubTask(RenderNode node, RenderSubTask task)
        {
            return ExecuteSubTaskAsync(node, task).GetAwaiter().GetResult();
        }
        /// <summary>
        /// Async executes a subtask on node
        /// </summary>
        private async Task<SubTaskResult> ExecuteSubTaskAsync(RenderNode node, RenderSubTask task)
        {
            RenderRequest req = task.GetRenderRequest();

            Image bitmap = null;

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

                using (MemoryStream str = new MemoryStream(resp.Data))
                    bitmap = Bitmap.FromStream(str);
                resp = null;
            }
            finally
            {
                time.Stop();
            }

            return new SubTaskResult(bitmap);
        }
        /// <summary>
        /// Handles an incoming tile and trigger the events as well as drawing the tile to an image/graphics
        /// </summary>
        private void ProcessTile(RenderSubTask task, Bitmap part, ref Graphics g, ref Bitmap result, ref object drawLock, bool dontDraw = false)
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
        private void TriggerPropUpdate(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void ForceParallel<T>(IEnumerable<T> collections, Action<T> act)
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


        /// <summary>
        /// Internally manages subtask results or error
        /// </summary>
        private class SubTaskResult
        {
            public Image Image { get; set; }
            public Exception Exception { get; set; }

            public SubTaskResult(Exception ex)
            {
                Exception = ex;
            }
            public SubTaskResult(Image image)
            {
                Image = image;
            }
        }
        /// <summary>
        /// Internally manages batch subtask results or error
        /// </summary>
        private class SubTaskBatchResult
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

}
