using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using HSMServer.Constants;
using HSMServer.Model.Validators;
using System.Linq;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using System.Net.Mime;

namespace HSMServer.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private readonly IUserManager _userManager;
        private readonly IMonitoringCore _monitoringCore;

        public AccountController(IUserManager userManager, IMonitoringCore monitoringCore)
        {
            _userManager = userManager;
            _monitoringCore = monitoringCore;
        }

        [AllowAnonymous]
        [ActionName(nameof(Index))]
        public IActionResult Index()
        {
            return View(new LoginViewModel());
        }

        [AllowAnonymous]
        [ActionName(nameof(Authenticate))]
        [Consumes("application/x-www-form-urlencoded")]
        //[ValidateAntiForgeryToken]
        public async Task<IActionResult> Authenticate([FromForm]LoginViewModel model)
        {
            LoginValidator validator = new LoginValidator(_userManager);
            var results = validator.Validate(model);
            if (!results.IsValid) 
            {
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return RedirectToAction("Index", "Home");
            }

            //var user = _userManager.Authenticate(model.Login, model.Password);
            //if (user == null) return RedirectToAction("Index", "Home");

            TempData.Remove(TextConstants.TempDataErrorText);
            await Authenticate(model.Login, model.KeepLoggedIn);

            return RedirectToAction("Index", "Home");
        }

        public void Logout()
        {
            TempData.Remove(TextConstants.TempDataErrorText);
            HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            RedirectToAction("Index", "Home");
        }

        //public IActionResult GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        //{
        //    var pagedUsers = _userManager.GetUsersPage(page, pageSize);

        //    ViewData[TextConstants.ViewDataPageNumber] = page;
        //    ViewData[TextConstants.ViewDataPageSize] = pageSize;

        //    return View(pagedUsers.Select(u => new UserViewModel(u)).ToList());
        //}
        public IActionResult Users()
        {
            var users = _userManager.Users;
            //var users = _userManager.GetUsersPage(2, 2);

            return View(users.Select(x => new UserViewModel(x)).ToList());
        }

        public void RemoveUser([FromQuery(Name = "Username")] string username)
        {
            _userManager.RemoveUser(username);
        }

        [HttpPost]
        public void CreateUser([FromBody] UserViewModel model)
        {
            UserValidator validator = new UserValidator(_userManager);
            var results = validator.Validate(model);
            if (!results.IsValid)
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);              
            
            else 
                _userManager.AddUser(model.Username, string.Empty, string.Empty,
                HashComputer.ComputePasswordHash(model.Password), model.Role, model.ProductKeys);
        }

        [HttpPost]
        public void UpdateUser([FromBody] UserViewModel userViewModel)
        {
            var currentUser = _userManager.Users.First(x => x.UserName.Equals(userViewModel.Username));
            userViewModel.Password = currentUser.Password;
            User user = GetModelFromViewModel(userViewModel);

            _userManager.UpdateUser(user);
        }

        private async Task Authenticate(string login, bool keepLoggedIn)
        {
            var claims = new List<Claim> { new Claim(ClaimsIdentity.DefaultNameClaimType, login) };
            ClaimsIdentity id = new ClaimsIdentity(claims, "ApplicationCookie",
                ClaimsIdentity.DefaultNameClaimType, ClaimsIdentity.DefaultRoleClaimType);

            AuthenticationProperties properties = new AuthenticationProperties();
            properties.IsPersistent = keepLoggedIn;
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id),
                properties);
  
        }

        private User GetModelFromViewModel(UserViewModel userViewModel)
        {
            User user = new User()
            {
                UserName = userViewModel.Username,
                Password = userViewModel.Password,//HashComputer.ComputePasswordHash(userViewModel.Password),
                Role = userViewModel.Role,
                AvailableKeys = userViewModel.ProductKeys
            };
            return user;
        }
    }
}
