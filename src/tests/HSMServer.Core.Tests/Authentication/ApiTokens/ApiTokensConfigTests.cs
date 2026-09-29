using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // ApiTokensConfig contract (#1356 step 4, simplified by #1384): the token channel is
    // enabled by default (no UI toggle; Disabled=true in the config file is the emergency
    // kill switch) and startup validation gives actionable, key-named errors for every
    // knob. The expiry knobs of the fine-granted model are gone: tokens are eternal owner
    // mirrors with an optional read-only flag.
    public class ApiTokensConfigTests
    {
        [Fact]
        public void Defaults_EnabledChannel_EternalTokens()
        {
            var config = new ApiTokensConfig();

            Assert.True(config.Enabled);
            Assert.False(config.Disabled);
            Assert.Equal(10, config.MaxTokensPerUser);
            Assert.Equal(TimeSpan.FromDays(30), config.TokenRecordRetention);
            Assert.Equal(TimeSpan.FromDays(30), config.SecurityEventRetention);
            Assert.Equal(60, config.InvalidAttemptRateLimit);
        }

        [Fact]
        public void Binding_LegacyEnabledFalse_IsIgnored()
        {
            // Every server that ran the old default resaved "Enabled": false; it must not
            // keep tokens off after the upgrade.
            var config = Bind(("ApiTokens:Enabled", "false"));

            Assert.True(config.Enabled);
        }

        [Fact]
        public void Binding_DisabledTrue_TurnsChannelOff()
        {
            var config = Bind(("ApiTokens:Disabled", "true"));

            Assert.False(config.Enabled);
        }

        [Fact]
        public void Serialization_PersistsDisabled_NotEnabled()
        {
            var json = JsonSerializer.Serialize(new ApiTokensConfig());

            Assert.Contains("\"Disabled\":false", json);
            Assert.DoesNotContain("\"Enabled\"", json);
        }

        [Fact]
        public void KillSwitch_HandEditOfRunningConfig_AppliesWithoutRestart()
        {
            // The switch is config-file only: an edit must reach the bound singleton on
            // reload, and survive a subsequent resave (any settings save) instead of the
            // stale in-memory value being written back.
            var memory = new Dictionary<string, string>();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(memory).Build();
            var server = new ServerConfig(configuration);
            Assert.True(server.ApiTokens.Enabled);

            configuration["ApiTokens:Disabled"] = "true";
            configuration.Reload();
            Assert.False(server.ApiTokens.Enabled);

            server.ResaveSettings();
            Assert.False(server.ApiTokens.Enabled);

            configuration["ApiTokens:Disabled"] = "false";
            configuration.Reload();
            Assert.True(server.ApiTokens.Enabled);
        }

        private static ApiTokensConfig Bind(params (string Key, string Value)[] values) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)))
                .Build()
                .GetSection("ApiTokens")
                .Get<ApiTokensConfig>();

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void MaxTokensPerUser_BelowOne_Throws(int value)
        {
            var config = new ApiTokensConfig { MaxTokensPerUser = value };

            var ex = Assert.Throws<InvalidOperationException>(config.Validate);

            Assert.Contains(nameof(ApiTokensConfig.MaxTokensPerUser), ex.Message);
            Assert.Contains($"{value}", ex.Message);
        }

        [Theory]
        [InlineData(-1)]
        public void TokenRecordRetention_Negative_Throws(int days)
        {
            var config = new ApiTokensConfig { TokenRecordRetention = TimeSpan.FromDays(days) };

            var ex = Assert.Throws<InvalidOperationException>(config.Validate);

            Assert.Contains(nameof(ApiTokensConfig.TokenRecordRetention), ex.Message);
        }


        [Fact]
        public void TokenRecordRetention_AboveTenYears_Throws()
        {
            var config = new ApiTokensConfig { TokenRecordRetention = TimeSpan.FromDays(3651) };

            Assert.Throws<InvalidOperationException>(config.Validate);
        }


        [Fact]
        public void SecurityEventRetention_Negative_Throws()
        {
            var config = new ApiTokensConfig { SecurityEventRetention = TimeSpan.FromHours(-1) };

            var ex = Assert.Throws<InvalidOperationException>(config.Validate);

            Assert.Contains(nameof(ApiTokensConfig.SecurityEventRetention), ex.Message);
        }


        [Fact]
        public void InvalidAttemptRateLimit_BelowOne_Throws()
        {
            var config = new ApiTokensConfig { InvalidAttemptRateLimit = 0 };

            var ex = Assert.Throws<InvalidOperationException>(config.Validate);

            Assert.Contains(nameof(ApiTokensConfig.InvalidAttemptRateLimit), ex.Message);
        }


        [Fact]
        public void ValidKnobs_PassValidation()
        {
            var config = new ApiTokensConfig
            {
                Enabled = true,
                MaxTokensPerUser = 25,
            };

            config.Validate();
        }
    }
}
