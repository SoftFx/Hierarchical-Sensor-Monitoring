using HSMServer.Authentication;
using HSMServer.HtmlHelpers;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.ApiControllers
{
    [ApiController]
    [Route("[controller]")]
    public class ViewApiController : Controller
    {
        private readonly IMonitoringCore _monitoringCore;

        public ViewApiController(IMonitoringCore monitoringCore)
        {
            _monitoringCore = monitoringCore;
        }
        [HttpGet]
        public string GetList(string url, int port, string path)
        {
            //var result = ApiConnector.GetTree(url.Split(';')[0], port);
            var result = _monitoringCore.GetSensorsTree(HttpContext.User as User);

            TreeViewModel tree = new TreeViewModel(result);

            return ListHelper.CreateList(path, tree).ToString();
        }
    }
}
