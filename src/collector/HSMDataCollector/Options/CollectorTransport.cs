namespace HSMDataCollector.Core
{
    /// <summary>
    /// How the collector's built-in sender talks to the server
    /// (<see cref="CollectorOptions.Transport"/>).
    /// </summary>
    public enum CollectorTransport
    {
        /// <summary>
        /// One HTTP request per batch to the Sensor API routes. The default.
        /// </summary>
        Http = 0,

        /// <summary>
        /// A collector session: one WebSocket to <c>GET /api/sensors/session</c> on the same
        /// address and port, with every batch a frame the server acknowledges. Opt-in. The
        /// collector then never uses the HTTP routes: it does not fall back to them when a
        /// session cannot be opened, and its values wait in the queues until one can. File
        /// values cannot travel on a session yet and are refused, with an error, where they are
        /// sent. Needs a server with sessions enabled.
        /// </summary>
        Session = 1,
    }
}
