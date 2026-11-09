using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HSMServer.Core.Tests.Authentication
{
    // Key-ring persistence contract (#1633): the Data Protection ring backs the auth
    // cookie and the antiforgery tokens, and when it lived inside the container
    // filesystem every container recreate (image upgrade, compose change, host
    // rebuild) silently invalidated all sessions and every form opened before the
    // restart. The registration under test (AddHsmDataProtection, wired from
    // Program.cs into the mounted Config volume) must make the ring a property of
    // the DIRECTORY, not of the process: two ServiceProviders over the same
    // directory stand in for two container lives, and the cookie payload is
    // produced by the framework's own TicketDataFormat (CookieAuthenticationOptions
    // post-configure) — exactly the bytes the cookie handler writes.
    public class DataProtectionKeyRingTests : IDisposable
    {
        private readonly List<string> _tempDirectories = [NewTempDirectory()];


        [Fact]
        public void CookieTicket_FromOneProvider_ValidatesWithAnotherOverTheSameKeyDirectory()
        {
            var keysDirectory = _tempDirectories[0];
            string cookie;

            using (var firstContainerLive = BuildProvider(keysDirectory))
            {
                cookie = ProtectCookie(firstContainerLive, "admin");

                // The key materialized on the "volume", not in process memory —
                // this is what lets the recreated provider below validate at all.
                Assert.NotEmpty(Directory.GetFiles(keysDirectory, "*.xml"));
            }

            using var recreatedContainerLive = BuildProvider(keysDirectory);

            var ticket = UnprotectCookie(recreatedContainerLive, cookie);

            Assert.Equal("admin", ticket?.Principal.FindFirst(ClaimTypes.Name)?.Value);
        }

        // Both antiforgery halves of the token pair are issued by the framework
        // against the shared key ring; an explicit cookie name keeps the transfer
        // below independent of the framework's default-name derivation.
        private const string AntiforgeryCookieName = "hsm-test-antiforgery";


        [Fact]
        public async Task AntiforgeryToken_FromOneProvider_ValidatesWithAnotherOverTheSameKeyDirectory()
        {
            var keysDirectory = _tempDirectories[0];
            AntiforgeryTokenSet tokens;

            using (var firstContainerLive = BuildProvider(keysDirectory))
            {
                // A form opened in the previous container life: GetTokens protects
                // the token pair with the then-current key ring.
                tokens = firstContainerLive.GetRequiredService<IAntiforgery>()
                                           .GetTokens(new DefaultHttpContext());
            }

            using var recreatedContainerLive = BuildProvider(keysDirectory);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[tokens.HeaderName] = tokens.RequestToken;
            // Request.Cookies parses from the Cookie header (RequestCookieCollection
            // itself is internal in .NET 8); the token is base64url — no separators.
            httpContext.Request.Headers["Cookie"] = $"{AntiforgeryCookieName}={tokens.CookieToken}";

            // The submit after the restart: the exact validation whose failure
            // produced the #1633 error log ("An exception was thrown while
            // deserializing the token" -> "The key {...} was not found in the key
            // ring"). Succeeding without an exception is the assertion.
            await recreatedContainerLive.GetRequiredService<IAntiforgery>()
                                        .ValidateRequestAsync(httpContext);
        }

        [Fact]
        public void CookieTicket_FromOneProvider_FailsOverADifferentKeyDirectory()
        {
            var keysDirectory = _tempDirectories[0];
            var otherKeysDirectory = NewTempDirectory();
            _tempDirectories.Add(otherKeysDirectory);

            string cookie;
            using (var provider = BuildProvider(keysDirectory))
            {
                cookie = ProtectCookie(provider, "admin");
            }

            using var unrelatedRing = BuildProvider(otherKeysDirectory);

            // The #1633 symptom at the API the cookie handler consumes:
            // SecureDataFormat swallows the underlying CryptographicException
            // ("The key {...} was not found in the key ring" — the error-log line
            // from the issue) into a null ticket, i.e. an unauthenticated request
            // and a forced re-login. No cross-directory validation may ever happen.
            Assert.Null(UnprotectCookie(unrelatedRing, cookie));
        }

        private static ServiceProvider BuildProvider(string keysDirectory)
        {
            var services = new ServiceCollection();

            // AddLogging: the DataProtection key manager takes ILoggerFactory; a
            // WebApplicationBuilder host always has logging, a bare container does not.
            services.AddLogging();

            services.AddHsmDataProtection(keysDirectory);
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);
            services.AddAntiforgery(options => options.Cookie.Name = AntiforgeryCookieName);

            return services.BuildServiceProvider();
        }

        private static string ProtectCookie(ServiceProvider provider, string userName)
        {
            var identity = new ClaimsIdentity(
                authenticationType: CookieAuthenticationDefaults.AuthenticationScheme,
                claims: [new Claim(ClaimTypes.Name, userName)]);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                new AuthenticationProperties(),
                CookieAuthenticationDefaults.AuthenticationScheme);

            return TicketFormat(provider).Protect(ticket);
        }

        private static AuthenticationTicket UnprotectCookie(ServiceProvider provider, string cookie) =>
            TicketFormat(provider).Unprotect(cookie);

        private static ISecureDataFormat<AuthenticationTicket> TicketFormat(ServiceProvider provider) =>
            provider.GetRequiredService<IOptions<CookieAuthenticationOptions>>().Value.TicketDataFormat;

        private static string NewTempDirectory() =>
            Path.Combine(Path.GetTempPath(), "hsm-dataprotection-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            foreach (var directory in _tempDirectories)
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
