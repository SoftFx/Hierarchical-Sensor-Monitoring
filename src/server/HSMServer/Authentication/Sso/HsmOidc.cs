using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSMServer.Authentication.Sso
{
    public static class HsmOidcDefaults
    {
        public const string AuthenticationScheme = "Oidc";

        // The redirect URI registered at the provider is https://<server address>/signin-oidc.
        public const string CallbackPath = "/signin-oidc";

        public const string LoginPath = "/Account/Index";

        public const string ErrorQueryKey = "ssoError";

        public const string AfterSignInPath = "/Home";

        // Added to the provider principal when the userinfo response carries the groups key,
        // even as an empty list: "in no groups" must revoke AdminGroup admin, while a missing
        // key must not. A JSON array becomes one claim per element, so an empty list and a
        // missing key look the same without this marker. Never reaches the HSM cookie.
        public const string GroupsPresentClaim = "hsm:groups-present";


        public static string LoginErrorPath(SsoSignInError error) => $"{LoginPath}?{ErrorQueryKey}={error}";

        public static string Describe(SsoSignInError error) => error switch
        {
            SsoSignInError.MissingUsername => "SSO sign-in failed: the identity provider did not send a username.",
            SsoSignInError.InvalidUsername => "SSO sign-in failed: your username is not a valid HSM login (at most 64 characters: letters, digits and . _ @ + -).",
            SsoSignInError.ReservedUsername => "SSO sign-in failed: this username is reserved for the built-in administrator.",
            SsoSignInError.UnknownUser => "SSO sign-in failed: there is no HSM user with your username. Ask an administrator to create it.",
            SsoSignInError.CreateFailed => "SSO sign-in failed: the HSM user could not be created.",
            SsoSignInError.ProviderFailure => "SSO sign-in failed: the identity provider did not complete the sign-in.",
            SsoSignInError.ProviderUnavailable => "SSO sign-in could not start: the identity provider may be unreachable. Sign in with a password.",
            _ => null,
        };
    }

    // The principal of the HSM auth cookie: the login is its only claim, and every request
    // resolves it into the stored user (MyCookieAuthenticationEvents, UserProcessorMiddleware).
    // Password and SSO sign-ins issue the same shape.
    public static class HsmCookiePrincipal
    {
        public static ClaimsPrincipal Create(string login) =>
            new(new ClaimsIdentity([new Claim(ClaimsIdentity.DefaultNameClaimType, login)], "ApplicationCookie",
                                   ClaimsIdentity.DefaultNameClaimType, ClaimsIdentity.DefaultRoleClaimType));
    }

    // Turns the provider's ticket into the HSM cookie (#1782). Resolved from DI per request
    // (OpenIdConnectOptions.EventsType).
    public sealed class HsmOidcEvents(ISsoSignInService signIn, OidcConfig config, ILogger<HsmOidcEvents> logger, TimeProvider time) : OpenIdConnectEvents
    {
        public override async Task TicketReceived(TicketReceivedContext context)
        {
            SsoSignInResult result;

            try
            {
                result = await signIn.ResolveUser(context.Principal);
            }
            catch (Exception ex)
            {
                // A storage failure while creating or updating the user: back to the login
                // page, not to the error page (the remote handler does not catch here).
                logger.LogError(ex, "SSO sign-in failed while resolving the HSM user.");
                result = SsoSignInResult.Fail(SsoSignInError.CreateFailed);
            }

            if (!result.Succeeded)
            {
                context.HandleResponse();
                context.Response.Redirect(HsmOidcDefaults.LoginErrorPath(result.Error));
                return;
            }

            // Only the login travels on: the provider's claims and tokens are not kept in the
            // cookie. The lifetime is absolute (no sliding renewal), so a person removed at the
            // provider loses HSM access when it ends.
            var now = time.GetUtcNow();

            context.Principal = HsmCookiePrincipal.Create(result.Login);
            context.Properties ??= new AuthenticationProperties();
            context.Properties.IsPersistent = true;
            context.Properties.IssuedUtc = now;
            context.Properties.ExpiresUtc = now + config.SessionLifetime;
            context.Properties.AllowRefresh = false;
            context.ReturnUri = HsmOidcDefaults.AfterSignInPath;
        }

        public override Task RemoteFailure(RemoteFailureContext context)
        {
            logger.LogWarning("SSO sign-in failed at the provider step: {Error}", context.Failure?.Message);

            context.HandleResponse();
            context.Response.Redirect(HsmOidcDefaults.LoginErrorPath(SsoSignInError.ProviderFailure));

            return Task.CompletedTask;
        }
    }

    // Records that the userinfo JSON has the groups key (HsmOidcDefaults.GroupsPresentClaim).
    internal sealed class GroupsPresenceClaimAction(string groupsKey) : ClaimAction(HsmOidcDefaults.GroupsPresentClaim, ClaimValueTypes.Boolean)
    {
        public override void Run(JsonElement userData, ClaimsIdentity identity, string issuer)
        {
            if (userData.ValueKind == JsonValueKind.Object && userData.TryGetProperty(groupsKey, out _) &&
                !identity.HasClaim(c => c.Type == HsmOidcDefaults.GroupsPresentClaim))
                identity.AddClaim(new Claim(HsmOidcDefaults.GroupsPresentClaim, "true", ClaimValueTypes.Boolean, issuer));
        }
    }

    public static class HsmOidcServiceCollectionExtensions
    {
        // Registers the OIDC scheme only when the Oidc section is enabled and valid; otherwise
        // the server has the same schemes as before #1782. The config and the sign-in service
        // are registered either way: the login page asks the config whether to show the button.
        public static AuthenticationBuilder AddHsmOidc(this AuthenticationBuilder builder, OidcConfig config)
        {
            builder.Services.AddSingleton(config);
            builder.Services.AddSingleton<ISsoSignInService, SsoSignInService>();
            builder.Services.AddSingleton<HsmOidcEvents>();
            builder.Services.TryAddSingleton(TimeProvider.System);

            if (!config.IsActive)
                return builder;

            return builder.AddOpenIdConnect(HsmOidcDefaults.AuthenticationScheme, config.DisplayName,
                                            options => Configure(options, config));
        }

        internal static void Configure(OpenIdConnectOptions options, OidcConfig config)
        {
            options.Authority = config.Authority.Trim();
            options.ClientId = config.ClientId.Trim();
            options.ClientSecret = config.ClientSecret;

            // Authorization code flow with PKCE; the provider's tokens are not stored.
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.SaveTokens = false;
            options.RequireHttpsMetadata = true;

            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.CallbackPath = new PathString(HsmOidcDefaults.CallbackPath);

            // Logout is local only. The handler's front-channel endpoint (/signout-oidc by
            // default) would sign the HSM cookie out on any cross-site GET, because the HSM
            // principal carries no sid/iss for it to check.
            options.RemoteSignOutPath = PathString.Empty;

            options.Scope.Clear();
            foreach (var scope in config.ScopeList)
                options.Scope.Add(scope);

            // Claim names as the provider sends them (no WS-Federation renaming), so the
            // configured UsernameClaim/GroupsClaim match what the provider documents.
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = config.UsernameClaim;

            // Some providers put the profile claims only into the userinfo response: copy the
            // two HSM needs from there too.
            options.GetClaimsFromUserInfoEndpoint = true;
            options.ClaimActions.MapUniqueJsonKey(config.UsernameClaim, config.UsernameClaim);

            if (!string.IsNullOrWhiteSpace(config.GroupsClaim))
            {
                options.ClaimActions.MapJsonKey(config.GroupsClaim, config.GroupsClaim);
                options.ClaimActions.Add(new GroupsPresenceClaimAction(config.GroupsClaim));
            }

            options.EventsType = typeof(HsmOidcEvents);
        }
    }
}
