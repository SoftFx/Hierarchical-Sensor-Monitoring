using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.Core.Authentication.UserObserver
{
    /// <summary>
    /// MultiThread observable implementation for the Observer design pattern
    /// </summary>
    public class UserObservableImpl : IUserObservable
    {
        private readonly object _observersSync = new object();
        private readonly List<IUserObserver> _observers;

        public UserObservableImpl()
        {
            _observers = new List<IUserObserver>();
        }
        public void AddObserver(IUserObserver observer)
        {
            lock (_observersSync)
            {
                _observers.Add(observer);
            }
        }

        public void FireUserChanged(User user)
        {
            lock (_observersSync)
            {
                foreach (var observer in _observers)
                {
                    observer.UserUpdated(user);
                }
            }   
        }

        public void RemoveObserver(IUserObserver observer)
        {
            lock (_observersSync)
            {
                _observers.Remove(observer);
            }
        }
    }
}