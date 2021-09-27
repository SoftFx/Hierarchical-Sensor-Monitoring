using HSMServer.Core.Model.Authentication;

namespace HSMServer.Model.ViewModel
{
    public interface ITreeViewManager
    {
        public TreeViewModel GetTreeViewModel(User user);
        public void AddOrCreate(User user, TreeViewModel model);
    }
}
