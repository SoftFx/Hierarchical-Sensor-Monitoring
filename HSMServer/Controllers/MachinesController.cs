using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HSMServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MachinesController : ControllerBase
    {
        private readonly ILogger<MachinesController> _logger;

        public MachinesController(ILogger<MachinesController> logger)
        {
            _logger = logger;
        }

        [Authorize]
        [HttpPut("{machineName}")]
        public ActionResult<string> Put()
        {
            return "Success";
        }


    }
}