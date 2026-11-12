using HSMServer.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // The HSM Agent moves itself off a retired port (44330/44333) only when 443 of the same host
    // answers testConnection with the server marker (#1655): a bare 200 from a foreign service must
    // not be enough. The header name is duplicated in src/agent/src/update_checker.cpp.
    public class SensorsTestConnectionTests
    {
        [Fact]
        public void TestConnection_answers_ok_with_the_server_marker()
        {
            var controller = new SensorsController(null, null, null, null, null, null)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            var result = controller.TestConnection();

            Assert.IsType<OkResult>(result);
            Assert.Equal("1", controller.Response.Headers[SensorsController.ServerMarkerHeader].ToString());
        }

        [Fact]
        public void The_marker_name_is_the_one_the_agent_checks()
        {
            Assert.Equal("X-Hsm-Server", SensorsController.ServerMarkerHeader);
        }
    }
}
