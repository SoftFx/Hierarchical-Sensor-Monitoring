namespace HSMDataCollector.Core
{
    /// <summary>
    /// How the collector's built-in sender talks to the server
    /// (<see cref="CollectorOptions.Transport"/>).
    /// </summary>
    public enum CollectorTransport
    {
        /// <summary>
        /// One HTTP request per batch to the Sensor API routes. The default before 4.0.0; choose
        /// it for a server without sessions or a proxy that does not pass WebSockets.
        /// </summary>
        Http = 0,

        /// <summary>
        /// A collector session: one WebSocket to <c>GET /api/sensors/session</c> on the same
        /// address and port, with every batch a frame the server acknowledges. The default since
        /// 4.0.0. The collector then never uses the HTTP routes: it does not fall back to them
        /// when a session cannot be opened, and its values wait in the queues until one can. A
        /// file value larger than the server's frame limit is dropped and reported. Needs a
        /// server with sessions enabled (HSMServer 3.43.0 or later).
        /// </summary>
        Session = 1,
    }
}
