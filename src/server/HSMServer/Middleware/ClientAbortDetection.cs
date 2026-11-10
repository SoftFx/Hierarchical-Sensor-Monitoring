using System;
using System.IO;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // One classifier for "the client went away mid-request", shared by the two
    // middlewares that need it: ApiExceptionJsonMiddleware (whether a 500 JSON
    // answer is even writable, #1371) and LoggingExceptionMiddleware (WARN,
    // not ERROR, in HSM-error-log.txt, #1632/ADR-0016). Two signals, either is
    // enough: RequestAborted fired, or the exception is a
    // ConnectionAbortedException — Kestrel does not cancel RequestAborted
    // inline (the callback is queued to the thread pool), so a body read can
    // throw ConnectionAbortedException (an OperationCanceledException
    // subclass) while the token still reports not-cancelled; that exception IS
    // the transport reporting the dead connection. A bare
    // OperationCanceledException with the token intact is NOT a client abort:
    // e.g. an HttpClient timeout inside a handler is an ordinary failure of a
    // live request.
    //
    // The same race exists on the HTTP/2 body read (#1648): Kestrel aborts the
    // request pipe with IOException("The client reset the request stream.")
    // (RST_STREAM) before the token fires, so that IOException — thrown out of
    // the body read, matching Kestrel's constant wording — and an IOException
    // caught while the token is cancelled are client aborts too. Every other
    // IOException (e.g. a failing disk) is an ordinary failure.
    internal static class ClientAbortDetection
    {
        // Kestrel's constant, non-localized wording of the HTTP/2 stream reset
        // (Http2Stream aborts the request body pipe with it).
        private const string ClientResetRequestStream = "The client reset the request stream.";

        public static bool IsClientAbort(Exception exception, HttpContext context) =>
            IsCancellationAbort(exception, context) ||
            IsBodyResetAbort(exception, context);

        private static bool IsCancellationAbort(Exception exception, HttpContext context) =>
            exception is OperationCanceledException &&
            (context.RequestAborted.IsCancellationRequested ||
             exception is ConnectionAbortedException);

        private static bool IsBodyResetAbort(Exception exception, HttpContext context) =>
            exception is IOException &&
            (context.RequestAborted.IsCancellationRequested ||
             exception.InnerException is ConnectionResetException or ConnectionAbortedException ||
             string.Equals(exception.Message, ClientResetRequestStream, StringComparison.Ordinal));
    }
}
