using HSMServer.Core.Model.Authentication;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class TreeViewManager : ITreeViewManager
    {
        private readonly Dictionary<string, TreeViewModel> _treeModels;
        private readonly object _lockObj = new object();

        public TreeViewManager()
        {
            _treeModels = new Dictionary<string, TreeViewModel>();
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
    }
}
