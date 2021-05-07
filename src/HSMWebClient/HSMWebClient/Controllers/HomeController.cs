using HSMWebClient.Constants;
using HSMWebClient.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HSMWebClient.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        IWebHostEnvironment _appEnvironment;

        public HomeController(IWebHostEnvironment appEnvironment, ILogger<HomeController> logger)
        {
            _logger = logger;
            _appEnvironment = appEnvironment;
        }

        public IActionResult Index()
        {
            return View(new ConnectionViewModel
            {
                Url = "https://localhost",//"https://hsm.dev.soft-fx.eu",
                Port = 44333,
            });
        }
        [HttpPost]
        public IActionResult Index(ConnectionViewModel model)
        {
            var result = ApiConnector.GetTree(model.Url, model.Port);

            model.Tree = new TreeViewModel(result);

            return View(model);
        }

    }
}
