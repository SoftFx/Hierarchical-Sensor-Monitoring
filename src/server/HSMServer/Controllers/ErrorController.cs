using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    [AllowAnonymous]
    public class ErrorController : Controller
    {
        public IActionResult Index(ErrorViewModel model = null)
        {
            if (model == null)
            {
                ErrorViewModel errorViewModel = new ErrorViewModel();
                errorViewModel.StatusCode = $"Status code: {HttpContext.Response.StatusCode}";

                return View(errorViewModel);
            }

            return View(model);
        }
    }
}
