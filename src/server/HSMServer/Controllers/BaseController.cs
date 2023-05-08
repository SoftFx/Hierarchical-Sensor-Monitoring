using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    public abstract class BaseController : Controller
    {
        protected static readonly JsonResult _emptyJsonResult = new(new EmptyResult());
        protected static readonly EmptyResult _emptyResult = new();


        public User CurrentUser => HttpContext.User as User;
    }
}
