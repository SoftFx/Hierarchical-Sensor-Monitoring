using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace HSMServer.Core.Tests.Middleware
{
    // The upgrade path of the single-port change (#1590), sibling of the trusted-proxies
    // tests above: a settings file written before #1590 still holds
    // Kestrel:SensorPort/Kestrel:SitePort. The values configure nothing anymore —
    // Kestrel:Port replaced them — and the first resave removes them, so
    // FindLegacyPortKeysInSettingsFile is the one chance to report what is being ignored
    // (ServerConfig.LegacyKestrelPortKeysIgnored feeds the startup warning from it).
    // NOT in a ServerConfiguration namespace: that segment would shadow
    // HSMServer.ServerConfiguration for tests that say ServerConfiguration.ApiTokensConfig.
    public class KestrelConfigLegacyPortKeysTests
    {
        [Fact]
        public void BothLegacyKeys_AreReportedTogether()
        {
            Assert.Equal("SensorPort=44330, SitePort=44333",
                FindInSettingsFile("{ \"Kestrel\": { \"SensorPort\": 44330, \"SitePort\": 44333 } }"));
        }

        [Theory]
        [InlineData("{ \"Kestrel\": { \"SensorPort\": 8443 } }", "SensorPort=8443")]
        [InlineData("{ \"Kestrel\": { \"SitePort\": 9443 } }", "SitePort=9443")]
        public void OneLegacyKey_IsReportedAlone(string json, string expected)
        {
            Assert.Equal(expected, FindInSettingsFile(json));
        }

        [Theory]
        [InlineData("{ \"Kestrel\": { \"SensorPort\": \"\", \"SitePort\": \" \" } }")]
        [InlineData("{ \"Kestrel\": { \"SensorPort\": \"\" } }")]
        public void BlankValues_AreNotReported(string json)
        {
            // A blank key configures nothing, so there is nothing to warn about — and the
            // resave drops the keys either way.
            Assert.Null(FindInSettingsFile(json));
        }

        [Fact]
        public void NoLegacyKeys_ReturnsNull()
        {
            Assert.Null(FindInSettingsFile("{ \"Kestrel\": { \"Port\": 443 } }"));
        }

        [Fact]
        public void EnvironmentOnlyValues_AreNotReported()
        {
            // The warning is about the settings FILE being ignored; values supplied only
            // through non-file providers (environment variables) never lived there.
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    [KestrelConfig.SensorPortKey] = "44330",
                    [KestrelConfig.SitePortKey] = "44333",
                })
                .Build();

            Assert.Null(KestrelConfig.FindLegacyPortKeysInSettingsFile(configuration));
        }


        private static string FindInSettingsFile(string json)
        {
            var path = Path.Combine(Path.GetTempPath(), $"hsm-legacy-ports-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, json);

            try
            {
                var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();

                return KestrelConfig.FindLegacyPortKeysInSettingsFile(configuration);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
