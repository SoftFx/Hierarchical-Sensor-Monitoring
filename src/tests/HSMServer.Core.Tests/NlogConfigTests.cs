using ModelContextProtocol;
using NLog;
using NLog.Config;
using NLog.Filters;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System;
using System.Collections.Generic;
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
    // otherwise surface only when the released app image boots, not here.
    public class NlogConfigTests
    {
        [Fact]
        public void NlogConfigLoadsTextTargetsOnly()
        {
            LoggingConfiguration config = LoadProductionConfig();

            // The server logs to exactly these three text targets; the structured
            // JSON target (and the log-store pipeline behind it) was removed by
            // ADR-0015 (#1594) and must not come back unnoticed. A new target must
            // consciously extend the retention contract below.
            var fileTargets = new[] { "errorfile", "allfile", "aspfile" }
                .Select(name => UnwrapFileTarget(config, name))
                .ToHashSet();
            Assert.Null(config.FindTargetByName("jsonfile"));
            Assert.All(
                config.LoggingRules.SelectMany(rule => rule.Targets),
                target => Assert.Contains(UnwrapTarget(target), fileTargets));

            // The Microsoft.AspNetCore* rule is final for framework logs (IgnoreFinal
            // in Release, LogFinal in Debug), which is what keeps per-request
            // framework Info noise (5-8 Info lines per HTTP request, dominated by
            // sensor ingestion) out of the text targets.
            var aspFileRule = config.LoggingRules.Single(r => r.LoggerNamePattern == "Microsoft.AspNetCore*" && r.Targets.Contains(config.FindTargetByName("aspfile")));
            Assert.Equal(FilterResult.IgnoreFinal, Assert.Single(aspFileRule.Filters.OfType<ConditionBasedFilter>()).Action);
        }

        // #1613: every expected MCP tool failure (bad arguments, an unknown id, a
        // write conflict) reaches the caller correctly as isError=true, but the
        // MCP SDK ALSO logs the tool's McpException at ERROR as "\"{tool}\" threw
        // an unhandled exception" from its ModelContextProtocol.Server.McpServer
        // logger - one test session alone produced 39 such entries, all client
        // mistakes. Those entries must stay out of HSM-error-log.txt (the operator
        // surface; noise there hides real errors) while remaining observable in
        // HSM-all-log.txt. This drives the REAL rules end-to-end with the file
        // targets swapped for in-memory capture, so the filter's condition is
        // evaluated, not just asserted on paper.
        [Fact]
        public void ExpectedMcpToolFailuresStayOutOfTheErrorLog()
        {
            var factory = new LogFactory { ThrowConfigExceptions = true };
            LoggingConfiguration config = LoadProductionConfig(factory);

            // Swap each target's FileTarget (behind its async wrapper) for a
            // MemoryTarget: the configured rules and filters run unmodified and
            // nothing touches disk.
            var capture = new Dictionary<string, MemoryTarget>();
            foreach (var name in new[] { "errorfile", "allfile", "aspfile" })
            {
                var wrapper = Assert.IsAssignableFrom<WrapperTargetBase>(config.FindTargetByName(name));
                var memory = new MemoryTarget();
                wrapper.WrappedTarget = memory;
                capture[name] = memory;
            }
            factory.Configuration = config;

            // The exact entry the SDK produces for an expected tool failure:
            // ERROR + McpException from the ModelContextProtocol.Server.McpServer
            // logger. It must reach the general log but not the error log.
            var mcpServer = factory.GetLogger("ModelContextProtocol.Server.McpServer");
            mcpServer.Error(new McpException("The requested resource was not found."), "\"get_sensor\" threw an unhandled exception.");
            factory.Flush();
            Assert.Empty(capture["errorfile"].Logs);
            Assert.NotEmpty(capture["allfile"].Logs);

            // A genuinely unexpected exception from the same logger IS an
            // operator error and keeps reaching the error log.
            capture["errorfile"].Logs.Clear();
            mcpServer.Error(new InvalidOperationException("boom"), "\"get_sensor\" threw an unhandled exception.");
            factory.Flush();
            Assert.NotEmpty(capture["errorfile"].Logs);

            // The filter is scoped to the SDK's tool-dispatch logger: an
            // McpException logged from anywhere else still lands in the error log.
            capture["errorfile"].Logs.Clear();
            factory.GetLogger("HSMServer.SomeComponent").Error(new McpException("boom"), "failed");
            factory.Flush();
            Assert.NotEmpty(capture["errorfile"].Logs);
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

            foreach (var name in new[] { "errorfile", "allfile", "aspfile" })
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
        }

        // The same retention contract, pinned for src/module/HSMPingModule/nlog.config.
        // The module runs on NLog 5.2.2, and its config keeps enableArchiveFileCompression
        // - a FileTarget property NLog 6 removed - so its file cannot go through this
        // project's NLog 6 typed load (ThrowConfigExceptions would reject that attribute);
        // the attributes are asserted over the raw XML instead. Weaker than the render
        // test above (no january/october render), but it fails on exactly the regressions
        // that would bring unbounded Logs/ growth back to the module: a date renderer in
        // fileName (the ${shortdate} scheme ADR-0011 retired), a dropped archiveAboveSize,
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
                // rotating them (ADR-0011).
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
        private static LoggingConfiguration LoadProductionConfig() =>
            LoadProductionConfig(new LogFactory { ThrowConfigExceptions = true });

        // The overload for tests that log THROUGH the loaded config: pass your
        // own factory, mutate the returned configuration (e.g. swap targets for
        // in-memory capture), then assign factory.Configuration = config so the
        // factory owns and initializes it.
        private static LoggingConfiguration LoadProductionConfig(LogFactory factory)
        {
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
        private static Target UnwrapTarget(Target current)
        {
            for (var depth = 0; current is WrapperTargetBase wrapper && depth < 4; depth++)
                current = wrapper.WrappedTarget;
            return current;
        }

        private static FileTarget UnwrapFileTarget(LoggingConfiguration config, string name)
        {
            var current = config.FindTargetByName(name);
            Assert.NotNull(current);
            return Assert.IsType<FileTarget>(UnwrapTarget(current));
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
