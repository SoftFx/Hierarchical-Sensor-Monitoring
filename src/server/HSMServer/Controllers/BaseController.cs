using HSMServer.Authentication;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    public abstract class BaseController : Controller
    {
        protected static readonly JsonResult _emptyJsonResult = new(new EmptyResult());
        protected static readonly EmptyResult _emptyResult = new();

        protected readonly IUserManager _userManager;


        public User CurrentUser => HttpContext.User as User;

        public User StoredUser => _userManager[CurrentUser.Id];


        protected BaseController(IUserManager userManager)
        {
            _userManager = userManager;
        }
    }
}
