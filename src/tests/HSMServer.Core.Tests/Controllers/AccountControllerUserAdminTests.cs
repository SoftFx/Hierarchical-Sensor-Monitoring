using HSMServer.Attributes;
using HSMServer.Controllers;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // #1782 review: the user administration posts were guarded only by the controller's
    // [Authorize], so any signed-in user — with SSO on, every auto-created one — could make
    // itself admin, set another user's password or delete users. They are admin-only now,
    // like the Users page that calls them.
    public class AccountControllerUserAdminTests
    {
        public static IEnumerable<object[]> UserAdministrationActions() =>
            new[] { nameof(AccountController.CreateUser), nameof(AccountController.UpdateUser), nameof(AccountController.RemoveUser) }
                .Select(name => new object[] { name });


        [Theory]
        [MemberData(nameof(UserAdministrationActions))]
        public void Action_IsAdminOnly(string action)
        {
            var method = typeof(AccountController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance);

            Assert.NotNull(method);
            Assert.NotNull(method.GetCustomAttribute<AuthorizeIsAdminAttribute>());
        }

        [Theory]
        [MemberData(nameof(UserAdministrationActions))]
        public void Action_RejectsANonAdmin(string action)
        {
            var context = Authorize(action, new User("ivanov") { IsAdmin = false });

            Assert.IsType<UnauthorizedResult>(context.Result);
        }

        [Theory]
        [MemberData(nameof(UserAdministrationActions))]
        public void Action_LetsAnAdminThrough(string action)
        {
            var context = Authorize(action, new User("admin") { IsAdmin = true });

            Assert.Null(context.Result);
        }


        private static AuthorizationFilterContext Authorize(string action, User user)
        {
            var filter = typeof(AccountController).GetMethod(action)!.GetCustomAttribute<AuthorizeIsAdminAttribute>();
            Assert.NotNull(filter);

            var actionContext = new ActionContext(new DefaultHttpContext { User = user }, new RouteData(), new ActionDescriptor());
            var context = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata> { filter });

            filter.OnAuthorization(context);

            return context;
        }
    }
}
