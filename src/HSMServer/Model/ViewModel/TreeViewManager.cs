using HSMServer.Authentication;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.ViewModel
{
    public class TreeViewManager : ITreeViewManager
    {
        private readonly Dictionary<User, TreeViewModel> _treeModels;
        private readonly object _lockObj = new object();

        public TreeViewManager()
        {
            _treeModels = new Dictionary<User, TreeViewModel>(new UserComparer());
        }

        public Dictionary<User, TreeViewModel> UserTreeViewDictionary
        {
            get
            {
                Dictionary<User, TreeViewModel> dictionary;
                lock (_lockObj)
                {
                    dictionary = new Dictionary<User, TreeViewModel>(_treeModels, comparer: new UserComparer());
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
                    result = _treeModels[user];
                }
                catch (Exception e)
                {
                    result = null;
                }
            }
            return result;
        }

        public void AddOrCreate(User user, TreeViewModel model)
        {
            if (_treeModels.ContainsKey(user))
                _treeModels[user] = model;
            else
                _treeModels.Add(user, model);
        }

        class UserComparer : IEqualityComparer<User>
        {
            public bool Equals(User x, User y)
            {
                if (/*x.CertificateThumbprint == y.CertificateThumbprint  && */(x.UserName == y.UserName))
                    return true;

                return false;
            }

            public int GetHashCode(User obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
