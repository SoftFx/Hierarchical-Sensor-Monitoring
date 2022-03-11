using HSMServer.Core.Authentication;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.MonitoringCoreInterface;
using System.Collections.Concurrent;

namespace HSMServer.Model.ViewModel
{
    public class TreeViewManager : ITreeViewManager
    {
        private readonly ConcurrentDictionary<string, TreeViewModel> _treeModels;
        private readonly ISensorsInterface _sensorsInterface;
        private readonly IUserManager _userManager;

        public TreeViewManager(IUserManager userManager, ISensorsInterface sensorsInterface)
        {
            _treeModels = new ConcurrentDictionary<string, TreeViewModel>();
            _sensorsInterface = sensorsInterface;

            _userManager = userManager;
            _userManager.UpdateUserEvent += UpdateUserEventHandler;
        }

        public TreeViewModel GetTreeViewModel(User user)
        {
            _treeModels.TryGetValue(user.UserName, out var result);

            return result;
        }

        public void RemoveViewModel(User user)
        {
            _treeModels.TryRemove(user.UserName, out _);
        }

        public void AddOrCreate(User user, TreeViewModel model)
        {
            _treeModels[user.UserName] = model;
        }

        private void UpdateUserEventHandler(User user)
        {
            _treeModels[user.UserName] = new TreeViewModel(_sensorsInterface.GetSensorsTree(user));
        }

        public void Dispose() =>
            _userManager.UpdateUserEvent -= UpdateUserEventHandler;
    }
}
