using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Components
{
    public class TreeNodeViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke(NodeViewModel node) => View("TreeNode", node);
    }
}
