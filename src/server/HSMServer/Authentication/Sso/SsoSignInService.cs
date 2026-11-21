using HSMServer.Model.Authentication;
using HSMServer.Model.Validators;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HSMServer.Authentication.Sso
{
    public enum SsoSignInError
    {
        None,
        // The provider's token had no value in Oidc.UsernameClaim.
        MissingUsername,
        // The username is the built-in "default" admin, which SSO never signs in.
        ReservedUsername,
        // The username breaks the rules every HSM login follows (length, characters).
        InvalidUsername,
        // No HSM user has that login and Oidc.AutoCreateUsers is off.
        UnknownUser,
        // Creating the user failed (storage refused it).
        CreateFailed,
        // The provider answered with an error, or the callback could not be validated.
        ProviderFailure,
        // The sign-in could not start: the provider's discovery document is unreachable.
        ProviderUnavailable,
    }

    public sealed record SsoSignInResult(string Login, SsoSignInError Error)
    {
        public bool Succeeded => Error == SsoSignInError.None;

        public static SsoSignInResult Success(string login) => new(login, SsoSignInError.None);

        public static SsoSignInResult Fail(SsoSignInError error) => new(null, error);
    }

    public interface ISsoSignInService
    {
        // Maps the identity the provider vouched for onto an HSM user: finds it by login,
        // creates it when allowed, and syncs IsAdmin with Oidc.AdminGroup when that is set.
        Task<SsoSignInResult> ResolveUser(ClaimsPrincipal external);
    }

    // The business rules of an SSO sign-in (#1782), outside the controller and the OIDC
    // handler so they can be tested without a provider.
    public sealed class SsoSignInService(IUserManager userManager, OidcConfig config, ILogger<SsoSignInService> logger) : ISsoSignInService
    {
        // The rules of the user forms (BaseUserValidator), applied to the whole login.
        private const int MaxUsernameLength = BaseUserValidator<object>.MaxUsernameLength;
        private const string AllowedCharacters = "a-z A-Z 0-9 . _ @ + -";
        private static readonly Regex UsernamePattern = new(@"^[a-zA-Z0-9._@+-]+$", RegexOptions.Compiled);

        public async Task<SsoSignInResult> ResolveUser(ClaimsPrincipal external)
        {
            var login = external?.FindFirst(config.UsernameClaim)?.Value?.Trim();

            if (string.IsNullOrEmpty(login))
            {
                logger.LogWarning("SSO sign-in rejected: the provider sent no '{Claim}' claim.", config.UsernameClaim);
                return SsoSignInResult.Fail(SsoSignInError.MissingUsername);
            }

            // The built-in admin of a fresh database has the password "default": a provider
            // account that happens to be named so must not inherit it.
            if (login == UserManager.DefaultUserUsername)
            {
                logger.LogWarning("SSO sign-in rejected: '{Login}' is the built-in HSM admin and can sign in only with its password.", login);
                return SsoSignInResult.Fail(SsoSignInError.ReservedUsername);
            }

            if (login.Length > MaxUsernameLength || !UsernamePattern.IsMatch(login))
            {
                logger.LogWarning("SSO sign-in rejected: '{Login}' is not a valid HSM login (at most {Max} characters of {Allowed}).",
                                  login, MaxUsernameLength, AllowedCharacters);
                return SsoSignInResult.Fail(SsoSignInError.InvalidUsername);
            }

            var isAdmin = AdminFromGroups(external, login);

            var user = userManager[login];

            if (user is null)
            {
                if (!config.AutoCreateUsers)
                {
                    logger.LogWarning("SSO sign-in rejected for '{Login}': no such HSM user and Oidc.AutoCreateUsers is off.", login);
                    return SsoSignInResult.Fail(SsoSignInError.UnknownUser);
                }

                // No password: TryAuthenticate never accepts an empty hash, so this user can
                // sign in only through the provider until an admin sets one.
                var created = new User(login) { IsAdmin = isAdmin ?? false };

                if (await userManager.TryAdd(created))
                    logger.LogInformation("SSO sign-in created HSM user '{Login}' (admin: {IsAdmin}).", login, created.IsAdmin);
                else if (userManager[login] is null)
                {
                    logger.LogError("SSO sign-in could not create HSM user '{Login}'.", login);
                    return SsoSignInResult.Fail(SsoSignInError.CreateFailed);
                }
                else
                    // A parallel sign-in of the same person created it first: sync that one.
                    await SyncAdmin(userManager[login], isAdmin);

                return SsoSignInResult.Success(login);
            }

            await SyncAdmin(user, isAdmin);

            return SsoSignInResult.Success(user.Name);
        }


        private async Task SyncAdmin(User user, bool? isAdmin)
        {
            if (isAdmin is not { } admin || user.IsAdmin == admin)
                return;

            if (await userManager.TryUpdate(new UserUpdate { Id = user.Id, IsAdmin = admin }))
                logger.LogWarning("SSO sign-in {Change} admin rights of '{Login}' (group '{Group}').",
                                  admin ? "granted" : "revoked", user.Name, config.AdminGroup);
            else
                logger.LogError("SSO sign-in could not {Change} admin rights of '{Login}'.", admin ? "grant" : "revoke", user.Name);
        }

        // Null = leave IsAdmin as stored: AdminGroup is not set, or the provider sent no groups
        // claim at all (scope not granted, mapping changed, userinfo failed). A missing claim
        // is not the same as "not a member" and must not revoke every admin one by one.
        // Group names compare case-insensitively: the provider owns them, and a case slip in
        // the HSM setting should not silently revoke every admin either.
        private bool? AdminFromGroups(ClaimsPrincipal external, string login)
        {
            if (string.IsNullOrWhiteSpace(config.AdminGroup))
                return null;

            var groups = external.FindAll(config.GroupsClaim).ToList();

            if (groups.Count == 0)
            {
                logger.LogWarning("SSO sign-in of '{Login}': the provider sent no '{Claim}' claim, admin rights are left unchanged.",
                                  login, config.GroupsClaim);
                return null;
            }

            var group = config.AdminGroup.Trim();

            return groups.Any(claim => string.Equals(claim.Value?.Trim(), group, StringComparison.OrdinalIgnoreCase));
        }
    }
}
