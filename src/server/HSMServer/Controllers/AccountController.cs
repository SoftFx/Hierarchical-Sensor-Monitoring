using HSMCommon;
using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Authentication.Sso;
using HSMServer.Constants;
using HSMServer.Extensions;
using HSMServer.Filters;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.Validators;
using HSMServer.Model.ViewModel;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class AccountController : BaseController
    {
        private readonly TreeViewModel _treeViewModel;
        private readonly OidcConfig _oidc;
        private readonly ILogger<AccountController> _logger;


        public AccountController(IUserManager userManager, TreeViewModel treeViewModel, OidcConfig oidc, ILogger<AccountController> logger) : base(userManager)
        {
            _treeViewModel = treeViewModel;
            _oidc = oidc;
            _logger = logger;
        }


        #region Login

        [AllowAnonymous]
        [UnauthorizedAccessOnlyFilter]
        [ActionName(nameof(Index))]
        public IActionResult Index([FromQuery(Name = HsmOidcDefaults.ErrorQueryKey)] SsoSignInError ssoError = SsoSignInError.None)
        {
            return View(new LoginViewModel
            {
                SsoDisplayName = _oidc.IsActive ? _oidc.DisplayName : null,
                SsoError = HsmOidcDefaults.Describe(ssoError),
            });
        }

        // Starts the OIDC sign-in (#1782); 404 when SSO is off, so the route reveals nothing.
        // The challenge runs here rather than as a ChallengeResult: it reads the provider's
        // discovery document first, and a provider that cannot be reached must send the
        // person back to the password form, not to the error page.
        [AllowAnonymous]
        [UnauthorizedAccessOnlyFilter]
        [HttpGet]
        public async Task<IActionResult> Sso()
        {
            if (!_oidc.IsActive)
                return NotFound();

            try
            {
                await HttpContext.ChallengeAsync(HsmOidcDefaults.AuthenticationScheme,
                    new AuthenticationProperties { RedirectUri = HsmOidcDefaults.AfterSignInPath });

                return new EmptyResult();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "SSO sign-in could not start: the identity provider at {Authority} is unavailable.", _oidc.Authority);

                return Redirect(HsmOidcDefaults.LoginErrorPath(SsoSignInError.ProviderUnavailable));
            }
        }

        [AllowAnonymous]
        [Consumes("application/x-www-form-urlencoded")]
        //[ValidateAntiForgeryToken]
        public async Task<IActionResult> Authenticate([FromForm] LoginViewModel model)
        {
            LoginValidator validator = new(_userManager);
            var results = validator.Validate(model);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return Redirect("/Home");
            }

            TempData.Remove(TextConstants.TempDataErrorText);
            await Authenticate(model.Username, model.KeepLoggedIn);

            return Redirect("/Home");
        }

        #endregion

        #region Registration

        [AllowAnonymous]
        [UnauthorizedAccessOnlyFilter]
        public IActionResult Registration([FromQuery(Name = "Cipher")] string cipher,
            [FromQuery(Name = "Tag")] string tag, [FromQuery(Name = "Nonce")] string nonce)
        {
            var model = new RegistrationViewModel();

            //if (!string.IsNullOrEmpty(cipher) && !string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(nonce))
            //{
            //    // var key = _configurationProvider.ReadConfigurationObject(ConfigurationConstants.AesEncryptionKey);
            //    var key = "sadasda";
            //    byte[] keyBytes = AESCypher.ToBytes(key);
            //    var result = AESCypher.Decrypt(cipher.Replace(' ', '+'), nonce.Replace(' ', '+'), tag.Replace(' ', '+'), keyBytes);
            //    var ticketId = Guid.Parse(result);
            //    var ticket = _ticketManager.GetTicket(ticketId);
            //    if (ticket == null)
            //    {
            //        return RedirectToAction("Index", "Error", new ErrorViewModel()
            //        {
            //            ErrorText = "Link already used.",
            //            StatusCode = "500"
            //        });
            //    }

            //    if (ticket.ExpirationDate < DateTime.UtcNow)
            //        return RedirectToAction("Index", "Error", new ErrorViewModel()
            //        {
            //            ErrorText = "Link expired.",
            //            StatusCode = "500"
            //        });

            //    model.ProductKey = ticket.ProductKey;
            //    model.Role = ticket.Role;
            //    model.TicketId = ticket.Id.ToString();
            //}

            return View(model);
        }

        [AllowAnonymous]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> Registrate([FromForm] RegistrationViewModel model)
        {
            RegistrationValidator validator = new(_userManager);
            var results = validator.Validate(model);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return View("Registration", model);
            }

            List<(Guid, ProductRoleEnum)> products = null;
            if (!string.IsNullOrEmpty(model.ProductKey) && !string.IsNullOrEmpty(model.Role))
            {
                products = new List<(Guid, ProductRoleEnum)>()
                {
                    (model.ProductKey.ToGuid(), (ProductRoleEnum)int.Parse(model.Role))
                };
            }

            await _userManager.AddUser(model.Username, HashComputer.ComputePasswordHash(model.Password), false, products);
            await Authenticate(model.Username, true);

            //if (!string.IsNullOrEmpty(model.TicketId))
            //    _ticketManager.RemoveTicket(Guid.Parse(model.TicketId));

            return Redirect("/Home");
        }

        #endregion

        #region Users

        [AuthorizeIsAdmin]
        public IActionResult Users()
        {
            // TODO: use ViewComponent and remove using TempData for passing products
            TempData[TextConstants.TempDataProductsText] =
                _treeViewModel.Nodes.Values.ToDictionary(product => product.Id, product => product.Name);

            var users = _userManager.GetUsers().OrderBy(x => x.Name);
            return View(users.Select(x => new UserViewModel(x)).ToList());
        }

        [HttpPost]
        public Task RemoveUser(Guid id) => _userManager.TryRemove(new(id, CurrentInitiator));

        [HttpPost]
        public async Task CreateUser([FromBody] UserViewModel model)
        {
            NewUserValidator validator = new(_userManager);
            var results = validator.Validate(model);

            if (!results.IsValid)
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
            else
                await _userManager.AddUser(model.Username.Trim(), HashComputer.ComputePasswordHash(model.Password), model.IsAdmin);
        }

        [HttpPost]
        public Task UpdateUser([FromBody] UserViewModel userViewModel)
        {
            var validator = new EditUserValidator();
            var results = validator.Validate(userViewModel);

            if (!results.IsValid)
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
            else if (_userManager.TryGetIdByName(userViewModel.Username, out var userId))
            {
                var updateUser = new UserUpdate
                {
                    Id = userId,
                    IsAdmin = userViewModel.IsAdmin,
                };

                if (!string.IsNullOrEmpty(userViewModel.Password))
                    updateUser.Password = HashComputer.ComputePasswordHash(userViewModel.Password);

                return _userManager.TryUpdate(updateUser);
            }

            return Task.CompletedTask;
        }

        #endregion

        public async Task<IActionResult> Logout()
        {
            TempData.Remove(TextConstants.TempDataErrorText);

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/Home");
        }


        private Task Authenticate(string login, bool keepLoggedIn)
        {
            AuthenticationProperties properties = new() { IsPersistent = keepLoggedIn };
            return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, HsmCookiePrincipal.Create(login), properties);
        }
    }
}
