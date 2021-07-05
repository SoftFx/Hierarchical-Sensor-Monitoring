using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    [AllowAnonymous]
    public class ErrorController : Controller
    {
        public IActionResult Index()
        {
            ErrorViewModel errorViewModel = new ErrorViewModel();
            errorViewModel.StatusCode = $"Status code: {HttpContext.Response.StatusCode}";

            return View(errorViewModel);
        }
    }
}
