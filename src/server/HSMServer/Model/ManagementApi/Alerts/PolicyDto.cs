using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.Alerts
{
    /// <summary>
    /// One data-alert policy of the alert-administration surface (#1500) — the
    /// clean wire contract of a <c>Policy</c>, NOT the UI view models and NOT the
    /// durable entity. Ids are Guid strings; enum-valued fields carry the DOMAIN
    /// ENUM NAME as a string (value tables in the field remarks). Writes are
    /// FULL-REPLACE at item granularity: a POST/PATCH body fully specifies the
    /// policy's desired content — fields left out fall back to their defaults, and
    /// the server generates/keeps the id (route wins on PATCH).
    /// </summary>
    /// <remarks>
    /// Round-trip notes: <c>templateId</c>/<c>templateAlertId</c> are server-owned,
    /// output-only — values sent in request bodies are ignored (create always writes
    /// null, update carries the stored linkage; template linkage is managed by the
    /// alert-templates surface); a template-owned policy accepts only the
    /// <c>isDisabled</c> toggle through this API — anything else answers 409, and so
    /// does its deletion (edit the template instead). A policy must carry at least
    /// one effect: a notification, an icon, or a status change.
    /// </remarks>
    public sealed record PolicyDto
    {
        /// <summary>Policy id; assigned by the server on create, taken from the route on update. Input values are ignored.</summary>
        public Guid Id { get; init; }

        /// <summary>Conditions combined into the policy trigger (1..10 entries, evaluated top to bottom).</summary>
        public List<AlertConditionDto> Conditions { get; init; } = [];

        /// <summary>
        /// Where a fired notification goes. Required when <c>notification</c> is present.
        /// Mode table: FromParent, Empty, AllChats, Custom, NotInitialized (NotInitialized = never configured; writable only as an echo of a read, never with chats).
        /// </summary>
        public AlertDestinationDto Destination { get; init; }

        /// <summary>The notification effect (message template + send schedule); null means no notification.</summary>
        public AlertNotificationDto Notification { get; init; }

        /// <summary>Icon shown on the sensor while the policy is triggered (e.g. "🔥"); null or empty means none.</summary>
        public string Icon { get; init; }

        /// <summary>
        /// Status effect while the policy is triggered: "Error" sets the sensor status to Error,
        /// "Ok" or null means no status action.
        /// </summary>
        public string Status { get; init; }

        /// <summary>Confirmation period as a TimeSpan string (e.g. "00:05:00"); null disables confirmation.</summary>
        public string ConfirmationPeriod { get; init; }

        /// <summary>Disabled policies stay stored but never fire.</summary>
        public bool IsDisabled { get; init; }

        /// <summary>Working-time schedule the policy respects; must reference an existing /api/v1/alertSchedules id; null = always.</summary>
        public Guid? ScheduleId { get; init; }

        /// <summary>Output-only, server-owned: the alert template this policy was minted by, when any. Values in request bodies are ignored.</summary>
        public Guid? TemplateId { get; init; }

        /// <summary>Output-only, server-owned: the template alert entry this policy was minted by, when any. Values in request bodies are ignored.</summary>
        public Guid? TemplateAlertId { get; init; }
    }

    /// <summary>
    /// One TTL (inactivity) policy (#1500): fires when the node receives no data
    /// within the interval. The interval is expressed EXPLICITLY — the client never
    /// uses null to mean "reset": <c>inherit</c>=true resets to the parent's
    /// interval, <c>interval</c> sets an explicit one, and exactly one of the two
    /// is required on every write.
    /// </summary>
    public sealed record TtlPolicyDto
    {
        /// <summary>Policy id; assigned by the server on create, taken from the route on update. Input values are ignored.</summary>
        public Guid Id { get; init; }

        /// <summary>
        /// Inactivity interval as a TimeSpan string (e.g. "00:30:00"); null exactly when
        /// <c>inherit</c> is true (reads: also null for a policy resolving to no interval anywhere).
        /// </summary>
        public string Interval { get; init; }

        /// <summary>True = take the interval from the parent node (the explicit reset-to-parent switch; on a product it resolves against the product's own Inactivity Period setting).</summary>
        public bool Inherit { get; init; }

        /// <summary>Where a fired notification goes. Required when <c>notification</c> is present. Mode table: FromParent, Empty, AllChats, Custom, NotInitialized (an echo of an unconfigured destination; never with chats).</summary>
        public AlertDestinationDto Destination { get; init; }

        /// <summary>The notification effect (message template + send schedule); null means no notification.</summary>
        public AlertNotificationDto Notification { get; init; }

        /// <summary>Icon shown on the node while the policy is triggered; null or empty means none.</summary>
        public string Icon { get; init; }

        /// <summary>Status effect while the policy is triggered: "Error", or "Ok"/null for no status action.</summary>
        public string Status { get; init; }

        /// <summary>Disabled policies stay stored but never fire.</summary>
        public bool IsDisabled { get; init; }

        /// <summary>Working-time schedule the policy respects (the TTL schedule gate, #1404); must reference an existing schedule id; null = always.</summary>
        public Guid? ScheduleId { get; init; }

        /// <summary>Output-only, server-owned: the alert template this policy was minted by, when any. Values in request bodies are ignored.</summary>
        public Guid? TemplateId { get; init; }

        /// <summary>Output-only, server-owned: the template alert entry this policy was minted by, when any. Values in request bodies are ignored.</summary>
        public Guid? TemplateAlertId { get; init; }
    }

    /// <summary>
    /// One condition of a policy: property, comparison operation, typed target.
    /// The property/operation pair must be one the TARGET SENSOR'S TYPE offers
    /// (the same surface the web editor renders) — a mismatch answers 422.
    /// </summary>
    public sealed record AlertConditionDto
    {
        /// <summary>
        /// The sensor property compared (PolicyProperty enum name).
        /// Common to every type: Status, Comment, NewSensorData.
        /// Value types: Value; String adds Length; File adds OriginalSize; bar types add
        /// Min, Max, Mean, Count, FirstValue, LastValue, EmaMin, EmaMax, EmaMean, EmaCount;
        /// numeric single-value types add EmaValue.
        /// </summary>
        public string Property { get; init; }

        /// <summary>
        /// Comparison operation (PolicyOperation enum name). Operation table per property:
        /// Status: IsChanged, IsChangedToOk, IsChangedToError, IsOk, IsError (no target);
        /// Comment: Equal, NotEqual, Contains, StartsWith, EndsWith, IsChanged (no target for IsChanged);
        /// NewSensorData: ReceivedNewValue (no target);
        /// numeric properties (Value/Ema/Min/Max/Mean/Count/FirstValue/LastValue/Length/OriginalSize):
        /// LessThanOrEqual, LessThan, GreaterThan, GreaterThanOrEqual, Equal, NotEqual;
        /// String/Version Value: Equal, NotEqual, Contains, StartsWith, EndsWith.
        /// </summary>
        public string Operation { get; init; }

        /// <summary>
        /// The constant compared against — a typed JSON value whose type follows the property:
        /// a JSON number for numeric properties (integer properties require an integer),
        /// a string for Comment and String Value, a TimeSpan string ("d.hh:mm:ss") for TimeSpan Value,
        /// a version string ("1.2.3") for Version Value. Target-less operations (the table above)
        /// take null; anything else is a 422.
        /// </summary>
        public object Target { get; init; }

        /// <summary>How this condition combines with the PREVIOUS one: "And" (default) or "Or"; ignored on the first condition.</summary>
        public string Combination { get; init; }
    }

    /// <summary>The notification destination of a policy.</summary>
    public sealed record AlertDestinationDto
    {
        /// <summary>Destination mode (PolicyDestinationMode enum name): FromParent, Empty, AllChats, Custom. Table note: NotInitialized marks an unconfigured destination and is writable only as an echo of a read, never with chats.</summary>
        public string Mode { get; init; }

        /// <summary>Destination chat ids for mode Custom (1..20, every id must be available on the node — folder-bound or global); must be empty for the other modes.</summary>
        public List<Guid> Chats { get; init; } = [];
    }

    /// <summary>The notification effect of a policy: what is sent and on which cadence.</summary>
    public sealed record AlertNotificationDto
    {
        /// <summary>Message template, may reference alert variables ($product, $path, $status, $comment, $operation, $target, $prevStatus).</summary>
        public string Template { get; init; }

        /// <summary>Re-send cadence of a still-triggered policy (AlertRepeatMode enum name). Repeat table: Immediately (default), FiveMinutes, TenMinutes, FifteenMinutes, ThirtyMinutes, Hourly, Daily, Weekly.</summary>
        public string Repeat { get; init; }

        /// <summary>Send the first notification immediately, outside the aggregation window.</summary>
        public bool InstantSend { get; init; }

        /// <summary>When the repeat cadence starts (UTC ISO 8601); null = at once.</summary>
        public DateTime? StartAt { get; init; }
    }

    /// <summary>
    /// A data policy as seen from a PRODUCT (#1500): the product's sensor-tree
    /// aggregate, not an owned list — a product holds no data policies of its own
    /// (per-node alert creation was removed in #1142); each item names the sensor
    /// that owns it, and writes route to that sensor.
    /// </summary>
    public sealed record ProductPolicyDto
    {
        /// <summary>The sensor that owns this policy.</summary>
        public Guid SensorId { get; init; }

        /// <summary>Full path of the owning sensor.</summary>
        public string SensorPath { get; init; }

        /// <summary>The policy itself.</summary>
        public PolicyDto Policy { get; init; }
    }
}
