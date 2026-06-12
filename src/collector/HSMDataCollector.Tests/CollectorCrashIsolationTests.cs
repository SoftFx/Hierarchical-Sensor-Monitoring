using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// Process-isolated crash regression lane for the #1102 A1/A2 host-crash vectors (#1103).
    /// A host-process crash cannot be asserted from inside the test process, so each case spawns
    /// HSMDataCollector.CrashTests.Host with a scenario that wires a deliberately hostile callback
    /// and triggers the vector. The host prints HOST_SURVIVED and exits 0 only if callback isolation
    /// holds; while a vector is live the injected exception kills the spawned process before the
    /// sentinel, which fails the assertion (red before the fix, green after).
    /// </summary>
    public sealed class CollectorCrashIsolationTests
    {
        private const string SurvivedSentinel = "HOST_SURVIVED";
        private const string HostProjectName = "HSMDataCollector.CrashTests.Host";

        private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(20);

        // A1: throwing host callbacks on the scheduler error path (public ExceptionThrowing event and
        // the internal onError seam every monitoring sensor passes HandleException into).
        [Theory]
        [InlineData("a1-throwing-exception-subscriber")]
        [InlineData("a1-throwing-onerror")]
        // A2: malformed EventCounters payloads through the real EventSource -> EventListener dispatch,
        // plus the true ETW-path time-in-gc smoke.
        [InlineData("a2-etw-malformed-counters")]
        [InlineData("a2-etw-time-in-gc-smoke")]
        public void Hostile_callback_does_not_crash_host_process(string scenario)
        {
            var result = RunCrashHost(scenario);

            Assert.True(result.ExitCode == 0,
                $"Scenario '{scenario}' crashed or failed the spawned host process (exit code {result.ExitCode})." +
                $"{Environment.NewLine}stdout:{Environment.NewLine}{result.StdOut}" +
                $"{Environment.NewLine}stderr:{Environment.NewLine}{result.StdErr}");
            Assert.Contains(SurvivedSentinel, result.StdOut);
        }

        [Fact]
        public void Crash_host_reports_unknown_scenarios_instead_of_passing_vacuously()
        {
            var result = RunCrashHost("no-such-scenario");

            Assert.Equal(64, result.ExitCode);
            Assert.DoesNotContain(SurvivedSentinel, result.StdOut);
        }

        private static HostRunResult RunCrashHost(string scenario)
        {
            var hostAssembly = LocateHostAssembly();

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"exec \"{hostAssembly}\" {scenario}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(hostAssembly)
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                var stdOut = new StringBuilder();
                var stdErr = new StringBuilder();

                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (stdOut) stdOut.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stdErr) stdErr.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit((int)HostTimeout.TotalMilliseconds))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Already exited between the timeout and the kill; nothing to clean up.
                    }

                    throw new TimeoutException(
                        $"Crash host scenario '{scenario}' did not exit within {HostTimeout}. Spawned-process tests must stay fast (<5 s).");
                }

                // Flush the async output handlers before reading the buffers.
                process.WaitForExit();

                lock (stdOut)
                lock (stdErr)
                    return new HostRunResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
            }
        }

        private static string LocateHostAssembly()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var collectorRoot = new DirectoryInfo(baseDirectory);

            while (collectorRoot != null && !Directory.Exists(Path.Combine(collectorRoot.FullName, HostProjectName)))
                collectorRoot = collectorRoot.Parent;

            if (collectorRoot == null)
                throw new InvalidOperationException($"Could not locate the '{HostProjectName}' project next to the test assembly ({baseDirectory}).");

            var hostBin = Path.Combine(collectorRoot.FullName, HostProjectName, "bin");
            var preferredConfiguration = baseDirectory.IndexOf($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Release"
                : "Debug";

            foreach (var configuration in new[] { preferredConfiguration, preferredConfiguration == "Release" ? "Debug" : "Release" })
            {
                var candidate = Path.Combine(hostBin, configuration, "net6.0", HostProjectName + ".dll");
                if (File.Exists(candidate))
                    return candidate;
            }

            throw new InvalidOperationException(
                $"The crash host assembly was not found under '{hostBin}'. Build {HostProjectName} (it is part of HSMDataCollector.sln) before running the crash isolation tests.");
        }

        private sealed class HostRunResult
        {
            internal HostRunResult(int exitCode, string stdOut, string stdErr)
            {
                ExitCode = exitCode;
                StdOut = stdOut;
                StdErr = stdErr;
            }

            internal int ExitCode { get; }

            internal string StdOut { get; }

            internal string StdErr { get; }
        }
    }
}
