using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client.HttpsClient;
using HSMDataCollector.LargeFiles;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Client.Session
{
    /// <summary>
    /// Large files on the collector session (ADR-0022, #1888): their File sensors are registered by a <c>commands</c>
    /// request of their own with an ack, before the first <c>begin</c> (the file connection is another socket, and a begin
    /// could overtake its registration); their pieces go on the file connection (<see cref="FileConnection"/>), made on
    /// first use.
    /// </summary>
    internal sealed partial class SessionDataSender : ILargeFileTransport
    {
        private FileConnection _files;

        /// <summary>The file connection of this session, made on first use; for tests, also its seams.</summary>
        internal FileConnection Files
        {
            get
            {
                var files = Volatile.Read(ref _files);

                if (files != null)
                    return files;

                var made = new FileConnection(_connection, _logger, _sessionTimings, _requestTimeout);

                return Interlocked.CompareExchange(ref _files, made, null) ?? made;
            }
        }

        /// <summary>Why large files cannot be sent here, or null.</summary>
        internal string LargeFilesUnavailableReason =>
            _unavailable == null ? null : $"large files need the collector session, which cannot exist here: {_unavailable}";

        IFileChannel ILargeFileTransport.Channel => Files;

        long ILargeFileTransport.SessionNumber => _connection.SessionsOpened;

        TimeSpan ILargeFileTransport.Backoff(int attempt) => _connection.NextPause(attempt);

        bool? ILargeFileTransport.RegistrationOf(string path) => RegistrationOf(path);

        async Task<LargeFileRegistration> ILargeFileTransport.RegisterAsync(AddOrUpdateSensorRequest request, CancellationToken token)
        {
            var element = HttpRequest<CommandRequestBase>.SerializeElement(request);
            var ack = await _connection.RequestAsync(SessionProtocol.OpCommands, "[" + element + "]", _requestTimeout, token).ConfigureAwait(false);

            if (ack.IsSuccess)
            {
                if (!string.IsNullOrEmpty(ack.Body))
                {
                    try
                    {
                        var errors = JsonSerializer.Deserialize<Dictionary<string, string>>(ack.Body);

                        if (errors != null && errors.TryGetValue(request.Path, out var error))
                            return new LargeFileRegistration(RegistrationOutcome.Refused, error ?? "refused");
                    }
                    catch (JsonException)
                    {
                    }
                }

                return new LargeFileRegistration(RegistrationOutcome.Accepted, null);
            }

            // A 4xx is final for the request (wire contract, "Statuses of the registration routes"); a 5xx or no ack is not.
            if (ack.Acked && ack.Status >= 400 && ack.Status < 500)
                return new LargeFileRegistration(RegistrationOutcome.Refused, $"the server refused the registration with status {ack.Status}: {ack.Error}".TrimEnd(' ', ':'));

            return new LargeFileRegistration(RegistrationOutcome.Unsettled, ack.Acked ? $"status {ack.Status}: {ack.Error}".TrimEnd(' ', ':') : ack.Error);
        }
    }
}
