using NLog;
using NLog.Config;
using NLog.Filters;
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
    // (#1470), with ThrowConfigExceptions so nothing is swallowed: a malformed file, an
    // unresolvable extension assembly (NLog.Web.AspNetCore for ${aspnet-*}, HSMServer for
    // ${hsm-redacted}), an unknown layout renderer (a typo in ${hsm-redacted}), or a
    // property NLog 6 removed (the strict load caught exactly that: enableArchiveFileCompression
    // was gone from FileTarget and had been silently ignored at every server start) would
    // otherwise surface only when the released app image boots, not here. CI has no
    // VictoriaLogs/vlagent; loading the configuration needs none.
    public class NlogConfigTests
    {
        [Fact]
        public void NlogConfigLoadsWithStructuredLogTarget()
        {
            var factory = new LogFactory { ThrowConfigExceptions = true };
            var config = new XmlLoggingConfiguration(RepoFile("src/server/HSMServer/nlog.config"), factory);

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

            // The target hangs off a rule gated by a when-filter on HSM_STRUCTURED_LOGS:
            // an event reaches jsonfile only when the variable equals 'true' (the compose
            // app service reads it from .env and defaults it to 'false'), so deployments
            // without the variable - docker run, docker-compose.direct.yml, non-Docker -
            // never write the JSON archive. Losing the gate, its Log action, or the
            // Ignore default would make every deployment write HSM-structured-log-*.json
            // unasked.
            var rule = config.LoggingRules.SingleOrDefault(r => r.Targets.Contains(jsonFile));
            Assert.NotNull(rule);
            var gate = Assert.Single(rule.Filters.OfType<ConditionBasedFilter>());
            Assert.Equal(FilterResult.Log, gate.Action);
            Assert.Equal(FilterResult.Ignore, rule.FilterDefaultAction);
            Assert.Contains("HSM_STRUCTURED_LOGS", gate.Condition.ToString());
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
