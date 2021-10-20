using HSMServer.Core.Model.Authentication;

namespace HSMServer.Core.Authentication.UserObserver
{
    /// <summary>
    /// Observable interface for the Observer design pattern
    /// </summary>
    public interface IUserObservable
    {
        void AddObserver(IUserObserver observer);
        void FireUserChanged(User user);
        void RemoveObserver(IUserObserver observer);
    }
}