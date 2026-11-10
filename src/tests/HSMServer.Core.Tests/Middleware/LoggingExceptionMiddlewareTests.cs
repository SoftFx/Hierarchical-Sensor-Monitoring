using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Middleware;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit;

namespace HSMServer.Core.Tests.Middleware
{
    // #1632: this middleware owns one arm of the ADR-0016 contract — the other
    // two live in nlog.config (pinned by NlogConfigTests). A client dropping the
    // connection before the response finishes (a polling MCP client abandoning
    // its POST /mcp, ~300 aborts/24h on one install) surfaces here as an
    // OperationCanceledException; logged at ERROR it flooded HSM-error-log.txt
    // (the operator surface) with entries needing no operator attention. The
    // middleware classifies by cause (ClientAbortDetection, shared with
    // ApiExceptionJsonMiddleware): the aborted request token, or the transport
    // itself reporting the dead connection — so the demotion applies to any
    // endpoint, while an internal cancellation (token intact) and
    // non-cancellation exceptions stay errors.
    //
    // The middleware logs through the process-global NLog LogManager, hence the
    // "NLog isolated" collection (DisableParallelization): the configuration is
    // swapped for a MemoryTarget for the class lifetime.
    [Collection("NLog isolated")]
    public sealed class LoggingExceptionMiddlewareTests : IDisposable
    {
        private readonly MemoryTarget _memory = new() { Layout = "${level}|${message}" };
        private readonly LoggingConfiguration _previous = LogManager.Configuration;


        public LoggingExceptionMiddlewareTests()
        {
            LogManager.Setup().LoadConfiguration(cfg => cfg.ForLogger().WriteTo(_memory));
        }

        public void Dispose() => LogManager.Configuration = _previous;


        // The exact production shape: an MCP client drops its POST after the
        // response has started, the transport throws TaskCanceledException, the
        // middleware catches it (the stack in #1632). Warn, not Error — the
        // server already records the cancellation once at INFO on the SDK side.
        [Fact]
        public async Task CancelledRequest_WithStartedResponse_LogsWarnInsteadOfError()
        {
            var context = AbortedContext(method: "POST", path: "/mcp");
            await context.Response.StartAsync();
            var middleware = Middleware(Throwing<TaskCanceledException>());

            await Assert.ThrowsAsync<TaskCanceledException>(() => middleware.InvokeAsync(context));

            Assert.Single(_memory.Logs, entry => entry.StartsWith("Warn|"));
            Assert.DoesNotContain(_memory.Logs, entry => entry.StartsWith("Error|"));
        }

        // Same client disconnect before the response started: the middleware
        // must not require HasStarted to recognize a cancellation.
        [Fact]
        public async Task CancelledRequest_BeforeResponseStarted_LogsWarnInsteadOfError()
        {
            var context = AbortedContext(method: "GET", path: "/api/v1/sensors");
            var middleware = Middleware(Throwing<OperationCanceledException>());

            await Assert.ThrowsAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

            Assert.Single(_memory.Logs, entry => entry.StartsWith("Warn|"));
            Assert.DoesNotContain(_memory.Logs, entry => entry.StartsWith("Error|"));
        }

        // The Kestrel race from #1371: the transport reports the dead
        // connection as ConnectionAbortedException (an OCE subclass) while the
        // request token has not fired yet — same client disconnect, same WARN
        // (the classifier is shared with ApiExceptionJsonMiddleware).
        [Fact]
        public async Task ConnectionAborted_WithLiveRequestToken_LogsWarnInsteadOfError()
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/mcp";
            var middleware = Middleware(_ => throw new ConnectionAbortedException());

            await Assert.ThrowsAsync<ConnectionAbortedException>(() => middleware.InvokeAsync(context));

            Assert.Single(_memory.Logs, entry => entry.StartsWith("Warn|"));
            Assert.DoesNotContain(_memory.Logs, entry => entry.StartsWith("Error|"));
        }

        // An OperationCanceledException with the request token intact is an
        // internal timeout/cancellation, not a client going away — a potential
        // server fault an operator must look at, so it stays an Error.
        [Fact]
        public async Task CancellationWithoutAbortedRequest_StaysError()
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/mcp";
            var middleware = Middleware(Throwing<TaskCanceledException>());

            await Assert.ThrowsAsync<TaskCanceledException>(() => middleware.InvokeAsync(context));

            Assert.Single(_memory.Logs, entry => entry.StartsWith("Error|"));
        }

        // The aborted token alone must not demote anything: a genuine failure
        // racing a client disconnect keeps its Error.
        [Fact]
        public async Task OtherException_OnAbortedRequest_StaysError()
        {
            var context = AbortedContext(method: "POST", path: "/mcp");
            var middleware = Middleware(Throwing<InvalidOperationException>());

            await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

            Assert.Single(_memory.Logs, entry => entry.StartsWith("Error|"));
        }

        // #1648: the HTTP/2 arm of the same client disconnect — Kestrel aborts
        // the request body pipe with IOException("The client reset the
        // request stream.") (RST_STREAM) while the request token has not fired
        // yet (the abort callback is queued). Same client going away, same
        // WARN; the exact Kestrel wording is non-localized.
        [Fact]
        public async Task ClientResetIOException_WithLiveRequestToken_LogsWarnInsteadOfError()
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/mcp";
            var middleware = Middleware(_ => throw new IOException("The client reset the request stream."));

            await Assert.ThrowsAsync<IOException>(() => middleware.InvokeAsync(context));

            Assert.Single(_memory.Logs, entry => entry.StartsWith("Warn|"));
            Assert.DoesNotContain(_memory.Logs, entry => entry.StartsWith("Error|"));
        }

        // An IOException racing an already-aborted request is the client going
        // away too (the token fired first here).
        [Fact]
        public async Task IOException_OnAbortedRequest_LogsWarnInsteadOfError()
        {
            var context = AbortedContext(method: "POST", path: "/mcp");
            var middleware = Middleware(Throwing<IOException>());

            await Assert.ThrowsAsync<IOException>(() => middleware.InvokeAsync(context));

            Assert.Single(_memory.Logs, entry => entry.StartsWith("Warn|"));
            Assert.DoesNotContain(_memory.Logs, entry => entry.StartsWith("Error|"));
        }

        // Every other IOException (a failing disk, a broken pipe of ours) is an
        // ordinary failure an operator must look at — it stays an Error.
        [Fact]
        public async Task OtherIOException_StaysError()
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/mcp";
            var middleware = Middleware(_ => throw new IOException("Disk full."));

            await Assert.ThrowsAsync<IOException>(() => middleware.InvokeAsync(context));

            Assert.Single(_memory.Logs, entry => entry.StartsWith("Error|"));
        }


        private static LoggingExceptionMiddleware Middleware(RequestDelegate next) =>
            new(next, new Mock<IServerConfig>().Object);

        private static RequestDelegate Throwing<TException>() where TException : Exception, new() =>
            _ => throw new TException();

        private static DefaultHttpContext AbortedContext(string method, string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path;
            // An already-cancelled token without a CancellationTokenSource to
            // leak: the token is a value the middleware reads, never a source
            // it disposes.
            context.RequestAborted = new CancellationToken(canceled: true);
            return context;
        }
    }
}
