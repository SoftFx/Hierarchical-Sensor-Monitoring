using HSMServer.Authentication;
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

        public Dictionary<string, TreeViewModel> UserTreeViewDictionary
        {
            get
            {
                Dictionary<string, TreeViewModel> dictionary;
                lock (_lockObj)
                {
                    dictionary = new Dictionary<string, TreeViewModel>(_treeModels);
                }

                return dictionary;
            }
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
                    throw e;
                    result = null;
                }
            }
            return result;
        }

        public void AddOrCreate(User user, TreeViewModel model)
        {
            if (_treeModels.ContainsKey(user.UserName))
                _treeModels[user.UserName] = model;
            else
                _treeModels.Add(user.UserName, model);
        }

        //class UserComparer : IEqualityComparer<User>
        //{
        //    public bool Equals(User x, User y)
        //    {
        //        if (/*x.CertificateThumbprint == y.CertificateThumbprint  && */(x.UserName == y.UserName))
        //            return true;

        //        return false;
        //    }

        //    public int GetHashCode(User obj)
        //    {
        //        return obj.GetHashCode();
        //    }
        //}
    }
}
