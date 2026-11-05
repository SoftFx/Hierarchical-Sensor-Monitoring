using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

        [Fact]
        public void CookieTicket_FromOneProvider_FailsOverADifferentKeyDirectory()
        {
            var otherKeysDirectory = NewTempDirectory();
            _tempDirectories.Add(otherKeysDirectory);

            string cookie;
            using (var provider = BuildProvider(_tempDirectories[0]))
            {
                cookie = ProtectCookie(provider, "admin");
            }

            using var stranger = BuildProvider(otherKeysDirectory);

            // The #1633 symptom at the API the cookie handler consumes:
            // SecureDataFormat swallows the underlying CryptographicException
            // ("The key {...} was not found in the key ring" — the error-log line
            // from the issue) into a null ticket, i.e. an unauthenticated request
            // and a forced re-login. No cross-directory validation may ever happen.
            Assert.Null(UnprotectCookie(stranger, cookie));
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
