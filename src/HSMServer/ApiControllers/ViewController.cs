using HSMServer.Authentication;
using HSMServer.HtmlHelpers;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.ApiControllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ViewController : Controller
    {
        private readonly IMonitoringCore _monitoringCore;

        public ViewController(IMonitoringCore monitoringCore)
        {
            _monitoringCore = monitoringCore;
        }

        [HttpGet("GetList/{path}")]
        public string GetList(string path)
        {
            var result = _monitoringCore.GetSensorsTree(HttpContext.User as User);

            TreeViewModel tree = new TreeViewModel(result);

            return ListHelper.CreateList(path, tree).ToString();
        }
    }
}
