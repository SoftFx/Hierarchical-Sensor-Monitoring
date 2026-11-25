using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client.HttpsClient;
using HSMDataCollector.Tables;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMTableBlocks;

namespace HSMDataCollector.Client.Session
{
    /// <summary>
    /// The table sensors on the collector session (ADR-0021 point 7): their registrations travel as a <c>commands</c>
    /// request of their own, never mixed with the registrations of other sensors, and the per-path errors of its ack say
    /// which were refused. Blocks go on the table connection of the session (#1846 part 2): until it exists here, a block
    /// is not delivered and stays in the collector's backlog.
    /// </summary>
    internal sealed partial class SessionDataSender : ITableBlockChannel
    {
        /// <summary>The frame limit the server announces by default (wire contract, "Table connection").</summary>
        private const int DefaultTableFrameBytes = 4 * 1024 * 1024;

        string ITableBlockChannel.TablesUnavailableReason =>
            _unavailable == null ? null : $"table sensors need the collector session, which cannot exist here: {_unavailable}";

        int ITableBlockChannel.MaxBlockBytes => DefaultTableFrameBytes - TableBlockFormatSizes.FrameHeaderSize;

        TableBlockLimits ITableBlockChannel.BlockLimits => TableBlockLimits.Default;

        Task ITableBlockChannel.WaitUntilReadyAsync(CancellationToken token) => _connection.WaitUntilUpAsync(token);

        Task<TableBlockAck> ITableBlockChannel.PutBlockAsync(Guid producer, long sequence, byte[] block, CancellationToken token) =>
            Task.FromResult(TableBlockAck.NotDelivered("the table connection of the collector session is not in this build yet (#1846)"));

        async Task<TableRegistrationResult> ITableBlockChannel.RegisterTablesAsync(IReadOnlyList<AddOrUpdateSensorRequest> registrations, CancellationToken token)
        {
            var room = _connection.MaxBodyBytes;

            if (room == 0)
                return TableRegistrationResult.NotSent(_connection.DownReason);

            var accepted = new List<string>();
            var refused = new Dictionary<string, string>(StringComparer.Ordinal);
            string unsettled = null;
            var index = 0;

            while (index < registrations.Count)
            {
                var body = new StringBuilder("[");
                var bodyBytes = 2;
                var paths = new List<string>();

                while (index < registrations.Count)
                {
                    var element = HttpRequest<CommandRequestBase>.SerializeElement(registrations[index]);
                    var bytes = Encoding.UTF8.GetByteCount(element);

                    // A table registration is a few hundred bytes; one that does not fit a frame on its own is refused
                    // here, for good, as the server would refuse a frame larger than its limit.
                    if (bytes + 2 > room)
                    {
                        refused[registrations[index].Path] = $"its registration of {bytes} bytes is larger than a collector session frame ({room} bytes)";
                        index++;
                        continue;
                    }

                    if (bodyBytes + bytes + (paths.Count == 0 ? 0 : 1) > room)
                        break;

                    if (paths.Count != 0)
                    {
                        body.Append(',');
                        bodyBytes++;
                    }

                    body.Append(element);
                    bodyBytes += bytes;
                    paths.Add(registrations[index].Path);
                    index++;
                }

                if (paths.Count == 0)
                    continue;

                body.Append(']');

                var ack = await _connection.RequestAsync(SessionProtocol.OpCommands, body.ToString(), _requestTimeout, token).ConfigureAwait(false);

                if (ack.IsSuccess)
                {
                    var errors = ReadPathErrors(ack.Body);

                    foreach (var path in paths)
                    {
                        if (errors.TryGetValue(path, out var error))
                            refused[path] = error;
                        else
                            accepted.Add(path);
                    }

                    continue;
                }

                // A 4xx is final for the whole request (wire contract, "Statuses of the registration routes"); a 5xx or
                // no ack leaves the paths of this frame unsettled, to be sent again.
                if (ack.Acked && ack.Status >= 400 && ack.Status < 500)
                {
                    foreach (var path in paths)
                        refused[path] = $"the server refused the registration request with status {ack.Status}: {ack.Error}".TrimEnd(' ', ':');

                    continue;
                }

                // The paths of this frame stay unsettled; the frames after it are tried all the same, so one path
                // that faults on every repeat does not hold the healthy ones back (#1846 review, item 8).
                unsettled = ack.Acked ? $"status {ack.Status}: {ack.Error}".TrimEnd(' ', ':') : ack.Error;
            }

            return new TableRegistrationResult(accepted, refused, unsettled);
        }

        private Dictionary<string, string> ReadPathErrors(string body)
        {
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);

            if (string.IsNullOrEmpty(body))
                return errors;

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(body);

                if (parsed != null)
                    foreach (var error in parsed.Where(e => e.Key != null))
                        errors[error.Key] = error.Value ?? "refused";
            }
            catch (JsonException)
            {
                _logger?.Debug("The body of a commands ack is not a path-to-error dictionary.");
            }

            return errors;
        }
    }
}
