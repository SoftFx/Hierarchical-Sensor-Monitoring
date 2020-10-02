using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMgRPC.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HSMgRPC.Controllers
{
    /// <summary>
    /// Simple test controller for checking endpoint settings & accessibility
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
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
            return $"string {DateTime.Now.ToShortDateString()} : {DateTime.Now.ToShortTimeString()}";
        }

        [HttpPost]
        public ActionResult<string> Post([FromBody]SampleData input)
        {
            _logger.LogInformation($"Received string {input.Data}");
            return Ok(input);
        }
    }
}
