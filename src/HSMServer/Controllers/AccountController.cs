using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace HSMServer.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private const string _tempDataErrorText = "ErrorMessage";
        private readonly IUserService _userService;

        public AccountController(IUserService userService)
        {
            _userService = userService;
        }

        [AllowAnonymous]
        [ActionName("Authenticate")]
        [Consumes("application/x-www-form-urlencoded")]
        //[ValidateAntiForgeryToken]
        public async Task<IActionResult> Authenticate([FromForm]LoginModel model)
        {
            if (ValidateModel(model))
            {
                var user = _userService.Authenticate(model.Login, model.Password);
                if (user != null)
                {
                    TempData.Remove(_tempDataErrorText);
                    await Authenticate(model.Login, model.KeepLoggedIn);
                    return RedirectToAction("Index", "Home");
                }
                else
                {
                    TempData[_tempDataErrorText] = "Incorrect password or username!";
                }
            }

            return RedirectToAction("Index", "Account");
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
        public void Logout()
        {
            TempData.Remove(_tempDataErrorText);
            HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme, new AuthenticationProperties {RedirectUri = Url.Action(nameof(Index))});
        }

        private bool ValidateModel(LoginModel model)
        {
            if (string.IsNullOrEmpty(model.Login))
            {
                TempData[_tempDataErrorText] = "Login must not be empty!";
                return false;
            }

            if (string.IsNullOrEmpty(model.Password))
            {
                TempData[_tempDataErrorText] = "Password must not be empty!";
                return false;
            }

            return true;
        }
    }
}
