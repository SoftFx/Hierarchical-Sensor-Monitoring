using HSMServer.Authentication;
using HSMServer.Authentication.Sso;
using HSMServer.Model.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.Sso
{
    // How an identity the provider vouched for becomes an HSM user (#1782): the username
    // claim is the HSM login, an unknown login is created without password or roles when
    // allowed, and IsAdmin follows Oidc.AdminGroup only when that is set.
    public class SsoSignInServiceTests
    {
        private readonly Dictionary<string, User> _users = new();
        private readonly List<User> _added = new();
        private readonly List<UserUpdate> _updates = new();
        private readonly Mock<IUserManager> _userManager = new();
        private readonly OidcConfig _config = new() { Enabled = true };


        public SsoSignInServiceTests()
        {
            _userManager.Setup(m => m[It.IsAny<string>()])
                        .Returns((string name) => _users.TryGetValue(name, out var user) ? user : null);

            _userManager.Setup(m => m.TryAdd(It.IsAny<User>()))
                        .Returns((User user) =>
                        {
                            if (!_users.TryAdd(user.Name, user))
                                return Task.FromResult(false);

                            _added.Add(user);
                            return Task.FromResult(true);
                        });

            _userManager.Setup(m => m.TryUpdate(It.IsAny<UserUpdate>()))
                        .Returns((UserUpdate update) =>
                        {
                            _updates.Add(update);
                            _users.Values.Single(u => u.Id == update.Id).Update(update);
                            return Task.FromResult(true);
                        });
        }


        private SsoSignInService Service() => new(_userManager.Object, _config, NullLogger<SsoSignInService>.Instance);

        private User AddExisting(string name, bool isAdmin = false, string password = "hash")
        {
            var user = new User(name) { IsAdmin = isAdmin, Password = password };
            _users.Add(name, user);
            return user;
        }

        private static ClaimsPrincipal External(string username, params string[] groups)
        {
            var claims = new List<Claim> { new("sub", "a1b2c3") };

            if (username is not null)
                claims.Add(new Claim("preferred_username", username));

            claims.AddRange(groups.Select(group => new Claim("groups", group)));

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Oidc"));
        }


        [Fact]
        public async Task ExistingUser_SignsInUnderItsLogin()
        {
            AddExisting("ivanov");

            var result = await Service().ResolveUser(External("ivanov"));

            Assert.True(result.Succeeded);
            Assert.Equal("ivanov", result.Login);
            Assert.Empty(_added);
            Assert.Empty(_updates);
        }

        [Fact]
        public async Task UsernameIsTrimmed()
        {
            AddExisting("ivanov");

            var result = await Service().ResolveUser(External("  ivanov "));

            Assert.Equal("ivanov", result.Login);
        }

        [Fact]
        public async Task UsernameClaim_IsConfigurable()
        {
            AddExisting("ivanov@example.test");
            _config.UsernameClaim = "email";

            var external = new ClaimsPrincipal(new ClaimsIdentity([new Claim("email", "ivanov@example.test"),
                                                                   new Claim("preferred_username", "other")], "Oidc"));

            var result = await Service().ResolveUser(external);

            Assert.Equal("ivanov@example.test", result.Login);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task MissingUsername_IsRejected(string username)
        {
            var result = await Service().ResolveUser(External(username));

            Assert.False(result.Succeeded);
            Assert.Equal(SsoSignInError.MissingUsername, result.Error);
            Assert.Empty(_added);
        }

        [Fact]
        public async Task BuiltInDefaultAdmin_IsNeverSignedInBySso()
        {
            // A fresh database starts with admin "default" / password "default".
            AddExisting("default", isAdmin: true);
            _config.AdminGroup = "hsm-admins";

            var result = await Service().ResolveUser(External("default", "hsm-admins"));

            Assert.Equal(SsoSignInError.ReservedUsername, result.Error);
            Assert.Empty(_updates);
        }

        [Theory]
        [InlineData("Default")]
        [InlineData("DEFAULT")]
        public async Task BuiltInDefaultAdmin_ReservationIgnoresCase(string username)
        {
            var result = await Service().ResolveUser(External(username));

            Assert.Equal(SsoSignInError.ReservedUsername, result.Error);
            Assert.Empty(_added);
        }

        [Fact]
        public async Task BuiltInDefaultAdmin_IsNotCreatedEither()
        {
            var result = await Service().ResolveUser(External("default"));

            Assert.Equal(SsoSignInError.ReservedUsername, result.Error);
            Assert.Empty(_added);
        }

        [Fact]
        public async Task UnknownUser_IsCreatedWithoutPasswordRolesOrAdmin()
        {
            var result = await Service().ResolveUser(External("petrov", "hsm-admins"));

            Assert.True(result.Succeeded);
            Assert.Equal("petrov", result.Login);

            var created = Assert.Single(_added);
            Assert.Equal("petrov", created.Name);
            Assert.True(string.IsNullOrEmpty(created.Password));
            Assert.False(created.IsAdmin);              // AdminGroup is not set
            Assert.Empty(created.ProductsRoles);
            Assert.Empty(created.FoldersRoles);
        }

        [Fact]
        public async Task UnknownUser_WithAutoCreateOff_IsRejected()
        {
            _config.AutoCreateUsers = false;

            var result = await Service().ResolveUser(External("petrov"));

            Assert.Equal(SsoSignInError.UnknownUser, result.Error);
            Assert.Empty(_added);
        }

        [Fact]
        public async Task LoginsAreCaseSensitive_LikePasswordLogin()
        {
            AddExisting("Ivanov");
            _config.AutoCreateUsers = false;

            var result = await Service().ResolveUser(External("ivanov"));

            Assert.Equal(SsoSignInError.UnknownUser, result.Error);
        }

        [Fact]
        public async Task CreateRefusedByStorage_IsAFailure()
        {
            _userManager.Setup(m => m.TryAdd(It.IsAny<User>())).ReturnsAsync(false);

            var result = await Service().ResolveUser(External("petrov"));

            Assert.Equal(SsoSignInError.CreateFailed, result.Error);
        }

        [Fact]
        public async Task CreateLostToAParallelSignIn_UsesTheUserThatWon()
        {
            // The other sign-in adds the user between the lookup and TryAdd.
            var winner = new User("petrov");
            _userManager.Setup(m => m.TryAdd(It.IsAny<User>()))
                        .Returns(() => { _users.TryAdd("petrov", winner); return Task.FromResult(false); });

            var result = await Service().ResolveUser(External("petrov"));

            Assert.True(result.Succeeded);
            Assert.Equal("petrov", result.Login);
        }


        [Fact]
        public async Task AdminGroup_GrantsAdminToAMember()
        {
            var user = AddExisting("ivanov", password: null);     // an SSO-created user
            _config.AdminGroup = "hsm-admins";

            await Service().ResolveUser(External("ivanov", "logs-readers", "hsm-admins"));

            Assert.True(user.IsAdmin);
            Assert.Equal(true, Assert.Single(_updates).IsAdmin);
        }

        [Fact]
        public async Task AdminGroup_NeverGrantsAdminToAPasswordUser()
        {
            // The login may have been registered by someone else before SSO was on: granting
            // admin would hand it to whoever knows the password.
            var user = AddExisting("alice", password: "outsider-hash");
            _config.AdminGroup = "hsm-admins";

            var result = await Service().ResolveUser(External("alice", "hsm-admins"));

            Assert.True(result.Succeeded);
            Assert.False(user.IsAdmin);
            Assert.Empty(_updates);
        }

        [Fact]
        public async Task AdminGroup_StillRevokesAdminFromAPasswordUser()
        {
            var user = AddExisting("ivanov", isAdmin: true, password: "hash");
            _config.AdminGroup = "hsm-admins";

            await Service().ResolveUser(External("ivanov", "logs-readers"));

            Assert.False(user.IsAdmin);
        }

        [Fact]
        public async Task AdminGroup_RevokesAdminFromANonMember()
        {
            var user = AddExisting("ivanov", isAdmin: true);
            _config.AdminGroup = "hsm-admins";

            await Service().ResolveUser(External("ivanov", "logs-readers"));

            Assert.False(user.IsAdmin);
            Assert.Equal(false, Assert.Single(_updates).IsAdmin);
        }

        [Fact]
        public async Task AdminGroup_NoGroupsClaimAtAll_LeavesAdminUnchanged()
        {
            // A missing claim (scope not granted, mapping changed) is not "not a member":
            // revoking here would strip every admin one sign-in at a time.
            var user = AddExisting("ivanov", isAdmin: true);
            _config.AdminGroup = "hsm-admins";

            var result = await Service().ResolveUser(External("ivanov"));

            Assert.True(result.Succeeded);
            Assert.True(user.IsAdmin);
            Assert.Empty(_updates);
        }

        [Fact]
        public async Task AdminGroup_EmptyGroupsList_RevokesAdmin()
        {
            // "groups": [] — the key is there, the person is in no groups (marker set by the
            // claim action): that is "not a member", unlike a missing key.
            var user = AddExisting("ivanov", isAdmin: true);
            _config.AdminGroup = "hsm-admins";

            var external = new ClaimsPrincipal(new ClaimsIdentity([new Claim("preferred_username", "ivanov"),
                                                                   new Claim(HsmOidcDefaults.GroupsPresentClaim, "true")], "Oidc"));

            await Service().ResolveUser(external);

            Assert.False(user.IsAdmin);
            Assert.Equal(false, Assert.Single(_updates).IsAdmin);
        }

        [Fact]
        public async Task AdminGroup_NoGroupsClaim_CreatesANonAdmin()
        {
            _config.AdminGroup = "hsm-admins";

            await Service().ResolveUser(External("petrov"));

            Assert.False(Assert.Single(_added).IsAdmin);
        }

        [Theory]
        [InlineData("ivan ov")]
        [InlineData("ivan/ov")]
        [InlineData("иванов")]
        [InlineData("a234567890123456789012345678901234567890123456789012345678901234x")]   // 65 characters
        public async Task LoginOutsideTheUserFormRules_IsRejected(string username)
        {
            var result = await Service().ResolveUser(External(username));

            Assert.Equal(SsoSignInError.InvalidUsername, result.Error);
            Assert.Empty(_added);
        }

        [Theory]
        [InlineData("ivanov")]
        [InlineData("i.ivanov@example.test")]
        [InlineData("ivan_ov-2+x")]
        [InlineData("a23456789012345678901234567890123456789012345678901234567890123x")]    // 64 characters
        public async Task LoginWithinTheUserFormRules_IsAccepted(string username)
        {
            var result = await Service().ResolveUser(External(username));

            Assert.True(result.Succeeded);
        }

        [Fact]
        public async Task AdminGroup_ComparesNamesIgnoringCase()
        {
            var user = AddExisting("ivanov", password: null);
            _config.AdminGroup = "HSM-Admins";

            await Service().ResolveUser(External("ivanov", "hsm-admins"));

            Assert.True(user.IsAdmin);
        }

        [Fact]
        public async Task AdminGroup_UnchangedMembership_WritesNothing()
        {
            AddExisting("ivanov", isAdmin: true);
            _config.AdminGroup = "hsm-admins";

            await Service().ResolveUser(External("ivanov", "hsm-admins"));

            Assert.Empty(_updates);
        }

        [Fact]
        public async Task NoAdminGroup_LeavesHsmsOwnFlagAlone()
        {
            var user = AddExisting("admin", isAdmin: true);

            await Service().ResolveUser(External("admin"));

            Assert.True(user.IsAdmin);
            Assert.Empty(_updates);
        }

        [Fact]
        public async Task AdminGroup_AppliesToACreatedUser()
        {
            _config.AdminGroup = "hsm-admins";

            await Service().ResolveUser(External("petrov", "hsm-admins"));

            Assert.True(Assert.Single(_added).IsAdmin);
        }

        [Fact]
        public async Task AdminGroup_GroupsClaimIsConfigurable()
        {
            var user = AddExisting("ivanov", password: null);
            _config.AdminGroup = "hsm-admins";
            _config.GroupsClaim = "roles";

            var external = new ClaimsPrincipal(new ClaimsIdentity([new Claim("preferred_username", "ivanov"),
                                                                   new Claim("roles", "hsm-admins")], "Oidc"));

            await Service().ResolveUser(external);

            Assert.True(user.IsAdmin);
        }

        [Fact]
        public async Task AdminGroup_DoesNotTouchProductRoles()
        {
            var user = AddExisting("ivanov");
            var product = Guid.NewGuid();
            user.ProductsRoles.Add((product, ProductRoleEnum.ProductManager));
            _config.AdminGroup = "hsm-admins";

            await Service().ResolveUser(External("ivanov", "hsm-admins"));

            Assert.Equal([(product, ProductRoleEnum.ProductManager)], user.ProductsRoles);
        }
    }
}
