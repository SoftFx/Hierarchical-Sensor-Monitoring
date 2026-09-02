using System;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HSMServer.Authentication
{
    // Scheme-isolation wiring for the management API (initiative step 3), kept in one
    // place so Program.cs cannot register the pieces inconsistently:
    //   AddHsmApiTokenScheme    - the HsmApiToken scheme on top of the cookie default
    //   AddHsmApiTokenAuthorization - cookie-pinned DefaultPolicy + the management policy
    //
    // Invariants (tested in HsmApiTokenSchemeIsolationTests): cookie remains the default
    // authenticate/challenge scheme and the only scheme behind bare [Authorize]; the
    // management policy authenticates through HsmApiToken only and accepts exactly one
    // HsmApiToken identity.
    public static class HsmApiTokenServiceCollectionExtensions
    {
        public static AuthenticationBuilder AddHsmApiTokenScheme(this AuthenticationBuilder builder)
        {
            // No options, no events: the handler's contract is fixed, and it must never be
            // forwarded to or configured into a policy/default scheme by registration.
            return builder.AddScheme<AuthenticationSchemeOptions, HsmApiTokenHandler>(
                HsmApiTokenDefaults.AuthenticationScheme, _ => { });
        }


        public static IServiceCollection AddHsmApiTokenAuthorization(this IServiceCollection services)
        {
            services.AddSingleton<IAuthorizationHandler, HsmApiTokenOnlyAuthorizationHandler>();

            services.AddAuthorization(options =>
            {
                // Bare [Authorize] on legacy MVC/Razor is pinned to the cookie scheme
                // explicitly: an API-token identity can never satisfy a legacy
                // authorization, whatever the default scheme resolves to in the future.
                options.DefaultPolicy = new AuthorizationPolicyBuilder(
                        CookieAuthenticationDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .Build();

                // The management policy authenticates through the HsmApiToken scheme ONLY
                // (a cookie session without a bearer credential never produces a principal
                // here and challenges as a plain 401, never a login redirect), and then
                // requires the single-identity token principal shape.
                options.AddPolicy(HsmApiTokenDefaults.ManagementPolicy, policy => policy
                    .AddAuthenticationSchemes(HsmApiTokenDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .AddRequirements(new HsmApiTokenOnlyRequirement()));
            });

            return services;
        }
    }
}
