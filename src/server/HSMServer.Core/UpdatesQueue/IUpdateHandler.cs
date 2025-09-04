

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public interface IUpdateHandler
    {
        void ProcessRequest(IUpdatesQueue queue, IUpdateRequest request);
    }
}