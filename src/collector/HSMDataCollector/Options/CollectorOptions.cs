using System;
using System.Collections.Generic;

namespace HSMDataCollector.Core
{
    public sealed class CollectorOptions
    {
        internal const string LocalhostAddress = "localhost";
        internal const int DefaultPort = 443;


        public string ServerAddress { get; set; } = LocalhostAddress;

        public string AccessKey { get; set; }

        public string ClientName { get; set; }

        public int Port { get; set; } = DefaultPort;


        public string ComputerName { get; set; }

        public string Module { get; set; }


        public int MaxQueueSize { get; set; } = 20000;

        public int MaxValuesInPackage { get; set; } = 1000;

        public TimeSpan PackageCollectPeriod { get; set; } = TimeSpan.FromSeconds(15);

        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public IDataSender DataSender { get; set; }

        /// <summary>
        /// How the built-in sender talks to the server: over the HTTP routes (the default), or
        /// over a collector session, one WebSocket on the same address and port. A collector on
        /// a session never uses the HTTP routes: while no session can be opened its values wait
        /// in the queues. The setting belongs to the built-in sender; with a custom
        /// <see cref="DataSender"/> it has no effect, and Start says so.
        /// </summary>
        public CollectorTransport Transport { get; set; } = CollectorTransport.Http;

        /// <summary>The session's waits; a seam for tests. Null takes the protocol's defaults.</summary>
        internal Client.Session.SessionTimings SessionTimings { get; set; }

        internal string ServerUrl => ServerAddress.TrimEnd('/');

        public bool AllowUntrustedServerCertificate { get; set; }

        public bool AllowPlaintextTransport { get; set; }

        public TimeSpan ExceptionDeduplicatorWindow { get; set; } = TimeSpan.FromHours(1);

        public int MaxDeduplicatedMessages { get; set; } = 1000;

        public int MaxSensors { get; set; } = 100000;

        /// <summary>
        /// Headers the collector adds to every request that carries sensor values, next to its
        /// own <c>Key</c> and <c>ClientName</c>. They state facts about the host for the server
        /// (an agent sends <c>X-Agent-Version</c>, which is what makes the server answer with
        /// directives). Read once, when the collector creates its sender; registrations do not
        /// carry them. They belong to the built-in sender: with a custom <see cref="DataSender"/>
        /// they are validated and otherwise unused. A name the collector sets itself
        /// (<c>Key</c>, <c>ClientName</c>), a header of the request body or of the connection,
        /// a name that is not a plain token and a value that is not printable ASCII are refused
        /// at construction.
        /// </summary>
        public IDictionary<string, string> ExtraRequestHeaders { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Headers a host may not set: the collector's own, those that describe the request
        // body or the connection, which the HTTP stack owns, and the cookie headers, which the
        // .NET Framework handler rebuilds from its own cookie container (a hand-set Cookie is
        // sent by the net6.0 build and silently lost by the net472 one).
        private static readonly HashSet<string> ReservedRequestHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Key", "ClientName",
            "Host", "Connection", "Upgrade", "Transfer-Encoding", "Expect", "Proxy-Connection", "Keep-Alive", "TE", "Trailer",
            "Content-Type", "Content-Length", "Content-Encoding",
            "Cookie", "Cookie2",
        };

        private const string HeaderNameSeparators = "\"(),/:;<=>?@[\\]{}";

        private static void ValidateExtraRequestHeader(string name, string value)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("An extra request header needs a name.", nameof(ExtraRequestHeaders));

            foreach (var ch in name)
                if (ch <= ' ' || ch >= 127 || HeaderNameSeparators.IndexOf(ch) >= 0)
                    throw new ArgumentException($"'{name}' is not a valid request header name.", nameof(ExtraRequestHeaders));

            if (ReservedRequestHeaders.Contains(name)
                || name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Sec-WebSocket-", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"The request header '{name}' is set by the collector and cannot be replaced.", nameof(ExtraRequestHeaders));

            if (value == null)
                throw new ArgumentException($"The request header '{name}' has no value.", nameof(ExtraRequestHeaders));

            // Printable ASCII and tab only. A line break would be a second header; anything
            // outside ASCII is refused by one HTTP stack at send time and mangled by the other.
            foreach (var ch in value)
                if ((ch < ' ' && ch != '\t') || ch >= 127)
                    throw new ArgumentException($"The value of the request header '{name}' is not printable ASCII.", nameof(ExtraRequestHeaders));

            // The HTTP stack has the last word: a name it files under the content or response
            // headers would be dropped from the request without a sound.
            using (var probe = new System.Net.Http.HttpRequestMessage())
                if (!probe.Headers.TryAddWithoutValidation(name, value))
                    throw new ArgumentException($"'{name}' cannot be sent as a request header.", nameof(ExtraRequestHeaders));
        }

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(AccessKey))
                throw new ArgumentNullException(nameof(AccessKey));

            if (string.IsNullOrWhiteSpace(ServerAddress))
                throw new ArgumentNullException(nameof(ServerAddress));

            if (Port <= 0 || Port > 65535)
                throw new ArgumentOutOfRangeException(nameof(Port), "Port must be in the range 1..65535.");

            if (MaxQueueSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxQueueSize), "Max queue size must be greater than zero.");

            if (MaxValuesInPackage <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxValuesInPackage), "Max values in package must be greater than zero.");

            if (PackageCollectPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(PackageCollectPeriod), "Package collect period must be greater than zero.");

            if (RequestTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(RequestTimeout), "Request timeout must be greater than zero.");

            if (ExceptionDeduplicatorWindow < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ExceptionDeduplicatorWindow), "Exception deduplicator window cannot be negative.");

            if (MaxDeduplicatedMessages <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxDeduplicatedMessages), "Max deduplicated messages must be greater than zero.");

            if (MaxSensors <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxSensors), "Max sensors must be greater than zero.");

            if (Transport != CollectorTransport.Http && Transport != CollectorTransport.Session)
                throw new ArgumentOutOfRangeException(nameof(Transport), "The transport is either Http or Session.");

            foreach (var header in ExtraRequestHeaders)
                ValidateExtraRequestHeader(header.Key, header.Value);
        }
    }
}
