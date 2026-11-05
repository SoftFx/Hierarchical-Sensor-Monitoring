using System;
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
    internal static class ClientAbortDetection
    {
        public static bool IsClientAbort(Exception exception, HttpContext context) =>
            exception is OperationCanceledException &&
            (context.RequestAborted.IsCancellationRequested ||
             exception is ConnectionAbortedException);
    }
}
