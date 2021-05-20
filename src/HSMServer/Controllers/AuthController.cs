using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model;
using Microsoft.AspNetCore.Authorization;

namespace HSMServer.Controllers
{
    [Authorize]
    [Route("[controller]")]
    [ApiController]
    internal class AuthController : ControllerBase
    {
        private readonly IUserService _userService;

        public AuthController(IUserService userService)
        {
            _userService = userService;
        }

        [AllowAnonymous]
        [HttpPost("authenticate")]
        public async Task<IActionResult> Authenticate([FromBody] LoginModel model)
        {
            var user = await _userService.Authenticate(model.Login, model.Password);

            if (user == null)
            {
                return BadRequest(new { message = "Incorrect password or username" });
            }

            return Ok(user);
        }
    }
}
