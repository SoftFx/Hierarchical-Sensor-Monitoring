using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests
{
    // Loads the real src/server/HSMServer/nlog.config the way NLog does at server startup
    // (#1470): a malformed file, an unresolvable extension assembly (HSMServer for
    // ${hsm-redacted}), or a broken rule would otherwise surface only when the released
    // app image boots. CI has no VictoriaLogs/vlagent; loading the configuration needs none.
    public class NlogConfigTests
    {
        [Fact]
        public void NlogConfigLoadsWithStructuredLogTarget()
        {
            var config = new XmlLoggingConfiguration(RepoFile("src/server/HSMServer/nlog.config"));

            // jsonfile is the single structured target: the archive file that vlagent
            // (compose 'logs' profile) tails and ships to VictoriaLogs. The direct-post
            // vl-web target is gone since the vlagent pivot.
            var jsonFile = config.FindTargetByName("jsonfile");
            Assert.NotNull(jsonFile);
            Assert.Null(config.FindTargetByName("vl-web"));

            // vlagent maps the JSON fields by name (msgField/timeField flags in
            // docker-compose.yml), so the reserved names are a contract: _time and
            // _msg must stay, everything else ships as an individual log field.
            // (The targets-level async="true" adds its own wrapper on top; walk it.)
            var current = jsonFile;
            for (var depth = 0; current is WrapperTargetBase wrapper && depth < 4; depth++)
                current = wrapper.WrappedTarget;
            var layout = ((FileTarget)current).Layout as JsonLayout;
            Assert.NotNull(layout);
            Assert.Contains(layout.Attributes, a => a.Name == "_time");
            Assert.Contains(layout.Attributes, a => a.Name == "_msg");

            // The target hangs off the HSM_STRUCTURED_LOGS-gated rule (its when-filter),
            // so non-compose deployments never write the JSON archive.
            var rule = config.LoggingRules.SingleOrDefault(r => r.Targets.Contains(jsonFile));
            Assert.NotNull(rule);
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
