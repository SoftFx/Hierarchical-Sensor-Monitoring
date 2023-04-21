using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    public class BaseController : Controller
    {
        public User CurrentUser => HttpContext.User as User;
    }
}
