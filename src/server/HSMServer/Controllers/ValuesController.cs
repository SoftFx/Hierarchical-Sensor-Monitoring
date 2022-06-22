using HSMServer.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;

namespace HSMServer.Controllers
{
    /// <summary>
    /// Simple test controller for checking endpoint settings & accessibility
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class ValuesController : ControllerBase
    {
        private readonly ILogger<ValuesController> _logger;

        public ValuesController(ILogger<ValuesController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public ActionResult<string> Get()
        {
            _logger.LogInformation($"ValuesController: GET at {DateTime.Now:F}");
            return $"Now is {DateTime.Now:F}";
        }

        [HttpPost]
        public ActionResult<string> Post([FromBody]SampleData input)
        {
            _logger.LogInformation($"Received string {input.Data}");
            return Ok(input);
        }
    }
}
