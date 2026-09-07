using System;
using HSMServer.ServerConfiguration;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // ApiTokensConfig contract (initiative step 4): upgrade-safe defaults — a deployment
    // with no ApiTokens section gets the token channel fully disabled — and startup
    // validation with actionable, key-named errors for every knob.
    public class ApiTokensConfigTests
    {
        [Fact]
        public void Defaults_AreUpgradeSafeDisabledChannel()
        {
            var config = new ApiTokensConfig();

            Assert.False(config.Enabled);
            Assert.Equal(10, config.MaxTokensPerUser);
            Assert.False(config.AllowNoExpiration);
            Assert.Equal(TimeSpan.FromDays(90), config.DefaultLifetime);
            Assert.Equal(TimeSpan.FromDays(30), config.TokenRecordRetention);
            Assert.Equal(TimeSpan.FromDays(30), config.SecurityEventRetention);
            Assert.Equal(60, config.InvalidAttemptRateLimit);
        }

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
        [InlineData(0)]
        [InlineData(-1)]
        public void DefaultLifetime_NotPositive_Throws(int days)
        {
            var config = new ApiTokensConfig { DefaultLifetime = TimeSpan.FromDays(days) };

            var ex = Assert.Throws<InvalidOperationException>(config.Validate);

            Assert.Contains(nameof(ApiTokensConfig.DefaultLifetime), ex.Message);
        }


        [Fact]
        public void DefaultLifetime_BelowOneDay_Throws()
        {
            // The form's presets are day-granular; a 12h default would surface as a
            // misleading "1 days" preset.
            var config = new ApiTokensConfig { DefaultLifetime = TimeSpan.FromHours(12) };

            Assert.Throws<InvalidOperationException>(config.Validate);
        }

        [Fact]
        public void DefaultLifetime_AboveTenYears_Throws()
        {
            var config = new ApiTokensConfig { DefaultLifetime = TimeSpan.FromDays(3651) };

            Assert.Throws<InvalidOperationException>(config.Validate);
        }

        [Fact]
        public void ValidIssuanceKnobs_PassValidation()
        {
            var config = new ApiTokensConfig
            {
                Enabled = true,
                MaxTokensPerUser = 25,
                AllowNoExpiration = true,
                DefaultLifetime = TimeSpan.FromDays(30),
            };

            config.Validate();
        }
    }
}
