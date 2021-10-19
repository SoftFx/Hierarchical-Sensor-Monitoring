using HSMServer.Core.Model.Authentication;

namespace HSMServer.Core.Authentication.UserObserver
{
    /// <summary>
    /// Observer interface for the Observer design pattern
    /// </summary>
    public interface IUserObserver
    {
        void UserUpdated(User user);
    }
}