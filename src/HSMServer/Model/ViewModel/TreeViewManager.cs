using HSMServer.Core.Authentication;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.MonitoringCoreInterface;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class TreeViewManager : ITreeViewManager
    {
        private readonly Dictionary<string, TreeViewModel> _treeModels;
        private readonly object _lockObj = new object();
        private readonly ISensorsInterface _sensorsInterface;
        private readonly IUserEvents _userEvents;

        public TreeViewManager(IUserEvents userEvents, ISensorsInterface sensorsInterface)
        {
            _treeModels = new Dictionary<string, TreeViewModel>();
            _sensorsInterface = sensorsInterface;
            
            _userEvents = userEvents;
            _userEvents.UpdateUserEvent += UpdateUserEventHandler;
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

        private void UpdateUserEventHandler(object _, User user)
        {
            lock (_lockObj)
            {
                _treeModels[user.UserName] = new TreeViewModel(_sensorsInterface.GetSensorsTree(user));
            }
        }

        public void Dispose() =>
            _userEvents.UpdateUserEvent -= UpdateUserEventHandler;
    }
}
