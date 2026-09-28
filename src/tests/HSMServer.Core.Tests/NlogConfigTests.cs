using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests
{
    // Loads the real src/server/HSMServer/nlog.config the way NLog does at server startup
    // (#1470): a malformed file, an unresolvable extension assembly (NLog.Targets.WebService,
    // HSMServer for ${hsm-redacted}), or a broken rule would otherwise surface only when the
    // released app image boots. CI has no VictoriaLogs; loading the configuration needs none.
    public class NlogConfigTests
    {
        [Fact]
        public void NlogConfigLoadsWithStructuredLogTargets()
        {
            var config = new XmlLoggingConfiguration(RepoFile("src/server/HSMServer/nlog.config"));

            var jsonFile = config.FindTargetByName("jsonfile");
            var vlWeb = config.FindTargetByName("vl-web");
            Assert.NotNull(jsonFile);
            Assert.NotNull(vlWeb);

            // vl-web wraps the WebService target that posts to VictoriaLogs (the targets-level
            // async="true" adds its own wrapper on top); walk the chain without referencing the
            // satellite package here.
            var current = vlWeb;
            var sawWebService = false;
            for (var depth = 0; current is WrapperTargetBase wrapper && depth < 4; depth++)
            {
                current = wrapper.WrappedTarget;
                if (current.GetType().Name == "WebServiceTarget")
                    sawWebService = true;
            }
            Assert.True(sawWebService, "vl-web must wrap a WebService target");

            // Both structured targets hang off one rule, so the HSM_STRUCTURED_LOGS gate
            // (the rule's when-filter) covers the archive file and the direct posts together.
            var rule = config.LoggingRules.SingleOrDefault(r => r.Targets.Contains(jsonFile));
            Assert.NotNull(rule);
            Assert.Contains(vlWeb, rule.Targets);
        }

        private static string RepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            var native = relative.Replace('/', Path.DirectorySeparatorChar);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, native);
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            throw new FileNotFoundException($"Could not locate '{relative}' above {AppContext.BaseDirectory}");
        }
    }
}
