using NLog;
using NLog.Common;
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

            // The config's root element also carries internalLogLevel/internalLogFile, and
            // loading the file applies both to NLog's static InternalLogger for the rest
            // of the test process (the hard-coded c:\temp path is nonsense on the Linux
            // CI lanes); save the previous values and restore them so this test leaves
            // no trace. (A null saved value is assigned back as null.)
            var savedInternalLevel = InternalLogger.LogLevel;
            var savedInternalLogFile = InternalLogger.LogFile;
            try
            {
                var config = new XmlLoggingConfiguration(RepoFile("src/server/HSMServer/nlog.config"), factory);

                // jsonfile is the single structured target: the archive file that vlagent
                // (compose 'logs' profile) tails and ships to VictoriaLogs.
                var jsonFile = config.FindTargetByName("jsonfile");
                Assert.NotNull(jsonFile);

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

                // The written line is the contract vlagent consumes. Render one Info
                // event whose message carries a truncated hsm_pat_v1_ credential (the
                // redaction wrapper removes those entirely - prefix included) and whose
                // exception has a real multi-line stack trace. The newline escaping
                // happens in the JsonLayout itself, not in an attribute's raw layout,
                // so the single-line assertions run on the whole rendered line.
                Exception boom;
                try
                {
                    throw new InvalidOperationException("boom", new ArgumentException("inner"));
                }
                catch (Exception ex)
                {
                    boom = ex;
                }

                var logEvent = new LogEventInfo(LogLevel.Info, "NlogConfigTests", "auth failed for hsm_pat_v1_ABCDEF123456 (truncated)")
                {
                    Exception = boom,
                };

                var msg = layout.Attributes.Single(a => a.Name == "_msg").Layout.Render(logEvent);
                Assert.DoesNotContain("hsm_pat_", msg);

                var line = layout.Render(logEvent);
                Assert.DoesNotContain("\n", line);
                Assert.DoesNotContain("\r", line);

                var time = layout.Attributes.Single(a => a.Name == "_time").Layout.Render(logEvent);
                Assert.EndsWith("Z", time);

                // The target hangs off a rule gated by a when-filter on HSM_STRUCTURED_LOGS:
                // an event reaches jsonfile only when the variable equals 'true' (the compose
                // app service reads it from .env and defaults it to 'false'), so deployments
                // without the variable - docker run, docker-compose.direct.yml, non-Docker -
                // never write the JSON archive. Losing the gate, its Log action, or the
                // Ignore default would make every deployment write HSM-structured-log-*.json
                // unasked. The comparison itself is pinned too: flipping the operator to
                // != 'true' would pass a name-only assertion while silencing every
                // deployment's JSON log.
                var rule = config.LoggingRules.SingleOrDefault(r => r.Targets.Contains(jsonFile));
                Assert.NotNull(rule);
                var gate = Assert.Single(rule.Filters.OfType<ConditionBasedFilter>());
                Assert.Equal(FilterResult.Log, gate.Action);
                Assert.Equal(FilterResult.Ignore, rule.FilterDefaultAction);
                var gateCondition = gate.Condition.ToString();
                Assert.Contains("HSM_STRUCTURED_LOGS", gateCondition);
                Assert.Contains("== 'true'", gateCondition);
            }
            finally
            {
                InternalLogger.LogLevel = savedInternalLevel;
                InternalLogger.LogFile = savedInternalLogFile;
            }
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
