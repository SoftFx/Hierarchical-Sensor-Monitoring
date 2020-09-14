using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HSMgRPC.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        [HttpPost]
        public ActionResult<string> Post([FromBody] string stringData)
        {
            var copy = (string)stringData.Clone();

            return Ok(stringData);
        }

        [HttpGet]
        public ActionResult<string> Get()
        {
            return Ok("pswdhflasndf';lsdj;fdjas;fajsd;afj;dasjf");
        }
    }
}
