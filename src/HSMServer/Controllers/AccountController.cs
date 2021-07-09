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
using HSMServer.Attributes;
using HSMServer.Filters;
using HSMServer.Model.ViewModel;
using HSMServer.Configuration;
using System;

namespace HSMServer.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private readonly IUserManager _userManager;
        private readonly IConfigurationProvider _configurationProvider;

        public AccountController(IUserManager userManager, IConfigurationProvider configurationProvider)
        {
            _userManager = userManager;
            _configurationProvider = configurationProvider;
        }

        [AllowAnonymous]
        [UnauthorizedAccessOnlyFilter]
        [ActionName(nameof(Index))]
        public IActionResult Index()
        {
            return View(new LoginViewModel());
        }

        [AllowAnonymous]
        [UnauthorizedAccessOnlyFilter]
        public IActionResult Registration([FromQuery(Name = "Cipher")] string cipher,
            [FromQuery(Name = "Tag")] string tag, [FromQuery(Name = "Nonce")] string nonce)        {
            var model = new RegistrationViewModel();

            if (!string.IsNullOrEmpty(cipher) && !string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(nonce)) 
            {
                var key = _configurationProvider.ReadConfigurationObject(ConfigurationConstants.AesEncryptionKey);
                byte[] keyBytes = AESCypher.ToBytes(key.Value);

                var result = AESCypher.Decrypt(cipher.Replace(' ', '+'), nonce.Replace(' ', '+'), tag.Replace(' ', '+'), keyBytes);
                var matches = result.Split('_');

                var productKey = matches[0];
                var role = matches[1];
                var expidationDate = DateTime.Parse(matches[2]);

                if (expidationDate < DateTime.UtcNow) return View(new RegistrationViewModel());

                model.ProductKey = productKey;
                model.Role = role;
            }

            return View(model);
        }

        [AllowAnonymous]
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

            TempData.Remove(TextConstants.TempDataErrorText);
            await Authenticate(model.Username, model.KeepLoggedIn);

            return RedirectToAction("Index", "Home");
        }

        [AllowAnonymous]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> Registrate([FromForm]RegistrationViewModel model)
        {
            RegistrationValidator validator = new RegistrationValidator(_userManager);
            var results = validator.Validate(model);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return View("Registration", model);
            }

            if (!string.IsNullOrEmpty(model.ProductKey) && !string.IsNullOrEmpty(model.Role))
            {
                var products = new List<KeyValuePair<string, ProductRoleEnum>>()
                    { new KeyValuePair<string, ProductRoleEnum>(model.ProductKey, 
                    (ProductRoleEnum)Int32.Parse(model.Role))};
            }

            _userManager.AddUser(model.Username, null, null,
                HashComputer.ComputePasswordHash(model.Password), false);
            await Authenticate(model.Username, true);

            return RedirectToAction("Index", "Home");
        }

        public async Task<IActionResult> Logout()
        {
            TempData.Remove(TextConstants.TempDataErrorText);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        //public IActionResult GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        //{
        //    var pagedUsers = _userManager.GetUsersPage(page, pageSize);

        //    ViewData[TextConstants.ViewDataPageNumber] = page;
        //    ViewData[TextConstants.ViewDataPageSize] = pageSize;

        //    return View(pagedUsers.Select(u => new UserViewModel(u)).ToList());
        //}
        [AuthorizeRole(true)]
        public IActionResult Users()
        {
            var users = _userManager.Users.OrderBy(x => x.UserName).ToList();

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
                HashComputer.ComputePasswordHash(model.Password), model.IsAdmin);
        }

        [HttpPost]
        public void UpdateUser([FromBody] UserViewModel userViewModel)
        {
            var currentUser = _userManager.Users.First(x => x.UserName.Equals(userViewModel.Username));
            userViewModel.Password = currentUser.Password;

            User user = GetModelFromViewModel(userViewModel);
            user.ProductsRoles = currentUser.ProductsRoles;

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
                Password = userViewModel.Password,
                IsAdmin = userViewModel.IsAdmin
            };
            return user;
        }
    }
}
