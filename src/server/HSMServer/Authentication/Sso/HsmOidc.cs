using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System;
using System.Security.Claims;
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


        public static string LoginErrorPath(SsoSignInError error) => $"{LoginPath}?{ErrorQueryKey}={error}";

        public static string Describe(SsoSignInError error) => error switch
        {
            SsoSignInError.MissingUsername => "SSO sign-in failed: the identity provider did not send a username.",
            SsoSignInError.UnknownUser => "SSO sign-in failed: there is no HSM user with your username. Ask an administrator to create it.",
            SsoSignInError.CreateFailed => "SSO sign-in failed: the HSM user could not be created.",
            SsoSignInError.ProviderFailure => "SSO sign-in failed: the identity provider did not complete the sign-in.",
            SsoSignInError.ProviderUnavailable => "SSO sign-in is unavailable: the identity provider cannot be reached. Sign in with a password.",
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
            var result = await signIn.ResolveUser(context.Principal);

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
            context.Properties.RedirectUri = HsmOidcDefaults.AfterSignInPath;
        }

        public override Task RemoteFailure(RemoteFailureContext context)
        {
            logger.LogWarning("SSO sign-in failed at the provider step: {Error}", context.Failure?.Message);

            context.HandleResponse();
            context.Response.Redirect(HsmOidcDefaults.LoginErrorPath(SsoSignInError.ProviderFailure));

            return Task.CompletedTask;
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
                options.ClaimActions.MapJsonKey(config.GroupsClaim, config.GroupsClaim);

            options.EventsType = typeof(HsmOidcEvents);
        }
    }
}
