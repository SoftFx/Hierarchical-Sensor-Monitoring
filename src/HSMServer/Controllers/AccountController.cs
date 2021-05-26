using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Formatters;

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

        //[AllowAnonymous]
        [HttpPost("Authenticate")]
        [ActionName("Authenticate")]
        [Consumes("application/x-www-form-urlencoded")]
        //[ValidateAntiForgeryToken]
        public async Task<IActionResult> Authenticate([FromForm]LoginModel model)
        {
            var user = _userService.Authenticate(model.Login, model.Password); 
            if (user != null) 
            { 
                await Authenticate(model.Login);
                return RedirectToAction("Index", "Home");
            }

            return BadRequest(new { message = "Incorrect password or username" });
        }

        private async Task Authenticate(string login)
        {
            var claims = new List<Claim>{new Claim(ClaimsIdentity.DefaultNameClaimType, login)};
            ClaimsIdentity id = new ClaimsIdentity(claims, "ApplicationCookie", 
                ClaimsIdentity.DefaultNameClaimType, ClaimsIdentity.DefaultRoleClaimType);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id));
        }
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Main", "Home");
        }
    }
}
