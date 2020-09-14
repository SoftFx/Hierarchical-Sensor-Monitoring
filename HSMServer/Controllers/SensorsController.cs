using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using HSMCommon.DataObjects;
using HSMServer.Authentication;
using HSMServer.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HSMServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SensorsController : ControllerBase
    {
        private readonly ILogger<SensorsController> _logger;
        public SensorsController(ILogger<SensorsController> logger)
        {
            _logger = logger;
        }

        //[Authorize]
        [HttpGet("{machineName}")]
        public ActionResult<string> Get(string machineName)
        {
            var code = Database.DataStorage.GetSensorsData(machineName, out List<ShortSensorData> results);
            if (code == ReturnCodes.Success)
            {
                string json = JsonSerializer.Serialize(results);
                return json;
            }

            return $"Error: {code.ToString()}";
        }

        [HttpGet("{machineName}/{sensorName}")]
        //[BasicAuth]
        [ServiceFilter(typeof(BasicAuthFilter))]
        public ActionResult<string> Get(string machineName, string sensorName)
        {
            var code = Database.DataStorage.GetSensorsData(machineName, sensorName, out List<ShortSensorData> results);
            if (code == ReturnCodes.Success)
            {
                string json = JsonSerializer.Serialize(results);
                return json;
            }

            return $"Error: {code.ToString()}";
        }

        [HttpGet("{machineName}/{sensorName}/{n}")]
        //[BasicAuth]
        [ServiceFilter(typeof(BasicAuthFilter))]
        public ActionResult<string> Get(string machineName, string sensorName, int n)
        {
            X509Certificate2 cer = Request.HttpContext.Connection.ClientCertificate;

            var code = Database.DataStorage.GetSensorsData(machineName, sensorName, out List<ShortSensorData> results);
            results.Sort((s1, s2) => s2.Time.CompareTo(s1.Time));
            results = results.Take(n).ToList();
            if (code == ReturnCodes.Success)
            {
                string json = JsonSerializer.Serialize(results);
                return json;
            }

            return $"Error: {code.ToString()}";
        }

        [HttpPost]
        public ActionResult<string> Post([FromBody]SensorData sensorData)
        {
            if (sensorData == null)
            {
                return BadRequest();
            }

            //await Task.Factory.StartNew(() => Database.Database.PutData(sensorData));
            if (Database.DataStorage.PutData(sensorData) != ReturnCodes.Success)
            {
                return "Failed to put data!";
            }
            
            return Ok(sensorData);
        }
    }
}