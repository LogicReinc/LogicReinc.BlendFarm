using LogicReinc.BlendFarm.Client.Tasks;
using LogicReinc.BlendFarm.Shared;
using LogicReinc.BlendFarm.Shared.Communication.RenderNode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LogicReinc.BlendFarm.Client
{
    /// <summary>
    /// Describes a SubTask part of a bigger RenderTask on the Client-side
    /// </summary>
    public class RenderSubTask
    {
        public RenderTask Parent { get; set; }

        public string ID { get; set; } = Guid.NewGuid().ToString();

        //Render Rectangle (0..1)
        public decimal X { get; set; }
        public decimal X2 { get; set; }
        public decimal Y { get; set; }
        public decimal Y2 { get; set; }

        public int Frame { get; set; }

        /// <summary>
        /// Value of work, used for progress
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public bool Crop { get; set; } = false;

        public RenderSubTask(RenderTask parent, decimal x, decimal x2, decimal y, decimal y2, int frame)
        {
            Parent = parent;
            X = x;
            X2 = x2;
            Y = y;
            Y2 = y2;
            Frame = frame;

            long totalPixels = (long)((Parent.Settings.OutputWidth * (X2 - X)) * (Parent.Settings.OutputHeight * (Y2 - Y)));
            Value = ((double)totalPixels) / (Parent.Settings.OutputWidth * Parent.Settings.OutputHeight);

            Crop = Parent.Settings.BlenderUpdateBugWorkaround;
        }

        /// <summary>
        /// Converts task to RenderRequest
        /// </summary>
        /// <returns></returns>
        public RenderRequest GetRenderRequest()
        {
            return new RenderRequest()
            {
                TaskID = ID,
                Version = Parent.Version,
                SessionID = Parent.SessionID,
                FileID = Parent.FileID,
                Settings = ToRenderPacketModel()
            };
        }

        /// <summary>
        /// Converts list of tasks to a batch request
        /// </summary>
        public static RenderBatchRequest GetRenderBatchRequest(string id, params RenderSubTask[] tasks)
        {
            RenderTask mainTask = tasks.FirstOrDefault()?.Parent;
            return new RenderBatchRequest()
            {
                TaskID = id,
                Version = mainTask.Version,
                SessionID = mainTask.SessionID,
                FileID = mainTask.FileID,
                Settings = tasks.Select(x => x.ToRenderPacketModel()).ToList()
            };
        }

        /// <summary>
        /// Add padding around tile (Mostly used Workaround which crops tiles)
        /// </summary>
        public void AddPadding(decimal x, decimal y)
        {
            X = Math.Max(X - x, 0);
            X2 = Math.Min(X2 + x, 1);
            Y = Math.Max(Y - y, 0);
            Y2 = Math.Min(Y2 + y, 1);
        }

        /// <summary>
        /// Converts Task to RenderSettings
        /// </summary>
        public RenderPacketModel ToRenderPacketModel()
        {
            return new RenderPacketModel()
            {
                X = X,
                Y = Y,
                X2 = X2,
                Y2 = Y2,
                Frame = Frame,
                Scene = Parent.Settings.Scene,
                Height = Parent.Settings.OutputHeight,
                Width = Parent.Settings.OutputWidth,
                Samples = Parent.Settings.Samples,
                FPS = Parent.Settings.FPS,
                Denoiser = Parent.Settings.Denoiser,
                TaskID = ID,
                Engine = Parent.Settings.Engine,
                Workaround = Parent.Settings.BlenderUpdateBugWorkaround,
                Crop = Crop || Parent.Settings.BlenderUpdateBugWorkaround,
                RenderFormat = (Parent is AnimationTask) ? Parent.Settings.RenderFormat : ""
            };
        }
    }
}
