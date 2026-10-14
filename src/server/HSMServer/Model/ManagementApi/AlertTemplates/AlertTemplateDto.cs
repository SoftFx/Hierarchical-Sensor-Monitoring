using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using HSMServer.Model.ManagementApi.Alerts;

namespace HSMServer.Model.ManagementApi.AlertTemplates
{
    /// <summary>
    /// Alert template wire contract of the /api/v1/alertTemplates surface. The shape
    /// mirrors the durable template one field for field; ids are Guid strings. Byte
    /// fields carry DOMAIN ENUM VALUES (documented per field) — membership is validated
    /// structurally, unknown values answer 400 with the offending field key.
    /// </summary>
    /// <remarks>
    /// Round-trip notes: a client-sent template id is IGNORED on create (server
    /// generates one) and must equal the route id on update or be absent; Guid.Empty
    /// policy ids are regenerated server-side on every write; destination chat display
    /// names are overwritten with the manager's current names. `destination`/`schedule`
    /// null means "omitted" (defaults apply). TTL policies and intervals are parallel
    /// lists matched by position. `warnings` is output-only (apply diagnostics of the
    /// create/update response; ignored on writes, absent on reads).
    /// </remarks>
    [Description("Alert template: path patterns, sensor type, regular and TTL policies with destinations. Byte fields carry DOMAIN ENUM VALUES (documented per field) — unknown values answer 400.")]
    public sealed record AlertTemplateDto
    {
        /// <summary>Output-only: assigned by the server on create. On update it must equal the route id or be absent.</summary>
        [Description("Output-only: assigned by the server on create. On update it must equal the route id or be absent.")]
        public Guid Id { get; init; }

        /// <summary>Unique name, 1..200 characters, case-sensitive, globally unique across folders.</summary>
        [Description("Unique name, 1..200 characters, case-sensitive, globally unique across folders.")]
        public string Name { get; init; }

        /// <summary>
        /// SensorType enum value, or 100 (AnyType) to match every sensor type:
        /// 0=Boolean, 1=Integer, 2=Double, 3=String, 4=IntegerBar, 5=DoubleBar, 6=File,
        /// 7=TimeSpan, 8=Version, 9=Rate, 10=Enum, 100=AnyType.
        /// </summary>
        [Description("SensorType enum value the template matches: 0=Boolean, 1=Integer, 2=Double, 3=String, 4=IntegerBar, 5=DoubleBar, 6=File, 7=TimeSpan, 8=Version, 9=Rate, 10=Enum, 100=AnyType (match every sensor type).")]
        public byte SensorType { get; init; }

        /// <summary>Target folder id; required on create and update.</summary>
        [Description("Target folder id (the access-grouping Folder entity, not a tree node); required on create and update. Use list_folders to discover valid values.")]
        public Guid FolderId { get; init; }

        /// <summary>Path templates the template applies to (e.g. "Root/*/cpu"); 1..100 entries, each a sensor path pattern.</summary>
        [Description("Path patterns the template applies to (e.g. \"Root/*/cpu\"; '*' matches one path segment); 1..100 entries.")]
        public List<string> Paths { get; init; } = [];

        /// <summary>TTL alert policies; parallel to <see cref="Ttls"/> (same length, matched by position).</summary>
        [Description("TTL (inactivity) alert policies; parallel to `ttls` — same length, matched by position (the interval of ttls[i] belongs to ttlPolicies[i]).")]
        public List<AlertPolicyDto> TtlPolicies { get; init; } = [];

        /// <summary>TTL intervals; parallel to <see cref="TtlPolicies"/> — the interval of TTLs[i] belongs to TtlPolicies[i].</summary>
        [Description("TTL intervals; parallel to `ttlPolicies` — the interval of ttls[i] belongs to ttlPolicies[i].")]
        public List<TimeIntervalDto> Ttls { get; init; } = [];

        /// <summary>Regular (non-TTL) alert policies applied to every matching sensor.</summary>
        [Description("Regular (non-TTL) alert policies applied to every matching sensor.")]
        public List<AlertPolicyDto> Policies { get; init; } = [];

        /// <summary>
        /// Output-only apply diagnostics of THIS write: template policies that matching
        /// sensors REJECTED at apply time — the save persisted, but each listed alert was
        /// NOT created on that sensor (e.g. a Value condition that a bar sensor's type
        /// does not offer). Set only on create/update responses with rejections; absent
        /// on reads and on clean writes (WhenWritingNull — a warning-free response keeps
        /// its pre-warnings byte shape); ignored when sent by a client.
        /// </summary>
        [Description("Output-only apply diagnostics of THIS write: template policies that matching sensors REJECTED at apply time — the save persisted, but each listed alert was NOT created on that sensor (e.g. a Value condition the sensor's type does not offer). Absent on reads and on clean writes; ignored when sent by a client.")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AlertTemplateWarningDto> Warnings { get; init; }
    }

    /// <summary>One apply warning: the alert was not created on this sensor.</summary>
    [Description("One apply warning: the alert was not created on this sensor.")]
    public sealed record AlertTemplateWarningDto
    {
        /// <summary>Full path of the sensor that rejected the policy.</summary>
        [Description("Full path of the sensor that rejected the policy.")]
        public string Sensor { get; init; }

        /// <summary>Why the sensor refused the policy (the validation reason).</summary>
        [Description("Why the sensor refused the policy (the validation reason).")]
        public string Reason { get; init; }
    }

    /// <summary>
    /// One alert policy: conditions + notification destination + schedule. A Guid.Empty
    /// id is regenerated server-side on every write (empty ids would collide in
    /// per-sensor policy collections); ids must be unique across policies and ttlPolicies.
    /// </summary>
    [Description("One alert policy: conditions + notification destination + send schedule. A Guid.Empty id is regenerated server-side on every write; ids must be unique across policies and ttlPolicies.")]
    public sealed record AlertPolicyDto
    {
        /// <summary>Policy id; Guid.Empty means "server generates one".</summary>
        [Description("Policy id; Guid.Empty means \"server generates one\" (and is regenerated on every write).")]
        public Guid Id { get; init; }

        /// <summary>Conditions combined into the policy trigger.</summary>
        [Description("Conditions combined into the policy trigger (see the condition tables on each field).")]
        public List<PolicyConditionDto> Conditions { get; init; } = [];

        /// <summary>Notification destination (chat set + mode flags); null means defaults.</summary>
        [Description("Notification destination (chat set + mode flags); null means defaults.")]
        public PolicyDestinationDto Destination { get; init; } = new();

        /// <summary>Send schedule of a fired alert; null means defaults.</summary>
        [Description("Send schedule of a fired alert; null means defaults.")]
        public PolicyScheduleDto Schedule { get; init; } = new();

        /// <summary>Status the sensor gets while the policy is triggered (HSMCommon SensorStatus): 0=Ok, 1=Error, 255=OffTime.</summary>
        [Description("Status the sensor gets while the policy is triggered: 0=Ok, 1=Error, 255=OffTime.")]
        public byte SensorStatus { get; init; }

        /// <summary>Disabled policies stay stored but never fire.</summary>
        [Description("Disabled policies stay stored but never fire.")]
        public bool IsDisabled { get; init; }

        /// <summary>Alert message template; the full alert-variable table (every registered variable, with its meaning) lives in <see cref="AlertVariablesDocs.Variables"/>.</summary>
        [Description(AlertVariablesDocs.Variables)]
        public string Template { get; init; }

        /// <summary>UI icon name.</summary>
        [Description("UI icon shown on the sensor while the policy is triggered (e.g. \"🔥\").")]
        public string Icon { get; init; }

        /// <summary>Confirmation period (100-ns ticks), optional.</summary>
        [Description("Confirmation period in 100-ns ticks (the policy must stay triggered this long before firing); null/omitted disables confirmation.")]
        public long? ConfirmationPeriod { get; init; }

        /// <summary>Reserved linkage fields of the durable policy; normally omitted by clients.</summary>
        [Description("Reserved linkage field of the durable policy; normally omitted by clients.")]
        public Guid? TemplateId { get; init; }

        /// <summary>Working-time schedule the policy respects; must reference an existing /api/v1/alertSchedules id.</summary>
        [Description("Working-time schedule the policy respects; must reference an existing alert-schedule id (see list_alert_schedules); null = always.")]
        public Guid? ScheduleId { get; init; }

        /// <summary>Reserved linkage field of the durable policy; normally omitted by clients.</summary>
        [Description("Reserved linkage field of the durable policy; normally omitted by clients.")]
        public Guid? TemplateAlertId { get; init; }
    }

    /// <summary>One condition of a policy: property, comparison operation, target value.</summary>
    [Description("One condition of a policy: property, comparison operation, target value. The property/operation pair must be one the SENSOR'S TYPE offers — mismatches are reported as apply warnings.")]
    public sealed record PolicyConditionDto
    {
        /// <summary>The value the property is compared against (constant, or the sensor's last value).</summary>
        [Description("The value the property is compared against (a constant, or the sensor's last value).")]
        public PolicyTargetDto Target { get; init; }

        /// <summary>How this condition combines with the PREVIOUS one: 0=And, 1=Or.</summary>
        [Description("How this condition combines with the PREVIOUS one: 0=And (default), 1=Or; ignored on the first condition.")]
        public byte Combination { get; init; }

        /// <summary>
        /// Comparison operation (PolicyOperation enum):
        /// 0=LessThanOrEqual, 1=LessThan, 2=GreaterThan, 3=GreaterThanOrEqual, 4=Equal,
        /// 5=NotEqual, 20=IsChanged, 21=IsError, 22=IsOk, 23=IsChangedToError,
        /// 24=IsChangedToOk, 30=Contains, 31=StartsWith, 32=EndsWith, 50=ReceivedNewValue.
        /// </summary>
        [Description("Comparison operation (PolicyOperation enum): 0=LessThanOrEqual, 1=LessThan, 2=GreaterThan, 3=GreaterThanOrEqual, 4=Equal, 5=NotEqual, 20=IsChanged, 21=IsError, 22=IsOk, 23=IsChangedToError, 24=IsChangedToOk, 30=Contains, 31=StartsWith, 32=EndsWith, 50=ReceivedNewValue. Status/IsChanged/ReceivedNewValue-style operations need no target.")]
        public byte Operation { get; init; }

        /// <summary>
        /// The sensor property compared (PolicyProperty enum):
        /// 0=Status, 1=Comment, 20=Value, 101=Min, 102=Max, 103=Mean, 104=Count,
        /// 105=LastValue, 106=FirstValue, 120=Length (value length), 151=OriginalSize,
        /// 200=NewSensorData, 210=EmaValue, 211=EmaMin, 212=EmaMax, 213=EmaMean, 214=EmaCount.
        /// The availability matrix by sensor type lives in
        /// <see cref="AlertPropertyMatrixDocs.Matrix"/> (exhaustive, negatives included).
        /// </summary>
        [Description("The sensor property compared (PolicyProperty enum): 0=Status, 1=Comment, 20=Value, 101=Min, 102=Max, 103=Mean, 104=Count, 105=LastValue, 106=FirstValue, 120=Length (value length), 151=OriginalSize (File), 200=NewSensorData, 210=EmaValue, 211=EmaMin, 212=EmaMax, 213=EmaMean, 214=EmaCount. " + AlertPropertyMatrixDocs.Matrix)]
        public byte Property { get; init; }
    }

    /// <summary>The right-hand side of a condition.</summary>
    [Description("The right-hand side of a condition.")]
    public sealed record PolicyTargetDto
    {
        /// <summary>
        /// 0=Const (compare against <see cref="Value"/>), 1=LastValue (compare against the sensor's last value).
        /// Target-less operations (IsChanged, IsError, IsOk, IsChangedToError, IsChangedToOk, ReceivedNewValue)
        /// never read the constant — a Const target on them is accepted and evaluated as LastValue
        /// (the sensor's own previous value), matching what the web UI submits for them.
        /// </summary>
        [Description("Target type: 0=Const (compare against `value`), 1=LastValue (compare against the sensor's last value). Target-less operations (IsChanged, IsError, IsOk, IsChangedToError, IsChangedToOk, ReceivedNewValue) never read the constant — a Const target on them is evaluated as the sensor's own previous value.")]
        public byte Type { get; init; }

        /// <summary>The constant compared against when type is Const; ignored for LastValue.</summary>
        [Description("The constant compared against when type is 0=Const; ignored for LastValue. A plain string — numeric properties parse it as a number.")]
        public string Value { get; init; }
    }

    /// <summary>
    /// Notification destination. Chat keys are chat ids (Guid strings); display names
    /// are an echo that the server overwrites on every write. A chat must be global or
    /// bound to the template's folder, otherwise the write answers 400.
    /// </summary>
    [Description("Notification destination. Chat keys are chat ids (Guid strings — use list_chats to discover them); display-name values are an echo the server overwrites on every write. A chat must be global or bound to the template's folder, otherwise the write answers 400.")]
    public sealed record PolicyDestinationDto
    {
        /// <summary>Chat id (Guid string) to chat display name; at least one chat for a custom destination.</summary>
        [Description("Chat id (Guid string) to chat display name; at least one chat for a custom destination (the name half is an echo).")]
        public Dictionary<string, string> Chats { get; init; } = new();

        /// <summary>Mode flag: destination not initialized (zero chats reconstruct as this).</summary>
        [Description("Mode flag: destination not initialized (a destination with zero chats reconstructs as this).")]
        public bool IsNotInitialized { get; init; }

        /// <summary>Mode flag: no destination.</summary>
        [Description("Mode flag: no destination (alerts fire without a notification).")]
        public bool IsEmpty { get; init; }

        /// <summary>Mode flag: send through the folder's default chats.</summary>
        [Description("Mode flag: send through the folder's default chats.")]
        public bool UseDefaultChats { get; init; }

        /// <summary>Mode flag: send through every chat bound to the folder.</summary>
        [Description("Mode flag: send through every chat bound to the folder.")]
        public bool AllChats { get; init; }
    }

    /// <summary>Send schedule of a fired alert.</summary>
    [Description("Send schedule of a fired alert.")]
    public sealed record PolicyScheduleDto
    {
        /// <summary>Send time offset within the aggregation window, in 100-ns ticks.</summary>
        [Description("Send time offset within the aggregation window, in 100-ns ticks.")]
        public long TimeTicks { get; init; }

        /// <summary>Send immediately, outside the aggregation window.</summary>
        [Description("Send immediately, outside the aggregation window.")]
        public bool InstantSend { get; init; }

        /// <summary>
        /// Re-send interval of a still-triggered policy (AlertRepeatMode enum):
        /// 0=Immediately, 5=FiveMinutes, 6=TenMinutes, 7=FifteenMinutes, 10=ThirtyMinutes,
        /// 20=Hourly, 50=Daily, 100=Weekly.
        /// </summary>
        [Description("Re-send interval of a still-triggered policy (AlertRepeatMode enum): 0=Immediately, 5=FiveMinutes, 6=TenMinutes, 7=FifteenMinutes, 10=ThirtyMinutes, 20=Hourly, 50=Daily, 100=Weekly.")]
        public byte RepeateMode { get; init; }
    }

    /// <summary>
    /// One TTL interval. `Interval` is a SPARSE TimeInterval enum (long):
    /// -100=FromFolder, -10=FromParent, -1=Ticks (authoritative — read Ticks),
    /// 0=None, 26784000000000=Month (31 days), 80352000000000=ThreeMonths,
    /// 160704000000000=SixMonths, 315360000000000=Year (365 days).
    /// Undefined values answer 400 (they would throw inside the timeout scan later).
    /// </summary>
    [Description("One TTL interval. `interval` is a SPARSE TimeInterval enum (long): -100=FromFolder, -10=FromParent, -1=Ticks (authoritative — read `ticks`), 0=None, 26784000000000=Month (31 days), 80352000000000=ThreeMonths, 160704000000000=SixMonths, 315360000000000=Year (365 days). Undefined values answer 400.")]
    public sealed record TimeIntervalDto
    {
        /// <summary>TimeInterval enum value (see the record summary for the full list).</summary>
        [Description("TimeInterval enum value (see the TimeInterval description for the full table).")]
        public long Interval { get; init; }

        /// <summary>Custom interval in 100-ns ticks; authoritative when Interval is -1 (Ticks), otherwise ignored.</summary>
        [Description("Custom interval in 100-ns ticks; authoritative when `interval` is -1 (Ticks), otherwise ignored.")]
        public long Ticks { get; init; }
    }
}
