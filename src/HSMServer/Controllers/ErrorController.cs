using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    [AllowAnonymous]
    public class ErrorController : Controller
    {
        [Route("/error")]
        public IActionResult Error()
        {
            ErrorViewModel errorViewModel = new ErrorViewModel();
            errorViewModel.StatusCode = $"Status code: {HttpContext.Response.StatusCode.ToString()}";
            return Index(errorViewModel);
        }

        public IActionResult Index(ErrorViewModel viewModel)
        {
            return View(viewModel);
        }
    }
}
