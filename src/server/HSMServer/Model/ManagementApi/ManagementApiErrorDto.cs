namespace HSMServer.Model.ManagementApi
{
    /// <summary>
    /// The uniform error body of the management API: every error response of every
    /// /api/v1 endpoint — controller results, authentication challenges, area-guard
    /// rejections and the /api exception handler — carries this shape with
    /// application/json, never HTML and never an empty body.
    /// </summary>
    public sealed record ManagementApiErrorDto
    {
        /// <summary>
        /// Stable machine-readable code, one per HTTP status:
        /// validation_failed (400), unauthorized (401), forbidden (403),
        /// not_found (404), conflict (409), internal_error (500).
        /// </summary>
        public string Error { get; init; }

        /// <summary>Human-readable summary; 404 bodies are generic by design (anti-enumeration).</summary>
        public string Message { get; init; }

        /// <summary>Field-keyed validation messages ({"field": ["msg"]}) on 400s; {"traceId": "..."} on 500s; null otherwise.</summary>
        public object Details { get; init; }
    }
}
