using NLog;
using NLog.Config;
using NLog.Filters;
using NLog.Layouts;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace HSMServer.Core.Tests
{
    // Loads the real src/server/HSMServer/nlog.config the way NLog does at server startup
    // (#1470), with ThrowConfigExceptions so nothing is swallowed: a malformed file, an
    // unresolvable extension assembly (NLog.Web.AspNetCore for ${aspnet-*}, HSMServer for
    // ${hsm-redacted}), an unknown layout renderer (a typo in ${hsm-redacted}), or a
    // target property NLog 6 removed (e.g. enableArchiveFileCompression, dropped from
    // FileTarget and silently ignored at every server start before such a guard) would
    // otherwise surface only when the released app image boots, not here. CI has no
    // VictoriaLogs/vlagent; loading the configuration needs none.
    public class NlogConfigTests
    {
        [Fact]
        public void NlogConfigLoadsWithStructuredLogTarget()
        {
            var factory = new LogFactory { ThrowConfigExceptions = true };

            // The config's root element carries internalLogLevel/internalLogFile, and
            // loading the file applies both to NLog's static InternalLogger (the
            // hard-coded c:\temp path is a relative filename NLog would create in the
            // working directory on the Linux CI lanes) - process-global state that
            // parallel test classes can observe. So both attributes are stripped from
            // the XML before the load: the test process is never touched, and no
            // save/restore of the InternalLogger is needed. The guard assertion below
            // fails loudly if the production config renames or adds internalLog*
            // attributes, so the stripping cannot silently stop matching.
            var configPath = RepoFile("src/server/HSMServer/nlog.config");
            var strippedXml = Regex.Replace(
                File.ReadAllText(configPath),
                @"\sinternalLog(Level|File)\s*=\s*(""[^""]*""|'[^']*')",
                string.Empty);
            Assert.DoesNotContain("internalLog", strippedXml);

            LoggingConfiguration config;
            using (var reader = new StringReader(strippedXml))
                config = new XmlLoggingConfiguration(reader, configPath, factory);

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

            // Both jsonfile rules are gated by a when-filter on HSM_STRUCTURED_LOGS:
            // an event reaches jsonfile only when the variable equals 'true' (the compose
            // app service reads it from .env and defaults it to 'false'), so deployments
            // without the variable - docker run, docker-compose.direct.yml, non-Docker -
            // never write the JSON archive. Losing the gate, its Log action, or the
            // Ignore default would make every deployment write HSM-structured-log-*.json
            // unasked. The comparison itself is pinned too: flipping the operator to
            // != 'true' would pass a name-only assertion while silencing every
            // deployment's JSON log.
            var appJsonRule = config.LoggingRules.SingleOrDefault(r => r.LoggerNamePattern == "*" && r.Targets.Contains(jsonFile));
            Assert.NotNull(appJsonRule);
            AssertStructuredLogsGate(appJsonRule);

            // Placement contract: the '* Info' jsonfile rule must sit BELOW the
            // Microsoft.AspNetCore* aspfile rule. That rule is final for framework logs
            // (IgnoreFinal in Release, LogFinal in Debug), which is what keeps
            // per-request framework Info noise (5-8 Info lines per HTTP request,
            // dominated by sensor ingestion) out of the text targets and out of the
            // JSON file alike - only application events reach that rule's write.
            // Moving it above the final rule would write every request to disk,
            // vlagent, and VictoriaLogs.
            var aspFileRule = config.LoggingRules.Single(r => r.LoggerNamePattern == "Microsoft.AspNetCore*" && r.Targets.Contains(config.FindTargetByName("aspfile")));
            Assert.Equal(FilterResult.IgnoreFinal, Assert.Single(aspFileRule.Filters.OfType<ConditionBasedFilter>()).Action);
            Assert.True(
                config.LoggingRules.IndexOf(aspFileRule) < config.LoggingRules.IndexOf(appJsonRule),
                "the '* Info' jsonfile rule must come after the Microsoft.AspNetCore* final rule");

            // Framework Warn+ placement contract: a second jsonfile
            // rule scoped to Microsoft.AspNetCore* with minlevel Warn must sit ABOVE
            // the final aspfile rule. Without it, every framework Warn/Error event -
            // Kestrel connection errors, unhandled-exception middleware, DataProtection
            // key warnings - is stopped by the final rule before any lower jsonfile
            // rule and never reaches the searchable store, while the text errorfile
            // rule above still gets the Errors. Warn+ (not Info+) so request noise
            // stays out; framework events never survive past the final rule and app
            // events never match this pattern, so nothing is written twice.
            var aspNetJsonRules = config.LoggingRules.Where(r => r.LoggerNamePattern == "Microsoft.AspNetCore*" && r.Targets.Contains(jsonFile)).ToList();
            var frameworkJsonRule = Assert.Single(aspNetJsonRules);
            // Exactly Warn+ (Warn, Error, Fatal): not Info+ (request noise), and a
            // contrived non-contiguous level set must not pass either.
            Assert.Contains(LogLevel.Warn, frameworkJsonRule.Levels);
            Assert.Contains(LogLevel.Error, frameworkJsonRule.Levels);
            Assert.Contains(LogLevel.Fatal, frameworkJsonRule.Levels);
            Assert.DoesNotContain(LogLevel.Trace, frameworkJsonRule.Levels);
            Assert.DoesNotContain(LogLevel.Info, frameworkJsonRule.Levels);
            AssertStructuredLogsGate(frameworkJsonRule);
            Assert.True(
                config.LoggingRules.IndexOf(frameworkJsonRule) < config.LoggingRules.IndexOf(aspFileRule),
                "the framework Warn+ jsonfile rule must come before the Microsoft.AspNetCore* final rule");
        }

        // The HSM_STRUCTURED_LOGS gate shared by both jsonfile rules: a when-filter
        // with the Log action and the Ignore default, comparing the variable to
        // 'true', with the lookup cached (cached=true ambient option on ${environment}):
        // the env value cannot change without a restart, so the gate must not run
        // getenv on every matching event.
        private static void AssertStructuredLogsGate(LoggingRule rule)
        {
            var gate = Assert.Single(rule.Filters.OfType<ConditionBasedFilter>());
            Assert.Equal(FilterResult.Log, gate.Action);
            Assert.Equal(FilterResult.Ignore, rule.FilterDefaultAction);
            var gateCondition = gate.Condition.ToString();
            Assert.Contains("HSM_STRUCTURED_LOGS", gateCondition);
            Assert.Contains("cached=true", gateCondition);
            Assert.Contains("== 'true'", gateCondition);

            // Behavioral pin for the cache: the strict load does not validate option
            // names inside a when-condition (an unknown option there is silently
            // ignored, not a config exception), so the marker above is only text until
            // caching is observed. Evaluate the gate twice across an env-var flip: a
            // cached lookup keeps the first rendered value, a per-event getenv would
            // pick up the new one and this second assertion fails.
            var probeEvent = new LogEventInfo(LogLevel.Info, "NlogConfigTests", null);
            var originalValue = Environment.GetEnvironmentVariable("HSM_STRUCTURED_LOGS");
            try
            {
                Environment.SetEnvironmentVariable("HSM_STRUCTURED_LOGS", "true");
                Assert.True((bool)gate.Condition.Evaluate(probeEvent));

                Environment.SetEnvironmentVariable("HSM_STRUCTURED_LOGS", "false");
                Assert.True((bool)gate.Condition.Evaluate(probeEvent), "the HSM_STRUCTURED_LOGS lookup must be cached, not re-read per event");
            }
            finally
            {
                Environment.SetEnvironmentVariable("HSM_STRUCTURED_LOGS", originalValue);
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
