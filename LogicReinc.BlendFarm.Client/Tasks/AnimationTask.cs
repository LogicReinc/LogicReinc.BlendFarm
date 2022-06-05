using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client.Tasks
{
    public class AnimationTask : QueuedExecutionTask
    {
        private object _lock = new object();
        private List<Exception> _exceptions;

        private int _framesTotal = 0;
        private int _finished = 0;

        public int StartFrame { get; set; }
        public int EndFrame { get; set; }

        public event Action<RenderSubTask, SubTaskResult> OnFrameResult;

        public AnimationTask(List<RenderNode> nodes, string session, string version, long fileId, int start, int end, RenderManagerSettings settings = null) : base(nodes, session, version, fileId, settings)
        {
            StartFrame = start;
            EndFrame = end;
        }

        protected override void Setup()
        {
            _exceptions = new List<Exception>();

            _finished = 0;
            _framesTotal = EndFrame - StartFrame + 1;
        }

        protected override RenderSubTask[] GetTasks()
        {
            List<RenderSubTask> tasks = new List<RenderSubTask>();
            for (int i = StartFrame; i <= EndFrame; i++)
                tasks.Add(new RenderSubTask(this, 0, 1, 0, 1, i));
            return tasks.ToArray();
        }

        protected override void Roundup()
        {
            if (_finished != _framesTotal)
                throw new AggregateException($"Not all frames rendered ({_finished}/{_framesTotal})", _exceptions);
        }

        protected override void HandleResult(RenderSubTask task, SubTaskResult result)
        {
            lock (_lock)
                _finished++;

            ChangeProgress((double)_finished / _framesTotal);

            OnFrameResult?.Invoke(task, result);
        }
    }
}
