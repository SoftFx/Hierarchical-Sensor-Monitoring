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
                _logger.LogError(ex, "SSO sign-in could not start (provider {Authority}).", _oidc.Authority);

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
            if (_oidc.IsActive)
                return NotFound();

            return View(new RegistrationViewModel());
        }

        [AllowAnonymous]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> Registrate([FromForm] RegistrationViewModel model)
        {
            // With SSO on, anonymous self-registration is off (#1782): an outsider could
            // otherwise create the login of a future SSO user first, with a password of their
            // own, and inherit everything SSO later grants it (AdminGroup included).
            if (_oidc.IsActive)
                return NotFound();

            RegistrationValidator validator = new(_userManager);
            var results = validator.Validate(model);
            if (!results.IsValid)
            {
                TempData[TextConstants.TempDataErrorText] = ValidatorHelper.GetErrorString(results.Errors);
                return View("Registration", model);
            }

            // A self-registered user is not an admin and holds no role: the form carries a name and a
            // password, nothing that grants access. Roles are given afterwards by a Manager of the
            // product or folder, or by an admin.
            await _userManager.AddUser(model.Username, HashComputer.ComputePasswordHash(model.Password), isAdmin: false);
            await Authenticate(model.Username, true);

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

        // The user administration posts are admin-only like the Users page that calls them
        // (#1782 review): with only the controller's [Authorize], any signed-in user (and
        // with SSO on, every auto-created one) could make itself admin, set another user's
        // password or delete users.
        [HttpPost]
        [AuthorizeIsAdmin]
        public Task RemoveUser(Guid id) => _userManager.TryRemove(new(id, CurrentInitiator));

        [HttpPost]
        [AuthorizeIsAdmin]
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
        [AuthorizeIsAdmin]
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
