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
using System.Xml.Linq;
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
            LoggingConfiguration config = LoadProductionConfig();

            // jsonfile is the single structured target: the archive file that vlagent
            // (compose 'logs' profile) tails and ships to VictoriaLogs.
            var jsonFile = config.FindTargetByName("jsonfile");
            Assert.NotNull(jsonFile);

            // vlagent maps the JSON fields by name (msgField/timeField flags in
            // docker-compose.yml), so the reserved names are a contract: _time and
            // _msg must stay, everything else ships as an individual log field.
            var layout = UnwrapFileTarget(config, "jsonfile").Layout as JsonLayout;
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

            // _msg is a stored, indexed field, so its shape is a contract. The message
            // and the exception must both survive, separated by exactly one space: the
            // space lives inside ${onexception}'s inner layout (nlog.config), so it is
            // emitted only when there is an exception to append.
            Assert.Contains("auth failed for", msg);
            Assert.Contains("(truncated)", msg);
            Assert.Contains("boom", msg);
            Assert.Contains("(truncated) System.InvalidOperationException", msg);

            // Without an exception the same attribute must render the bare message:
            // a plain '${message} ${exception}' layout leaves a trailing space there
            // (the exception half renders empty), which every stored line would carry.
            var msgWithoutException = layout.Attributes.Single(a => a.Name == "_msg").Layout.Render(
                new LogEventInfo(LogLevel.Info, "NlogConfigTests", "plain message"));
            Assert.EndsWith("plain message", msgWithoutException);

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

        // The retention contract pinned from the 2026-10 dev incident (49 GiB in Logs/,
        // of which 22 GiB was a two-day event flood): NLog archives a file — and applies
        // maxArchiveFiles cleanup — only when it rotates a STATIC fileName. With a
        // ${shortdate}-dated fileName every day opens a new file and yesterday's file is
        // abandoned in Logs/ forever (no archive move, no cleanup; 566 such files had
        // accumulated on dev since January). Every target must therefore keep both
        // fileName and archiveFileName event-time-independent (no date renderer in
        // either), rotate daily AND at 100 MB, and cap the archive directory at 60
        // files (~6 GiB per target worst case).
        [Fact]
        public void FileTargetsRotateStaticNamesIntoBoundedArchives()
        {
            var config = LoadProductionConfig();

            var january = new LogEventInfo(LogLevel.Info, "NlogConfigTests", null) { TimeStamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc) };
            var october = new LogEventInfo(LogLevel.Info, "NlogConfigTests", null) { TimeStamp = new DateTime(2026, 10, 23, 19, 30, 12, DateTimeKind.Utc) };

            foreach (var name in new[] { "errorfile", "allfile", "aspfile", "jsonfile" })
            {
                var target = UnwrapFileTarget(config, name);

                var active = target.FileName.Render(january);
                Assert.Equal(active, target.FileName.Render(october));

                var archive = target.ArchiveFileName.Render(january);
                Assert.Equal(archive, target.ArchiveFileName.Render(october));
                Assert.Contains("Archives", archive);

                // DateAndSequence exists precisely for day+size rotation: unique
                // date+sequence archive names for the multiple same-day archives a
                // 100 MB size rotation produces (docker.md documents the naming).
                // Compared via ToString: the enum's type name moves between NLog
                // majors, its member names do not.
                Assert.Equal("DateAndSequence", target.ArchiveNumbering.ToString());
                Assert.Equal(FileArchivePeriod.Day, target.ArchiveEvery);
                Assert.Equal(60, target.MaxArchiveFiles);
                Assert.Equal(ArchiveRotationBytes, target.ArchiveAboveSize);
            }

            // vlagent can only ship what its glob in docker-compose.yml matches, so the
            // static jsonfile name must stay inside HSM-structured-log-*.json (dated
            // names would defeat archiving; a name outside the glob would silently stop
            // all shipping to VictoriaLogs). Archives live in the Archives/ subdirectory,
            // which the non-recursive glob never descends into, so only the active file
            // ships. The compose line is parsed naively on purpose: it is a one-line
            // scalar in the file, and any format change here should break this test
            // rather than silently keep matching an outdated glob.
            var jsonActive = Path.GetFileName(UnwrapFileTarget(config, "jsonfile").FileName.Render(january));
            var composeGlob = Path.GetFileName(
                File.ReadLines(RepoFile("docker-compose.yml"))
                    .Single(line => line.Contains("-fileCollector.glob="))
                    .Split('=')[^1].Trim().Trim('\'').Trim('"'));
            Assert.Matches(
                new Regex("^" + Regex.Escape(composeGlob).Replace(@"\*", ".*") + "$"),
                jsonActive);
        }

        // The same retention contract, pinned for src/module/HSMPingModule/nlog.config.
        // The module runs on NLog 5.2.2, and its config keeps enableArchiveFileCompression
        // - a FileTarget property NLog 6 removed - so its file cannot go through this
        // project's NLog 6 typed load (ThrowConfigExceptions would reject that attribute);
        // the attributes are asserted over the raw XML instead. Weaker than the render
        // test above (no january/october render), but it fails on exactly the regressions
        // that would bring unbounded Logs/ growth back to the module: a date renderer in
        // fileName (the ${shortdate} scheme ADR-0010 retired), a dropped archiveAboveSize,
        // or a loosened archive cap.
        [Fact]
        public void HsmPingModuleFileTargetsFollowRetentionContract()
        {
            var configXml = XDocument.Load(RepoFile("src/module/HSMPingModule/nlog.config"));
            var nlogNs = configXml.Root!.Name.Namespace;
            var fileTargets = configXml.Root!.Element(nlogNs + "targets")!
                .Elements(nlogNs + "target")
                .Where(t => (string?)t.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance")) == "File")
                .ToList();

            // The module has exactly two File targets; a new one must consciously extend
            // this contract (or the test), not appear unnoticed.
            Assert.Equal(
                new[] { "allfile", "errorfile" },
                fileTargets.Select(t => (string?)t.Attribute("name")).OrderBy(n => n));

            foreach (var target in fileTargets)
            {
                Assert.Equal("DateAndSequence", (string?)target.Attribute("archiveNumbering"));
                Assert.Equal("Day", (string?)target.Attribute("archiveEvery"));
                Assert.Equal(ArchiveRotationBytes.ToString(), (string?)target.Attribute("archiveAboveSize"));
                Assert.Equal("60", (string?)target.Attribute("maxArchiveFiles"));

                // Static names: neither fileName nor archiveFileName may carry an
                // event-time renderer - a dated name abandons files instead of
                // rotating them (ADR-0010).
                foreach (var attributeName in new[] { "fileName", "archiveFileName" })
                {
                    var name = (string?)target.Attribute(attributeName);
                    Assert.NotNull(name);
                    Assert.DoesNotMatch(
                        new Regex(@"\$\{(shortdate|longdate|date|time)", RegexOptions.IgnoreCase),
                        name);
                }

                var archiveName = (string?)target.Attribute("archiveFileName");
                Assert.Contains("Archives", archiveName);
                Assert.Contains("{#}", archiveName);
            }
        }

        // The 100 MB per-file rotation threshold pinned by the retention-contract test
        // above; the same value sits in every target's archiveAboveSize in nlog.config.
        private const long ArchiveRotationBytes = 104857600L;

        // Loads the real production config the way NLog does at server startup, with
        // ThrowConfigExceptions so nothing is swallowed: a malformed file, an
        // unresolvable extension assembly (NLog.Web.AspNetCore for ${aspnet-*},
        // HSMServer for ${hsm-redacted}), an unknown layout renderer, or a target
        // property NLog 6 removed would otherwise surface only when the released app
        // image boots, not here.
        //
        // The config's root element carries internalLogLevel/internalLogFile, and
        // loading the file applies both to NLog's static InternalLogger (the
        // hard-coded c:\temp path is a relative filename NLog would create in the
        // working directory on the Linux CI lanes) - process-global state that
        // parallel test classes can observe. So both attributes are stripped from
        // the XML before the load: the test process is never touched, and no
        // save/restore of the InternalLogger is needed. The guard assertion fails
        // loudly if the production config renames or adds internalLog* attributes,
        // so the stripping cannot silently stop matching.
        private static LoggingConfiguration LoadProductionConfig()
        {
            var factory = new LogFactory { ThrowConfigExceptions = true };

            var configPath = RepoFile("src/server/HSMServer/nlog.config");
            var strippedXml = Regex.Replace(
                File.ReadAllText(configPath),
                @"\sinternalLog(Level|File)\s*=\s*(""[^""]*""|'[^']*')",
                string.Empty);
            Assert.DoesNotContain("internalLog", strippedXml);

            using var reader = new StringReader(strippedXml);
            return new XmlLoggingConfiguration(reader, configPath, factory);
        }

        // The targets-level async="true" adds a wrapper on top of every target; walk it
        // down to the real FileTarget.
        private static FileTarget UnwrapFileTarget(LoggingConfiguration config, string name)
        {
            var current = config.FindTargetByName(name);
            for (var depth = 0; current is WrapperTargetBase wrapper && depth < 4; depth++)
                current = wrapper.WrappedTarget;
            return Assert.IsType<FileTarget>(current);
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
