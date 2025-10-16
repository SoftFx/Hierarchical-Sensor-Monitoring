using System.Threading.Tasks;
using HSMCommon.TaskResult;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    internal readonly struct StoreItem
    {
        public StoreItem(IUpdateRequest request, bool isAwaitable)
        {
            UpdateRequest = request;

            if (isAwaitable)
                TaskCompletionSource = new TaskCompletionSource<TaskResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal IUpdateRequest UpdateRequest { get; }
        internal TaskCompletionSource<TaskResult> TaskCompletionSource { get; }
        internal bool IsAwaitable => TaskCompletionSource != null;
    }

}
