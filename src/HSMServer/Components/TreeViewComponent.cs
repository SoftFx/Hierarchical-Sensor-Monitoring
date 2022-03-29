using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Components
{
    public class TreeViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke(TreeViewModel tree) => View("Tree", tree);
    }
}
