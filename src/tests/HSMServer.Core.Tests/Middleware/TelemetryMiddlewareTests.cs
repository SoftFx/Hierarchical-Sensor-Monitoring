using System.IO;
using System.Threading.Tasks;
using HSMServer.Middleware.Telemetry;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace HSMServer.Core.Tests.Middleware
{
    // #1649: the telemetry middleware owns the public-API gate. A rejected
    // key must answer one complete 406 body here, before MVC runs (next is
    // never called — nothing later formats a result over the written
    // response); and a request with NO key header at all must continue down
    // the pipeline — the registration briefly answered false for it too,
    // which cut the whole web UI off with empty 200s (found in review).
    public sealed class TelemetryMiddlewareTests
    {
        [Fact]
        public async Task RequestWithoutKeyHeader_ContinuesDownThePipeline()
        {
            var nextCalled = false;
            var middleware = new TelemetryMiddleware(
                _ => { nextCalled = true; return Task.CompletedTask; }, null, null);

            var context = new DefaultHttpContext();

            await middleware.InvokeAsync(context);

            Assert.True(nextCalled);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        [Fact]
        public async Task RejectedKey_AnswersComplete406Body_AndStopsThePipeline()
        {
            var nextCalled = false;
            var middleware = new StubbedTelemetryMiddleware(
                _ => { nextCalled = true; return Task.CompletedTask; },
                ("Key is invalid.", result: false));

            var context = new DefaultHttpContext();
            context.Request.Headers["Key"] = "00000000-1111-2222-3333-444444444444";
            // The default response body is Stream.Null; give it a real one
            // so the 406 body is readable.
            context.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(406, context.Response.StatusCode);

            var body = Assert.IsType<MemoryStream>(context.Response.Body);
            Assert.Equal("Key is invalid.", System.Text.Encoding.UTF8.GetString(body.ToArray()));
        }


        // The registration decision is stubbed: the real collector resolves
        // the key through the cache AND the self-monitoring sensor stack
        // (DataCollectorWrapper), which a unit test cannot stand up. The
        // unknown-key -> "Key is invalid." half of the chain is pinned by
        // AccessKeyTests.TryGetKeyTest_UnknownKeyAnswersKeyIsInvalid.
        private sealed class StubbedTelemetryMiddleware : TelemetryMiddleware
        {
            private readonly (string Error, bool Result) _registration;

            public StubbedTelemetryMiddleware(RequestDelegate next, (string Error, bool Result) registration)
                : base(next, null, null) => _registration = registration;

            public override bool TryRegisterPublicApiRequest(HttpContext context, out string error)
            {
                error = _registration.Error;

                return _registration.Result;
            }
        }
    }
}
