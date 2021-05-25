using System;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model;
using Microsoft.AspNetCore.Authorization;

namespace HSMServer.Controllers
{
 //   [Authorize]
    [Route("[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly IUserService _userService;

        public AccountController(IUserService userService)
        {
            _userService = userService;
        }

        [AllowAnonymous]
        [HttpPost("Authenticate")]
        //[ValidateAntiForgeryToken]
        public IActionResult Authenticate([FromForm]LoginModel model)
        {
            var user = _userService.Authenticate(model.Login, model.Password);

            if (user == null)
            {
                return BadRequest(new { message = "Incorrect password or username" });
            }

            return Ok();
        }

        //[HttpGet("hello")]
        //public async Task<IActionResult> Hello()
        //{
        //    return Ok(DateTime.Now);
        //}
    }
}
