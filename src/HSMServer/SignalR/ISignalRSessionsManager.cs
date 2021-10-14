using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.SignalR
{
    public interface ISignalRSessionsManager
    {
        void AddConnection(User user, string id);
        void RemoveConnection(User user, string id);
        Dictionary<User, List<string>> UserConnectionDictionary { get; }
        int GetConnectionsCount(User user);
    }
}