using System;
using HSMServer.ServerConfiguration;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // ApiTokensConfig contract (#1356 step 4, simplified by #1384): upgrade-safe defaults
    // in the channel sense — a deployment with no ApiTokens section gets the token
    // channel fully disabled — and startup validation with actionable, key-named errors
    // for every knob. The expiry knobs of the fine-granted model are gone: tokens are
    // eternal owner mirrors with an optional read-only flag.
    public class ApiTokensConfigTests
    {
        [Fact]
        public void Defaults_DisabledChannel_EternalTokens()
        {
            var config = new ApiTokensConfig();

            Assert.False(config.Enabled);
            Assert.Equal(10, config.MaxTokensPerUser);
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
