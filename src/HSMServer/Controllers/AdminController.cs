using HSMServer.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    [AuthorizeIsAdmin(true)]
    public class AdminController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
