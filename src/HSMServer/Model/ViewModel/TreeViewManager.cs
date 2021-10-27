using HSMServer.Core.Authentication.UserObserver;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.MonitoringCoreInterface;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class TreeViewManager : ITreeViewManager, IUserObserver
    {
        private readonly Dictionary<string, TreeViewModel> _treeModels;
        private readonly object _lockObj = new object();
        private readonly ISensorsInterface _sensorsInterface;

        public TreeViewManager(IUserObservable userObservable, ISensorsInterface sensorsInterface)
        {
            _treeModels = new Dictionary<string, TreeViewModel>();
            _sensorsInterface = sensorsInterface;
            userObservable.AddObserver(this);
        }

        public TreeViewModel GetTreeViewModel(User user)
        {
            TreeViewModel result;
            lock (_lockObj)
            {
                try
                {
                    result = _treeModels[user.UserName];
                }
                catch (Exception e)
                {
                    return null;                   
                }
            }
            return result;
        }

        public void RemoveViewModel(User user)
        {
            lock (_lockObj)
            {
                _treeModels.Remove(user.UserName);
            }   
        }

        public void AddOrCreate(User user, TreeViewModel model)
        {
            lock (_lockObj)
            {
                _treeModels[user.UserName] = model;
            }
        }

        public void UserUpdated(User user)
        {
            lock (_lockObj)
            {
                _treeModels[user.UserName] = new TreeViewModel(_sensorsInterface.GetSensorsTree(user));
            }
        }
    }
}
