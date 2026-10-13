using System.Collections.Generic;

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
        /// not_found (404), conflict (409), unprocessable_entity (422),
        /// internal_error (500), service_unavailable (503).
        /// </summary>
        public string Error { get; init; }

        /// <summary>Human-readable summary; 404 bodies are generic by design (anti-enumeration).</summary>
        public string Message { get; init; }

        /// <summary>
        /// The typed details map (#1371): string keys with per-status value shapes —
        /// field-keyed message arrays ({"field": ["msg"]}) on 400s and 422s,
        /// scalar resource pointers ({"traceId": "..."} on 500s, {"templateId": "..."}
        /// on create-path 409s); null otherwise. Declared Dictionary&lt;string, object&gt;
        /// rather than object so the published schema is a string-keyed map (values
        /// serialize by runtime type — the union is published explicitly by
        /// ManagementApiErrorDetailsSchemaFilter); the three fields are always present.
        /// </summary>
        public Dictionary<string, object> Details { get; init; }
    }
}
