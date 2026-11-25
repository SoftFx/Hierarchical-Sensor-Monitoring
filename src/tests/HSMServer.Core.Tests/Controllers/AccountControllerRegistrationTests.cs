using HSMServer.Authentication;
using HSMServer.Authentication.Sso;
using HSMServer.Controllers;
using HSMServer.Model.Authentication;
using HSMServer.Model.ViewModel;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Self-registration (the anonymous form, available while SSO is off) creates a user that is not an
    // admin and holds no role. Roles are granted afterwards by a Manager or an admin.
    public class AccountControllerRegistrationTests
    {
        private static readonly string[] FormFields =
            [nameof(RegistrationViewModel.Username), nameof(RegistrationViewModel.Password), nameof(RegistrationViewModel.SecondPassword)];


        [Fact]
        public void TheForm_BindsTheNameAndThePasswords_AndNothingElse()
        {
            var bound = typeof(RegistrationViewModel)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .Select(p => p.Name);

            Assert.Equal(FormFields.Order(), bound.Order());
        }

        [Fact]
        public async Task Registrate_CreatesAUserWithNoRole_WhateverElseThePostCarries()
        {
            var users = new Mock<IUserManager>();
            var controller = Controller(users, out var signIn);

            users.Setup(u => u.AddUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<(Guid, ProductRoleEnum)>>()))
                .ReturnsAsync(true);

            // The request names a product and a role next to the form's own fields. The action receives
            // what the model binder gives it: the view model, built from the posted form.
            var model = await Bind(controller, new Dictionary<string, string>
            {
                ["Username"] = "newcomer",
                ["Password"] = "password1",
                ["SecondPassword"] = "password1",
                ["ProductKey"] = Guid.NewGuid().ToString(),
                ["Role"] = ((int)ProductRoleEnum.ProductManager).ToString(),
                ["IsAdmin"] = "true",
            });

            Assert.IsType<RedirectResult>(await controller.Registrate(model));

            var call = Assert.Single(users.Invocations, i => i.Method.Name == nameof(IUserManager.AddUser));

            Assert.Equal("newcomer", call.Arguments[0]);
            Assert.Equal(false, call.Arguments[2]);

            var roles = (List<(Guid, ProductRoleEnum)>)call.Arguments[3];

            Assert.True(roles is null || roles.Count == 0);
            Assert.Equal(["newcomer"], SignedIn(signIn));
        }

        [Fact]
        public async Task Registrate_WhenTheUserWasNotCreated_SignsNobodyIn()
        {
            // The name was free when the form was validated and taken when the user was added: the
            // request must not end signed in under a name that now belongs to another user.
            var users = new Mock<IUserManager>();
            var controller = Controller(users, out var signIn);

            users.Setup(u => u.AddUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<List<(Guid, ProductRoleEnum)>>()))
                .ReturnsAsync(false);

            var result = await controller.Registrate(new RegistrationViewModel { Username = "taken", Password = "password1", SecondPassword = "password1" });

            Assert.Equal("Registration", Assert.IsType<ViewResult>(result).ViewName);
            Assert.Empty(SignedIn(signIn));
        }


        // The login names the request was signed in under.
        private static List<string> SignedIn(Mock<IAuthenticationService> signIn) =>
            signIn.Invocations
                .Where(i => i.Method.Name == nameof(IAuthenticationService.SignInAsync))
                .Select(i => ((System.Security.Claims.ClaimsPrincipal)i.Arguments[2]).Identity?.Name)
                .ToList();

        private static AccountController Controller(Mock<IUserManager> users, out Mock<IAuthenticationService> signIn)
        {
            var services = new ServiceCollection();

            signIn = new Mock<IAuthenticationService>();

            services.AddSingleton(signIn.Object);
            services.AddSingleton(Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionaryFactory>(
                f => f.GetTempData(It.IsAny<HttpContext>()) == Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataDictionary>()));
            services.AddLogging();
            services.AddControllersWithViews();

            users.Setup(u => u.GetUsers(It.IsAny<Func<User, bool>>())).Returns([]);

            return new AccountController(users.Object, null, new OidcConfig(), NullLogger<AccountController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() } },
            };
        }

        // Runs the MVC model binder over a posted form, as the framework does before the action.
        private static async Task<RegistrationViewModel> Bind(AccountController controller, Dictionary<string, string> form)
        {
            controller.ControllerContext.HttpContext.Request.ContentType = "application/x-www-form-urlencoded";
            controller.ControllerContext.HttpContext.Request.Form =
                new FormCollection(form.ToDictionary(f => f.Key, f => new Microsoft.Extensions.Primitives.StringValues(f.Value)));
            controller.ControllerContext.ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor();
            controller.ControllerContext.RouteData = new Microsoft.AspNetCore.Routing.RouteData();

            var model = new RegistrationViewModel();

            Assert.True(await controller.TryUpdateModelAsync(model, string.Empty, new FormValueProvider(BindingSource.Form,
                controller.ControllerContext.HttpContext.Request.Form, System.Globalization.CultureInfo.InvariantCulture)));

            return model;
        }
    }
}
