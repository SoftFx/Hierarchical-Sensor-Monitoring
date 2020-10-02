using System;
using HSMgRPC.DataLayer;
using HSMgRPC.Model;
using Microsoft.AspNetCore.Mvc;

namespace HSMgRPC.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SensorsController : ControllerBase
    {
        private readonly DatabaseClass _dataStorage;
        public SensorsController(DatabaseClass dataStorage)
        {
            _dataStorage = dataStorage;
        }
        
        [HttpPost("")]
        public ActionResult<JobResult> Post([FromBody] JobResult jobResult)
        {
            try
            {
                bool res = _dataStorage.PutSingleSensorData(jobResult);
                if (res)
                {
                    return Ok(jobResult);
                }

                return UnprocessableEntity(jobResult);
            }
            catch (Exception e)
            {
                BadRequest(jobResult);
            }

            return BadRequest(jobResult);
        }

        [HttpPost("string")]
        public ActionResult<string> Post([FromBody] string serialized)
        {
            return Ok(serialized);
        }
    }
}
