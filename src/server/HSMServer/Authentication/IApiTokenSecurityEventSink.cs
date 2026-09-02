namespace HSMServer.Authentication
{
    // Append-only per-request security-event sink of the API-token channel: the request
    // path enqueues and moves on; a single background writer drains to durable storage.
    // Volume control: successful authentications are sampled (the success rate is the
    // operational signal, not every request); failures and authorization denials are
    // always recorded. Loss bounds are explicit: a full queue or a failed write drops the
    // event (counted and logged) — security events must never block or fail a request.
    public interface IApiTokenSecurityEventSink
    {
        void Record(ApiTokenSecurityEvent @event);

        // Events dropped before reaching durable storage (queue full or write failure).
        long DroppedCount { get; }
    }
}
