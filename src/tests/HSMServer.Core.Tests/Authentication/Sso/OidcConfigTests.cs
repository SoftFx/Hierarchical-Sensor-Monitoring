using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.Sso
{
    // The Oidc settings section (#1782): off by default, an enabled but incomplete section
    // switches SSO off instead of stopping the server, and the client secret never reaches
    // the settings file that ServerConfig rewrites on every start.
    public class OidcConfigTests
    {
        private const string Secret = "s3cr3t-value-for-tests";


        private static OidcConfig Complete() => new()
        {
            Enabled = true,
            Authority = "https://sso.example.test/application/o/hsm/",
            ClientId = "hsm",
            ClientSecret = Secret,
        };


        [Fact]
        public void Default_IsOff()
        {
            var config = new OidcConfig();

            Assert.Empty(config.Validate());
            Assert.False(config.Enabled);
            Assert.False(config.IsActive);
        }

        [Fact]
        public void Default_Values_MatchTheDocumentedOnes()
        {
            var config = new OidcConfig();

            Assert.Equal(["openid", "profile"], config.ScopeList);
            Assert.Equal("preferred_username", config.UsernameClaim);
            Assert.Equal("groups", config.GroupsClaim);
            Assert.Equal(string.Empty, config.AdminGroup);
            Assert.True(config.AutoCreateUsers);
            Assert.Equal(TimeSpan.FromHours(12), config.SessionLifetime);
        }

        [Fact]
        public void Complete_IsActive()
        {
            var config = Complete();

            Assert.Empty(config.Validate());
            Assert.True(config.IsActive);
        }

        [Fact]
        public void MissingSecret_SwitchesSsoOff_AndNamesTheEnvironmentVariable()
        {
            var config = Complete();
            config.ClientSecret = null;

            var error = Assert.Single(config.Validate());

            Assert.Contains("Oidc__ClientSecret", error);
            Assert.False(config.IsActive);
        }

        [Theory]
        [InlineData("")]
        [InlineData("sso.example.test")]
        [InlineData("http://sso.example.test/application/o/hsm/")]
        public void NonHttpsAuthority_SwitchesSsoOff(string authority)
        {
            var config = Complete();
            config.Authority = authority;

            Assert.Contains(config.Validate(), error => error.Contains("Oidc.Authority"));
            Assert.False(config.IsActive);
        }

        [Fact]
        public void MissingClientId_SwitchesSsoOff()
        {
            var config = Complete();
            config.ClientId = " ";

            Assert.Contains(config.Validate(), error => error.Contains("Oidc.ClientId"));
            Assert.False(config.IsActive);
        }

        [Fact]
        public void ScopesWithoutOpenId_SwitchSsoOff()
        {
            var config = Complete();
            config.Scopes = "profile email";

            Assert.Contains(config.Validate(), error => error.Contains("openid"));
        }

        [Fact]
        public void AdminGroupWithoutGroupsClaim_SwitchesSsoOff()
        {
            var config = Complete();
            config.AdminGroup = "hsm-admins";
            config.GroupsClaim = "";

            Assert.Contains(config.Validate(), error => error.Contains("Oidc.GroupsClaim"));
        }

        [Theory]
        [InlineData(0, 0, 1)]       // 1 second
        [InlineData(31, 0, 0)]      // 31 days
        public void SessionLifetimeOutOfRange_SwitchesSsoOff(int days, int hours, int seconds)
        {
            var config = Complete();
            config.SessionLifetime = new TimeSpan(days, hours, 0, seconds);

            Assert.Contains(config.Validate(), error => error.Contains("Oidc.SessionLifetime"));
        }

        [Fact]
        public void ScopeList_SplitsOnSpaces()
        {
            var config = Complete();
            config.Scopes = "  openid   profile email ";

            Assert.Equal(["openid", "profile", "email"], config.ScopeList);
        }


        [Fact]
        public void ClientSecret_IsNeverWrittenToTheSettingsFile()
        {
            var json = JsonSerializer.Serialize(Complete());

            Assert.DoesNotContain(Secret, json);
            Assert.DoesNotContain(nameof(OidcConfig.ClientSecret), json);
        }

        [Fact]
        public void Resave_KeepsTheSectionStable()
        {
            // ServerConfig binds the section, then writes it back on every start: two rounds
            // must give the same values (an initialized array would grow on each one).
            var first = Bind(JsonSerializer.Serialize(new { Oidc = Complete() }));
            var second = Bind(JsonSerializer.Serialize(new { Oidc = first }));

            Assert.Equal(first.Scopes, second.Scopes);
            Assert.Equal(["openid", "profile"], second.ScopeList);
            Assert.Equal(first.SessionLifetime, second.SessionLifetime);
            Assert.Equal(first.Authority, second.Authority);
        }

        [Fact]
        public void Bind_ReadsEveryWellFormedValue()
        {
            var config = Bind("""
                { "Oidc": { "Enabled": "true", "Authority": "https://sso.example.test/", "ClientId": "hsm",
                            "Scopes": "openid profile email", "UsernameClaim": "email", "GroupsClaim": "roles",
                            "AdminGroup": "hsm-admins", "AutoCreateUsers": false, "DisplayName": "Authentik",
                            "SessionLifetime": "08:30:00", "ClientSecret": "ignored-here" } }
                """);

            Assert.True(config.Enabled);
            Assert.Equal("https://sso.example.test/", config.Authority);
            Assert.Equal("hsm", config.ClientId);
            Assert.Equal("openid profile email", config.Scopes);
            Assert.Equal("email", config.UsernameClaim);
            Assert.Equal("roles", config.GroupsClaim);
            Assert.Equal("hsm-admins", config.AdminGroup);
            Assert.False(config.AutoCreateUsers);
            Assert.Equal("Authentik", config.DisplayName);
            Assert.Equal(new TimeSpan(8, 30, 0), config.SessionLifetime);
            Assert.Null(config.ClientSecret);         // never from the settings file
        }

        [Fact]
        public void Bind_MalformedValue_SwitchesSsoOff_InsteadOfThrowing()
        {
            // Get<OidcConfig>() throws on this, and ServerConfig runs before the server starts.
            var config = Bind("""{ "Oidc": { "Enabled": true, "Authority": "https://sso.example.test/", "ClientId": "hsm", "SessionLifetime": "12h" } }""");
            config.ClientSecret = Secret;

            var error = Assert.Single(config.Validate());

            Assert.Contains("Oidc.SessionLifetime = '12h'", error);
            Assert.False(config.IsActive);
            // The valid values survive, so the resave does not erase the operator's settings.
            Assert.Equal("https://sso.example.test/", config.Authority);
            Assert.Equal(TimeSpan.FromHours(12), config.SessionLifetime);
        }

        [Fact]
        public void Bind_MalformedEnabled_IsReported()
        {
            var config = Bind("""{ "Oidc": { "Enabled": "yes" } }""");

            Assert.Contains(config.Validate(), error => error.Contains("Oidc.Enabled = 'yes'"));
            Assert.False(config.IsActive);
        }

        [Fact]
        public void Bind_ListWhereOneValueBelongs_IsReported()
        {
            // Without the error the default would replace the operator's list on the resave,
            // silently dropping e.g. a "groups" scope.
            var config = Bind("""{ "Oidc": { "Enabled": true, "Scopes": ["openid", "profile", "groups"] } }""");

            Assert.Contains(config.Validate(), error => error.Contains("Oidc.Scopes must be a single value"));
            Assert.False(config.IsActive);
        }

        [Fact]
        public void Bind_MissingSection_IsTheDefault()
        {
            var config = Bind("{}");

            Assert.Empty(config.Validate());
            Assert.False(config.Enabled);
        }

        [Fact]
        public void ReadClientSecret_TakesTheEnvironmentKey()
        {
            var environment = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { ["Oidc:ClientSecret"] = $"  {Secret}  " })
                .Build();

            Assert.Equal(Secret, OidcConfig.ReadClientSecret(environment));
        }

        [Fact]
        public void ReadClientSecret_BlankIsAbsent()
        {
            var environment = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { ["Oidc:ClientSecret"] = " " })
                .Build();

            Assert.Null(OidcConfig.ReadClientSecret(environment));
        }

        [Fact]
        public void SecretInTheSettingsFile_IsDetected()
        {
            var withSecret = BuildFromFile($$"""{ "Oidc": { "Enabled": true, "ClientSecret": "{{Secret}}" } }""");
            var withoutSecret = BuildFromFile("""{ "Oidc": { "Enabled": true } }""");

            Assert.True(OidcConfig.HasClientSecretInSettingsFile(withSecret));
            Assert.False(OidcConfig.HasClientSecretInSettingsFile(withoutSecret));
        }

        [Fact]
        public void SecretFromANonFileProvider_IsNotReportedAsAFileValue()
        {
            var environmentOnly = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { ["Oidc:ClientSecret"] = Secret })
                .Build();

            Assert.False(OidcConfig.HasClientSecretInSettingsFile(environmentOnly));
        }


        private static OidcConfig Bind(string json) =>
            OidcConfig.Bind(new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build()
                                                      .GetSection(OidcConfig.SectionName));

        private static IConfigurationRoot BuildFromFile(string json)
        {
            var directory = Path.Combine(Path.GetTempPath(), "hsm-oidc-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                File.WriteAllText(Path.Combine(directory, "appsettings.json"), json);

                return new ConfigurationBuilder().SetBasePath(directory).AddJsonFile("appsettings.json").Build();
            }
            finally
            {
                // The JSON provider has read the file during Build.
                Directory.Delete(directory, true);
            }
        }
    }
}
