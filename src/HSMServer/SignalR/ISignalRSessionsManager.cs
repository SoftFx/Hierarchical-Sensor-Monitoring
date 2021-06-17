using System.Collections.Generic;
using HSMServer.Authentication;

namespace HSMServer.SignalR
{
    public interface ISignalRSessionsManager
    {
        void AddConnection(User user, string id);
        string GetConnectionId(User user);
        List<User> GetCurrentUsers();
        List<string> GetCurrentConnectionIds();
        void RemoveConnection(User user);
        Dictionary<User, string> UserConnectionDictionary { get; }
    }
}