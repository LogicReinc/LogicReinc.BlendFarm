using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client.Tasks
{
    public abstract class QueuedExecutionTask : RenderTask
    {
        public QueuedExecutionTask(List<RenderNode> nodes, string session, string version, long fileId, RenderManagerSettings settings = null) : base(nodes, session, version, fileId, settings)
        {
        }

        protected override async Task<bool> Execute()
        {
            ChangeProgress(0);

            Setup();

            RenderSubTask[] tasks = GetTasks();

            await HandleQueueAsync(_usedNodes.ToArray(), tasks, HandleResult, HandleException);

            return true;
        }


        protected abstract void Setup();
        protected abstract RenderSubTask[] GetTasks();
        protected abstract void Roundup();

        protected abstract void HandleResult(RenderSubTask task, SubTaskResult result);
        protected virtual void HandleException(RenderNode node, RenderSubTask task, Exception ex) { }
    }
}
