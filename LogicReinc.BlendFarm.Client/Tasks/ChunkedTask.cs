using LogicReinc.BlendFarm.Client.ImageTypes;
using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using ImageConverter = LogicReinc.BlendFarm.Client.ImageTypes.ImageConverter;

namespace LogicReinc.BlendFarm.Client.Tasks
{
    public class ChunkedTask : QueuedExecutionTask, IImageTask
    {
        private object _drawLock = new object();
        private Bitmap result;
        private Graphics g;

        public Image FinalImage { get; private set; }

        public ChunkedTask(List<RenderNode> nodes, string session, string version, long fileId, RenderManagerSettings settings = null) : base(nodes, session, version, fileId, settings)
        {
        }

        protected override void Setup()
        {
            result = new Bitmap(Settings.OutputWidth, Settings.OutputHeight);
            g = Graphics.FromImage(result);
        }
        protected override RenderSubTask[] GetTasks()
        {
            return GetTaskQueueInOrder(GetChunkedSubTasks(), Settings.Order);
        }

        protected override void Roundup()
        {
            if (g != null)
                g.Dispose();

            FinalImage = result;
        }

        protected override void HandleResult(RenderSubTask task, SubTaskResult tresult)
        {
            ChangeProgress(Progress + task.Value);
            using (Image img = ImageConverter.Convert(tresult.Image, task.Parent.Settings.RenderFormat))
                ProcessTile(task, img, ref g, ref result, ref _drawLock);
        }


        /// <summary>
        /// Splits up file into subtasks based on Settings.ChunkWidth/Height
        /// </summary>
        private List<RenderSubTask> GetChunkedSubTasks(decimal overlap = 0.003m)
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
        private RenderSubTask[] GetTaskQueueInOrder(List<RenderSubTask> queue, TaskOrder order)
        {
            RenderSubTask[] newOrder;

            switch (order)
            {
                case TaskOrder.Center:
                    newOrder = queue.OrderBy(x =>
                    {

                        decimal posX = x.X + (x.X2 - x.X) / 2;
                        decimal posY = x.Y + (x.Y2 - x.Y) / 2;

                        decimal distanceX = 0.5m - posX;
                        decimal distanceY = 0.5m - posY;

                        double distance = (double)Math.Sqrt(Math.Pow((double)distanceX, 2) + Math.Pow((double)distanceY, 2));

                        return distance;
                    }).ToArray();
                    break;

                default:
                    newOrder = queue.ToArray();
                    break;
            }


            return newOrder;
        }
    }
}
