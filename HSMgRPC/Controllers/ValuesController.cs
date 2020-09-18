using HSMgRPC.Model;
using Microsoft.AspNetCore.Mvc;

namespace HSMgRPC.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        [HttpPost]
        public ActionResult<string> Post([FromBody] SampleData data)
        {
            SampleData data2COpy = data;

            return Ok(data);
        }

        [HttpGet]
        public ActionResult<string> Get()
        {
            return Ok("pswdhflasndf';lsdj;fdjas;fajsd;afj;dasjf");
        }
    }
}
