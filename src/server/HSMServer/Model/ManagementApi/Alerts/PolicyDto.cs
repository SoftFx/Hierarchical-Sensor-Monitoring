using System;
using System.Collections.Generic;
using System.ComponentModel;

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
    [Description("One data-alert policy: conditions, destination, notification, effects. Enum-valued fields carry the DOMAIN ENUM NAME as a string (value tables on each field). Writes are FULL-REPLACE: fields left out fall back to their defaults. A policy must carry at least one effect: a notification, an icon, or a status change.")]
    public sealed record PolicyDto
    {
        /// <summary>Policy id; assigned by the server on create, taken from the route on update. Input values are ignored.</summary>
        [Description("Policy id; assigned by the server on create, taken from the route on update. Input values are ignored.")]
        public Guid Id { get; init; }

        /// <summary>Conditions combined into the policy trigger (1..10 entries, evaluated top to bottom).</summary>
        [Description("Conditions combined into the policy trigger (1..10 entries, evaluated top to bottom).")]
        public List<AlertConditionDto> Conditions { get; init; } = [];

        /// <summary>
        /// Where a fired notification goes. Required when <c>notification</c> is present.
        /// Mode table: FromParent, Empty, AllChats, Custom, NotInitialized (NotInitialized = never configured; writable only as an echo of a read, never with chats).
        /// </summary>
        [Description("Where a fired notification goes. Required when `notification` is present.")]
        public AlertDestinationDto Destination { get; init; }

        /// <summary>The notification effect (message template + send schedule); null means no notification.</summary>
        [Description("The notification effect (message template + send schedule); null means no notification.")]
        public AlertNotificationDto Notification { get; init; }

        /// <summary>Icon shown on the sensor while the policy is triggered (e.g. "🔥"); null or empty means none.</summary>
        [Description("Icon shown on the sensor while the policy is triggered (e.g. \"🔥\"); null or empty means none.")]
        public string Icon { get; init; }

        /// <summary>
        /// Status effect while the policy is triggered: "Error" sets the sensor status to Error,
        /// "Ok" or null means no status action.
        /// </summary>
        [Description("Status effect while the policy is triggered: \"Error\" sets the sensor status to Error; \"Ok\" or null means no status action.")]
        public string Status { get; init; }

        /// <summary>Confirmation period as a TimeSpan string (e.g. "00:05:00"); null disables confirmation.</summary>
        [Description("Confirmation period as a TimeSpan string (e.g. \"00:05:00\"); null disables confirmation (the policy must stay triggered this long before firing).")]
        public string ConfirmationPeriod { get; init; }

        /// <summary>Disabled policies stay stored but never fire.</summary>
        [Description("Disabled policies stay stored but never fire.")]
        public bool IsDisabled { get; init; }

        /// <summary>Working-time schedule the policy respects; must reference an existing /api/v1/alertSchedules id; null = always.</summary>
        [Description("Working-time schedule the policy respects; must reference an existing alert-schedule id (see list_alert_schedules); null = always.")]
        public Guid? ScheduleId { get; init; }

        /// <summary>Output-only, server-owned: the alert template this policy was minted by, when any. Values in request bodies are ignored.</summary>
        [Description("Output-only, server-owned: the alert template this policy was minted by, when any. Values sent in request bodies are ignored; a template-owned policy accepts only the isDisabled toggle (anything else answers 409).")]
        public Guid? TemplateId { get; init; }

        /// <summary>Output-only, server-owned: the template alert entry this policy was minted by, when any. Values in request bodies are ignored.</summary>
        [Description("Output-only, server-owned: the template alert entry this policy was minted by, when any. Values sent in request bodies are ignored.")]
        public Guid? TemplateAlertId { get; init; }
    }

    /// <summary>
    /// One TTL (inactivity) policy (#1500): fires when the node receives no data
    /// within the interval. The interval is expressed EXPLICITLY — the client never
    /// uses null to mean "reset": <c>inherit</c>=true resets to the parent's
    /// interval, <c>interval</c> sets an explicit one, and exactly one of the two
    /// is required on every write.
    /// </summary>
    [Description("One TTL (inactivity) policy: fires when the node receives no data within the interval. Exactly one of `interval` or `inherit` is required on every write — never both, never neither.")]
    public sealed record TtlPolicyDto
    {
        /// <summary>Policy id; assigned by the server on create, taken from the route on update. Input values are ignored.</summary>
        [Description("Policy id; assigned by the server on create, taken from the route on update. Input values are ignored.")]
        public Guid Id { get; init; }

        /// <summary>
        /// Inactivity interval as a TimeSpan string (e.g. "00:30:00"); null exactly when
        /// <c>inherit</c> is true (reads: also null for a policy resolving to no interval anywhere).
        /// </summary>
        [Description("Inactivity interval as a TimeSpan string (e.g. \"00:30:00\"); null exactly when `inherit` is true.")]
        public string Interval { get; init; }

        /// <summary>True = take the interval from the parent node (the explicit reset-to-parent switch; on a product it resolves against the product's own Inactivity Period setting).</summary>
        [Description("True = take the interval from the parent node (the explicit reset-to-parent switch; on a product it resolves against the product's own Inactivity Period setting).")]
        public bool Inherit { get; init; }

        /// <summary>Where a fired notification goes. Required when <c>notification</c> is present. Mode table: FromParent, Empty, AllChats, Custom, NotInitialized (an echo of an unconfigured destination; never with chats).</summary>
        [Description("Where a fired notification goes. Required when `notification` is present.")]
        public AlertDestinationDto Destination { get; init; }

        /// <summary>The notification effect (message template + send schedule); null means no notification.</summary>
        [Description("The notification effect (message template + send schedule); null means no notification.")]
        public AlertNotificationDto Notification { get; init; }

        /// <summary>Icon shown on the node while the policy is triggered; null or empty means none.</summary>
        [Description("Icon shown on the node while the policy is triggered; null or empty means none.")]
        public string Icon { get; init; }

        /// <summary>Status effect while the policy is triggered: "Error", or "Ok"/null for no status action.</summary>
        [Description("Status effect while the policy is triggered: \"Error\", or \"Ok\"/null for no status action.")]
        public string Status { get; init; }

        /// <summary>Disabled policies stay stored but never fire.</summary>
        [Description("Disabled policies stay stored but never fire.")]
        public bool IsDisabled { get; init; }

        /// <summary>Working-time schedule the policy respects (the TTL schedule gate, #1404); must reference an existing schedule id; null = always.</summary>
        [Description("Working-time schedule the policy respects; must reference an existing alert-schedule id (see list_alert_schedules); null = always.")]
        public Guid? ScheduleId { get; init; }

        /// <summary>Output-only, server-owned: the alert template this policy was minted by, when any. Values in request bodies are ignored.</summary>
        [Description("Output-only, server-owned: the alert template this policy was minted by, when any. Values sent in request bodies are ignored; a template-owned policy accepts only the isDisabled toggle (anything else answers 409).")]
        public Guid? TemplateId { get; init; }

        /// <summary>Output-only, server-owned: the template alert entry this policy was minted by, when any. Values in request bodies are ignored.</summary>
        [Description("Output-only, server-owned: the template alert entry this policy was minted by, when any. Values sent in request bodies are ignored.")]
        public Guid? TemplateAlertId { get; init; }
    }

    /// <summary>
    /// One condition of a policy: property, comparison operation, typed target.
    /// The property/operation pair must be one the TARGET SENSOR'S TYPE offers
    /// (the same surface the web editor renders) — a mismatch answers 422.
    /// </summary>
    [Description("One condition of a policy: property, comparison operation, typed target. The property/operation pair must be one the TARGET SENSOR'S TYPE offers — a mismatch answers 422.")]
    public sealed record AlertConditionDto
    {
        /// <summary>
        /// The sensor property compared (PolicyProperty enum name).
        /// Common to every type: Status, Comment, NewSensorData.
        /// Value types: Value; String adds Length; File adds OriginalSize; bar types add
        /// Min, Max, Mean, Count, FirstValue, LastValue, EmaMin, EmaMax, EmaMean, EmaCount;
        /// numeric single-value types add EmaValue.
        /// </summary>
        [Description("The sensor property compared (PolicyProperty enum name). Common to every type: Status, Comment, NewSensorData. Value types: Value; String adds Length; File adds OriginalSize; bar types add Min, Max, Mean, Count, FirstValue, LastValue, EmaMin, EmaMax, EmaMean, EmaCount; numeric single-value types add EmaValue.")]
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
        [Description("Comparison operation (PolicyOperation enum name). Operation table per property: Status: IsChanged, IsChangedToOk, IsChangedToError, IsOk, IsError (no target); Comment: Equal, NotEqual, Contains, StartsWith, EndsWith, IsChanged (no target for IsChanged); NewSensorData: ReceivedNewValue (no target); numeric properties (Value/Ema/Min/Max/Mean/Count/FirstValue/LastValue/Length/OriginalSize): LessThanOrEqual, LessThan, GreaterThan, GreaterThanOrEqual, Equal, NotEqual; String/Version Value: Equal, NotEqual, Contains, StartsWith, EndsWith.")]
        public string Operation { get; init; }

        /// <summary>
        /// The constant compared against — a typed JSON value whose type follows the property:
        /// a JSON number for numeric properties (integer properties require an integer),
        /// a string for Comment and String Value, a TimeSpan string ("d.hh:mm:ss") for TimeSpan Value,
        /// a version string ("1.2.3") for Version Value. Target-less operations (the table above)
        /// take null; anything else is a 422.
        /// </summary>
        [Description("The constant compared against — a typed JSON value whose type follows the property: a JSON number for numeric properties (integer properties require an integer), a string for Comment and String Value, a TimeSpan string (\"d.hh:mm:ss\") for TimeSpan Value, a version string (\"1.2.3\") for Version Value. Target-less operations (see the operation table) take null.")]
        public object Target { get; init; }

        /// <summary>How this condition combines with the PREVIOUS one: "And" (default) or "Or"; ignored on the first condition.</summary>
        [Description("How this condition combines with the PREVIOUS one: \"And\" (default) or \"Or\"; ignored on the first condition.")]
        public string Combination { get; init; }
    }

    /// <summary>The notification destination of a policy.</summary>
    [Description("The notification destination of a policy.")]
    public sealed record AlertDestinationDto
    {
        /// <summary>Destination mode (PolicyDestinationMode enum name): FromParent, Empty, AllChats, Custom. Table note: NotInitialized marks an unconfigured destination and is writable only as an echo of a read, never with chats.</summary>
        [Description("Destination mode (PolicyDestinationMode enum name): FromParent (the parent node's routing), Empty (no destination), AllChats (every chat bound to the folder), Custom (the `chats` list). NotInitialized marks an unconfigured destination and is writable only as an echo of a read, never with chats.")]
        public string Mode { get; init; }

        /// <summary>Destination chat ids for mode Custom (1..20, every id must be available on the node — folder-bound or global); must be empty for the other modes.</summary>
        [Description("Destination chat ids for mode Custom (1..20, every id must be available on the node — folder-bound or global; use list_chats to discover them); must be empty for the other modes.")]
        public List<Guid> Chats { get; init; } = [];
    }

    /// <summary>The notification effect of a policy: what is sent and on which cadence.</summary>
    [Description("The notification effect of a policy: what is sent and on which cadence.")]
    public sealed record AlertNotificationDto
    {
        /// <summary>Message template, may reference alert variables ($product, $path, $status, $comment, $operation, $target, $prevStatus).</summary>
        [Description("Message template, may reference alert variables ($product, $path, $status, $comment, $operation, $target, $prevStatus).")]
        public string Template { get; init; }

        /// <summary>Re-send cadence of a still-triggered policy (AlertRepeatMode enum name). Repeat table: Immediately (default), FiveMinutes, TenMinutes, FifteenMinutes, ThirtyMinutes, Hourly, Daily, Weekly.</summary>
        [Description("Re-send cadence of a still-triggered policy (AlertRepeatMode enum name): Immediately (default), FiveMinutes, TenMinutes, FifteenMinutes, ThirtyMinutes, Hourly, Daily, Weekly.")]
        public string Repeat { get; init; }

        /// <summary>Send the first notification immediately, outside the aggregation window.</summary>
        [Description("Send the first notification immediately, outside the aggregation window.")]
        public bool InstantSend { get; init; }

        /// <summary>When the repeat cadence starts (UTC ISO 8601); null = at once.</summary>
        [Description("When the repeat cadence starts (UTC ISO 8601); null = at once.")]
        public DateTime? StartAt { get; init; }
    }

    /// <summary>
    /// A data policy as seen from a PRODUCT (#1500): the product's sensor-tree
    /// aggregate, not an owned list — a product holds no data policies of its own
    /// (per-node alert creation was removed in #1142); each item names the sensor
    /// that owns it, and writes route to that sensor.
    /// </summary>
    [Description("A data policy as seen from a PRODUCT: the product's sensor-tree aggregate — each item names the sensor that owns it, and writes route to that sensor.")]
    public sealed record ProductPolicyDto
    {
        /// <summary>The sensor that owns this policy.</summary>
        [Description("The sensor that owns this policy.")]
        public Guid SensorId { get; init; }

        /// <summary>Full path of the owning sensor.</summary>
        [Description("Full path of the owning sensor.")]
        public string SensorPath { get; init; }

        /// <summary>The policy itself.</summary>
        [Description("The policy itself.")]
        public PolicyDto Policy { get; init; }
    }
}
