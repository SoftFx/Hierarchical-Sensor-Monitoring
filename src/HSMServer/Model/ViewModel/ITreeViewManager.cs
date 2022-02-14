using HSMServer.Core.Model.Authentication;
using System;

namespace HSMServer.Model.ViewModel
{
    public interface ITreeViewManager : IDisposable
    {
        public TreeViewModel GetTreeViewModel(User user);
        public void RemoveViewModel(User user);
        public void AddOrCreate(User user, TreeViewModel model);
    }
}
