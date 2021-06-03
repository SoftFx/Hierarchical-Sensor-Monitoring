using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using HSMServer.Constants;

namespace HSMServer.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        //private readonly IUserService _userService;
        private readonly IUserManager _userManager;

        public AccountController(IUserManager userManager)
        {
            _userManager = userManager;
        }

        [AllowAnonymous]
        [ActionName("Authenticate")]
        [Consumes("application/x-www-form-urlencoded")]
        //[ValidateAntiForgeryToken]
        public async Task<IActionResult> Authenticate([FromForm]LoginModel model)
        {
            if (ValidateModel(model))
            {
                var user = _userManager.Authenticate(model.Login, model.Password);
                if (user != null)
                {
                    TempData.Remove(TextConstants.TempDataErrorText);
                    await Authenticate(model.Login, model.KeepLoggedIn);
                    return RedirectToAction("Index", "Home");
                }
                else
                {
                    TempData[TextConstants.TempDataErrorText] = "Incorrect password or username!";
                }
            }
            

            return RedirectToAction("Index");

            //return BadRequest(new { message = "Incorrect password or username" });
        }

        [AllowAnonymous]
        public IActionResult Index()
        {
            return View(new LoginModel());
        }

        private async Task Authenticate(string login, bool keepLoggedIn)
        {
            var claims = new List<Claim>{new Claim(ClaimsIdentity.DefaultNameClaimType, login)};
            ClaimsIdentity id = new ClaimsIdentity(claims, "ApplicationCookie", 
                ClaimsIdentity.DefaultNameClaimType, ClaimsIdentity.DefaultRoleClaimType);

            AuthenticationProperties properties = new AuthenticationProperties();
            properties.IsPersistent = keepLoggedIn;
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id),
                properties);
        }
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index");
        }

        private bool ValidateModel(LoginModel model)
        {
            if (string.IsNullOrEmpty(model.Login))
            {
                TempData[TextConstants.TempDataErrorText] = "Login must not be empty!";
                return false;
            }

            if (string.IsNullOrEmpty(model.Password))
            {
                TempData[TextConstants.TempDataErrorText] = "Password must not be empty!";
                return false;
            }

            return true;
        }
    }
}
