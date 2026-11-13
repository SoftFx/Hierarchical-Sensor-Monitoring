using HSMServer.Core.Model;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.IO;

namespace HSMServer.SensorApi
{
    // The server directives an agent gets (#1198), computed in one place for both transports:
    // the HTTP routes put them into X-Hsm-Directive response headers, the collector session
    // pushes them as frames (#1660 step 5). A directive is one line, "name:argument".
    //
    // Who gets them: a client that states its agent version (X-Agent-Version), which is how a
    // directive-aware agent identifies itself. Ordinary collectors state none and get nothing.
    //
    // What they are: the full desired state, not events. "update-available:<staged>" when
    // auto-update is on and the staged agent is newer than the stated one, then the state of
    // every known sensor group, enabled or disabled, so that switching a group back on in the UI
    // reaches the agent too. The agent ignores a directive that changes nothing.
    public sealed class AgentDirectives
    {
        public const string Header = "X-Hsm-Directive";
        public const string AgentVersionHeader = "X-Agent-Version";

        // How long a read of the staged agent version (wwwroot/agent/version.txt) is used. The
        // file changes only when an operator stages a new binary, and the HTTP routes ask on every
        // data request, the server's hottest path.
        public static readonly TimeSpan StagedVersionCacheAge = TimeSpan.FromSeconds(15);

        // In the order they are sent.
        private static readonly string[] _knownSensorGroups = ["computer", "system", "disk", "network", "module", "process"];

        private const string NoVersion = "0.0.0";

        private readonly IServerConfig _config;
        private readonly IWebHostEnvironment _environment;
        private readonly TimeProvider _time;

        private readonly object _stagedLock = new();
        private volatile StagedVersion _staged;


        public AgentDirectives(IServerConfig config, IWebHostEnvironment environment, TimeProvider time)
        {
            _config = config;
            _environment = environment;
            _time = time ?? TimeProvider.System;
        }


        // The directives for an agent that stated `agentVersion`, whose key belongs to
        // `product` (null when the caller is not known), in the order they are sent. Empty for
        // a client that stated no version.
        public List<string> For(string agentVersion, ProductModel product)
        {
            var directives = new List<string>(1 + _knownSensorGroups.Length);

            if (string.IsNullOrEmpty(agentVersion))
                return directives;

            // Agent and staged versions are plain numeric "x.y.z"; a semver pre-release or build
            // suffix fails Version.TryParse and skips the directive (accepted by contract).
            if (_config?.Agent?.AutoUpdateEnabled == true
                && Version.TryParse(agentVersion, out var agent)
                && !string.IsNullOrEmpty(_environment?.WebRootPath))
            {
                var staged = ReadStagedVersion(_environment.WebRootPath);

                if (Version.TryParse(staged, out var stagedVersion) && stagedVersion > agent)
                    directives.Add($"update-available:{staged}");
            }

            if (product is null)
                return directives;

            foreach (var group in _knownSensorGroups)
                directives.Add($"{(product.DisabledSensorGroups.Contains(group) ? "sensor-disable" : "sensor-enable")}:{group}");

            return directives;
        }

        private string ReadStagedVersion(string webRootPath)
        {
            var now = _time.GetTimestamp();
            var staged = _staged;

            // Lock-free while the last read is fresh.
            if (staged is not null && _time.GetElapsedTime(staged.ReadAt, now) < StagedVersionCacheAge)
                return staged.Version;

            lock (_stagedLock)
            {
                staged = _staged;

                // Another thread read it while this one waited.
                if (staged is not null && _time.GetElapsedTime(staged.ReadAt, now) < StagedVersionCacheAge)
                    return staged.Version;

                string version;

                try
                {
                    var text = File.ReadAllText(Path.Combine(webRootPath, "agent", "version.txt")).Trim();
                    version = string.IsNullOrEmpty(text) ? NoVersion : text;
                }
                catch
                {
                    version = NoVersion;
                }

                _staged = new StagedVersion(version, now);

                return version;
            }
        }


        private sealed record StagedVersion(string Version, long ReadAt);
    }
}
