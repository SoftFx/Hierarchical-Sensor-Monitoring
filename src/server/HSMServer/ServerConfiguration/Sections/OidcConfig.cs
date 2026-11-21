using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace HSMServer.ServerConfiguration
{
    // Optional OpenID Connect sign-in for the web UI (#1782). Off by default: a server
    // without the section, or with Enabled = false, registers no OIDC scheme and its
    // login page is unchanged. Nothing here is specific to one identity provider.
    public sealed class OidcConfig
    {
        public const string SectionName = "Oidc";

        // The environment key of the client secret (Oidc__ClientSecret in a container).
        public const string ClientSecretKey = SectionName + ":" + nameof(ClientSecret);

        public static readonly TimeSpan MinSessionLifetime = TimeSpan.FromMinutes(5);

        public static readonly TimeSpan MaxSessionLifetime = TimeSpan.FromDays(30);


        public bool Enabled { get; set; }

        // The issuer URL; discovery is read from <Authority>/.well-known/openid-configuration.
        public string Authority { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        // Read from the environment only (ReadClientSecret). The settings file is rewritten
        // on every start and is registered after the environment provider, so a persisted
        // copy would both leak the secret to the volume and override the environment;
        // [JsonIgnore] keeps the resave from writing it.
        [JsonIgnore]
        public string ClientSecret { get; set; }

        // Space-separated, like the OAuth "scope" parameter. A string rather than an array:
        // the configuration binder appends file entries to an initialized array, so a
        // default array would double on every resave.
        public string Scopes { get; set; } = "openid profile";

        // The claim whose value is the HSM login of the signed-in person.
        public string UsernameClaim { get; set; } = "preferred_username";

        // The claim listing the person's groups (one claim per group or a JSON array).
        public string GroupsClaim { get; set; } = "groups";

        // When set, every SSO sign-in sets IsAdmin to membership in this group, granting
        // and revoking. Empty: SSO never changes IsAdmin.
        public string AdminGroup { get; set; } = string.Empty;

        // An SSO login without an HSM user creates one: no password, no roles, not admin
        // (unless AdminGroup says so). False: only existing users may sign in with SSO.
        public bool AutoCreateUsers { get; set; } = true;

        // The provider name on the login button ("Sign in with <DisplayName>").
        public string DisplayName { get; set; } = "SSO";

        // Absolute lifetime of the auth cookie an SSO sign-in issues (no sliding renewal).
        // When it ends the person goes through the provider again, which re-syncs AdminGroup.
        public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(12);


        // Set by Validate: the section is enabled and complete. A server whose section is
        // enabled but invalid keeps running with password login only.
        [JsonIgnore]
        public bool IsActive { get; private set; }

        // Values of the section that could not be converted (Bind); each one switches SSO off.
        private readonly List<string> _bindErrors = new();

        [JsonIgnore]
        public IReadOnlyList<string> ScopeList =>
            (Scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);


        // Binds the section key by key instead of with Get<OidcConfig>(), which throws on the
        // first value it cannot convert ("SessionLifetime": "12h", "Enabled": "yes") and
        // would stop the server at startup. A bad value keeps its default, is reported by
        // Validate and switches SSO off; the valid ones survive the resave.
        public static OidcConfig Bind(IConfigurationSection section)
        {
            var config = new OidcConfig();

            var settable = typeof(OidcConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                             .Where(p => p.SetMethod?.IsPublic == true && p.GetCustomAttribute<JsonIgnoreAttribute>() is null);

            foreach (var property in settable)
            {
                var value = section[property.Name];

                if (value is null)
                {
                    // An array or object where a scalar belongs ("Scopes": ["openid", ...]):
                    // reported instead of silently replaced by the default on the resave.
                    if (section.GetSection(property.Name).GetChildren().Any())
                        config._bindErrors.Add($"{SectionName}.{property.Name} must be a single value, not a list or an object " +
                                               $"(e.g. {property.GetValue(config) ?? "text"}); the default is used.");
                    continue;
                }

                try
                {
                    property.SetValue(config, section.GetValue(property.PropertyType, property.Name));
                }
                catch (InvalidOperationException)
                {
                    config._bindErrors.Add($"{SectionName}.{property.Name} = '{value}' is not a valid {property.PropertyType.Name} " +
                                           $"(e.g. {property.GetValue(config) ?? "text"}); the default is used.");
                }
            }

            return config;
        }

        public static string ReadClientSecret(IConfiguration environment)
        {
            var value = environment[ClientSecretKey];

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        // True when a settings file holds a client secret: it is ignored, and the same
        // start's resave drops it from the file. The value itself is never reported.
        public static bool HasClientSecretInSettingsFile(IConfigurationRoot configuration) =>
            configuration.Providers.OfType<FileConfigurationProvider>()
                         .Any(provider => provider.TryGet(ClientSecretKey, out var value) && !string.IsNullOrWhiteSpace(value));


        // Returns the problems that keep the section from working (empty when it is disabled
        // and well-formed, or complete) and sets IsActive accordingly. It does not throw: SSO
        // is an optional login path, and a typo in it must not take the monitoring server down.
        public List<string> Validate()
        {
            var errors = new List<string>(_bindErrors);

            if (!Enabled)
            {
                IsActive = false;
                return errors;
            }

            if (!Uri.TryCreate(Authority?.Trim(), UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps)
                errors.Add($"{SectionName}.{nameof(Authority)} must be an absolute https URL of the provider's issuer (was '{Authority}').");

            if (string.IsNullOrWhiteSpace(ClientId))
                errors.Add($"{SectionName}.{nameof(ClientId)} is empty.");

            if (string.IsNullOrWhiteSpace(ClientSecret))
                errors.Add($"The client secret is not set: pass it in the environment variable {ClientSecretKey.Replace(":", "__")} " +
                           $"(it is never read from {ServerConfig.ConfigName}).");

            if (!ScopeList.Contains("openid"))
                errors.Add($"{SectionName}.{nameof(Scopes)} must include 'openid' (was '{Scopes}').");

            if (string.IsNullOrWhiteSpace(UsernameClaim))
                errors.Add($"{SectionName}.{nameof(UsernameClaim)} is empty.");

            if (!string.IsNullOrWhiteSpace(AdminGroup) && string.IsNullOrWhiteSpace(GroupsClaim))
                errors.Add($"{SectionName}.{nameof(GroupsClaim)} is empty, but {nameof(AdminGroup)} needs it.");

            if (SessionLifetime < MinSessionLifetime || SessionLifetime > MaxSessionLifetime)
                errors.Add($"{SectionName}.{nameof(SessionLifetime)} must be between {MinSessionLifetime} and {MaxSessionLifetime.TotalDays:0} days (was {SessionLifetime}).");

            IsActive = errors.Count == 0;

            return errors;
        }
    }
}
