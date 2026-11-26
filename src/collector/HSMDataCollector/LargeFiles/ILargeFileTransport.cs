using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client.Session;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.LargeFiles
{
    internal enum RegistrationOutcome
    {
        /// <summary>The server took the registration without an error under its path.</summary>
        Accepted,
        /// <summary>The server refused it: a per-path error, or a 4xx for the request. Final until the next Start.</summary>
        Refused,
        /// <summary>No answer, or a fault of the server: ask again later.</summary>
        Unsettled,
    }


    internal readonly struct LargeFileRegistration
    {
        internal LargeFileRegistration(RegistrationOutcome outcome, string reason)
        {
            Outcome = outcome;
            Reason = reason;
        }

        internal RegistrationOutcome Outcome { get; }

        internal string Reason { get; }
    }


    /// <summary>What the large file uploader needs of the collector session; the session sender implements it, tests fake it.</summary>
    internal interface ILargeFileTransport
    {
        /// <summary>The file connection of the session.</summary>
        IFileChannel Channel { get; }

        /// <summary>A number that changes with every new session: registrations are sent again on a new session.</summary>
        long SessionNumber { get; }

        /// <summary>The pause before the next attempt after <paramref name="attempt"/> failed ones (the session's backoff).</summary>
        TimeSpan Backoff(int attempt);

        /// <summary>
        /// How the ordinary registration of the sensor at <paramref name="path"/> stands on the session: true acknowledged
        /// without an error, false refused, null not answered yet. A routed send waits for true (amendment, decision 7).
        /// </summary>
        bool? RegistrationOf(string path);

        /// <summary>Registers one File sensor on the session (a <c>commands</c> request of its own) and waits for its ack.</summary>
        Task<LargeFileRegistration> RegisterAsync(AddOrUpdateSensorRequest request, CancellationToken token);
    }
}
