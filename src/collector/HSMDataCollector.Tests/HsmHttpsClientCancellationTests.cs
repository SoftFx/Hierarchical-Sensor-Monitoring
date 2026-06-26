using HSMDataCollector.Client;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using System;
using System.Threading.Tasks;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// Regression coverage for PR #1080 review finding #7:
    /// <see cref="HsmHttpsClient.CancelPendingRequests"/> must NOT dispose the shared
    /// <see cref="System.Net.Http.HttpClient"/>. The previous implementation cancelled the
    /// in-flight token AND disposed <c>_client</c>, which converted a <c>Dispose()</c> racing
    /// a concurrent graceful <c>Stop()</c> into silent data loss — every remaining send in
    /// the bounded flush threw <see cref="ObjectDisposedException"/>, got re-enqueued, then
    /// discarded by <c>ClearQueue</c>.
    /// </summary>
    public sealed class HsmHttpsClientCancellationTests
    {
        [Fact]
        public async Task CancelPendingRequests_keeps_HttpClient_usable_for_subsequent_requests()
        {
            // No server is listening on this address; any successful request path is impossible.
            // What we care about is the SHAPE of the failure: the bug under test surfaces as
            // ObjectDisposedException ("Cannot access a disposed object"), whereas a healthy
            // post-cancel client returns a connection-level failure instead.
            using (var client = new HsmHttpsClient(CreateOptions(), new LoggerManager()))
            {
                client.CancelPendingRequests();

                var result = await client.TestConnectionAsync().ConfigureAwait(false);

                Assert.False(result.IsOk, "There is no server to connect to; TestConnection must report an error.");
                Assert.False(string.IsNullOrEmpty(result.Error), "Failure result must include an error message.");
                Assert.False(
                    result.Error.IndexOf("disposed", StringComparison.OrdinalIgnoreCase) >= 0,
                    $"CancelPendingRequests must leave the HttpClient usable. Observed disposed-client error: {result.Error}");
            }
        }

        [Fact]
        public async Task CancelPendingRequests_can_be_invoked_repeatedly_without_disposing_client()
        {
            using (var client = new HsmHttpsClient(CreateOptions(), new LoggerManager()))
            {
                client.CancelPendingRequests();
                client.CancelPendingRequests();
                client.CancelPendingRequests();

                var result = await client.TestConnectionAsync().ConfigureAwait(false);

                Assert.False(
                    result.Error?.IndexOf("disposed", StringComparison.OrdinalIgnoreCase) >= 0,
                    $"Repeated CancelPendingRequests must not dispose the client. Error: {result.Error}");
            }
        }

        private static CollectorOptions CreateOptions() => new CollectorOptions
        {
            AccessKey = "cancel-pending-test",
            ClientName = "cancel-pending-test",
            ServerAddress = "127.0.0.1",
            // Port 1 (TCPMUX) is reserved and not bound on any reasonable test host, so the
            // request fails fast at the transport layer instead of waiting for HttpClient.Timeout.
            Port = 1,
            RequestTimeout = TimeSpan.FromSeconds(1),
            AllowPlaintextTransport = true,
        };
    }
}
