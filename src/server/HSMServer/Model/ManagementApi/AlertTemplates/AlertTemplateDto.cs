using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.AlertTemplates
{
    /// <summary>
    /// Alert template wire contract of the /api/v1/alertTemplates surface. The shape
    /// mirrors the durable template one field per field; ids are Guid strings. Byte
    /// fields carry DOMAIN ENUM VALUES (documented per field) — membership is validated
    /// structurally, unknown values answer 400 with the offending field key.
    /// </summary>
    /// <remarks>
    /// Round-trip notes: a client-sent template id is IGNORED on create (server
    /// generates one) and must equal the route id on update or be absent; Guid.Empty
    /// policy ids are regenerated server-side on every write; destination chat display
    /// names are overwritten with the manager's current names. `destination`/`schedule`
    /// null means "omitted" (defaults apply). TTL policies and intervals are parallel
    /// lists matched by position.
    /// </remarks>
    public sealed record AlertTemplateDto
    {
        /// <summary>Output-only: assigned by the server on create. On update it must equal the route id or be absent.</summary>
        public Guid Id { get; init; }

        /// <summary>Unique name, 1..200 characters, case-sensitive, globally unique across folders.</summary>
        public string Name { get; init; }

        /// <summary>
        /// SensorType enum value, or 100 (AnyType) to match every sensor type:
        /// 0=Boolean, 1=Integer, 2=Double, 3=String, 4=IntegerBar, 5=DoubleBar, 6=File,
        /// 7=TimeSpan, 8=Version, 9=Rate, 10=Enum, 100=AnyType.
        /// </summary>
        public byte SensorType { get; init; }

        /// <summary>Target folder id; required on create and update.</summary>
        public Guid FolderId { get; init; }

        /// <summary>Path templates the template applies to (e.g. "Root/*/cpu"); 1..100 entries, each a sensor path pattern.</summary>
        public List<string> Paths { get; init; } = [];

        /// <summary>TTL alert policies; parallel to <see cref="Ttls"/> (same length, matched by position).</summary>
        public List<AlertPolicyDto> TtlPolicies { get; init; } = [];

        /// <summary>TTL intervals; parallel to <see cref="TtlPolicies"/> — the interval of TTLs[i] belongs to TtlPolicies[i].</summary>
        public List<TimeIntervalDto> Ttls { get; init; } = [];

        /// <summary>Regular (non-TTL) alert policies applied to every matching sensor.</summary>
        public List<AlertPolicyDto> Policies { get; init; } = [];
    }

    /// <summary>
    /// One alert policy: conditions + notification destination + schedule. A Guid.Empty
    /// id is regenerated server-side on every write (empty ids would collide in
    /// per-sensor policy collections); ids must be unique across policies and ttlPolicies.
    /// </summary>
    public sealed record AlertPolicyDto
    {
        /// <summary>Policy id; Guid.Empty means "server generates one".</summary>
        public Guid Id { get; init; }

        /// <summary>Conditions combined into the policy trigger.</summary>
        public List<PolicyConditionDto> Conditions { get; init; } = [];

        /// <summary>Notification destination (chat set + mode flags); null means defaults.</summary>
        public PolicyDestinationDto Destination { get; init; } = new();

        /// <summary>Send schedule of a fired alert; null means defaults.</summary>
        public PolicyScheduleDto Schedule { get; init; } = new();

        /// <summary>Status the sensor gets while the policy is triggered (HSMCommon SensorStatus): 0=Ok, 1=Error, 255=OffTime.</summary>
        public byte SensorStatus { get; init; }

        /// <summary>Disabled policies stay stored but never fire.</summary>
        public bool IsDisabled { get; init; }

        /// <summary>Alert message template (may reference sensor values).</summary>
        public string Template { get; init; }

        /// <summary>UI icon name.</summary>
        public string Icon { get; init; }

        /// <summary>Confirmation period (100-ns ticks), optional.</summary>
        public long? ConfirmationPeriod { get; init; }

        /// <summary>Reserved linkage fields of the durable policy; normally omitted by clients.</summary>
        public Guid? TemplateId { get; init; }

        /// <summary>Working-time schedule the policy respects; must reference an existing /api/v1/alertSchedules id.</summary>
        public Guid? ScheduleId { get; init; }

        /// <summary>Reserved linkage field of the durable policy; normally omitted by clients.</summary>
        public Guid? TemplateAlertId { get; init; }
    }

    /// <summary>One condition of a policy: property, comparison operation, target value.</summary>
    public sealed record PolicyConditionDto
    {
        /// <summary>The value the property is compared against (constant, or the sensor's last value).</summary>
        public PolicyTargetDto Target { get; init; }

        /// <summary>How this condition combines with the PREVIOUS one: 0=And, 1=Or.</summary>
        public byte Combination { get; init; }

        /// <summary>
        /// Comparison operation (PolicyOperation enum):
        /// 0=LessThanOrEqual, 1=LessThan, 2=GreaterThan, 3=GreaterThanOrEqual, 4=Equal,
        /// 5=NotEqual, 20=IsChanged, 21=IsError, 22=IsOk, 23=IsChangedToError,
        /// 24=IsChangedToOk, 30=Contains, 31=StartsWith, 32=EndsWith, 50=ReceivedNewValue.
        /// </summary>
        public byte Operation { get; init; }

        /// <summary>
        /// The sensor property compared (PolicyProperty enum):
        /// 0=Status, 1=Comment, 20=Value, 101=Min, 102=Max, 103=Mean, 104=Count,
        /// 105=LastValue, 106=FirstValue, 120=Length (value length), 151=OriginalSize,
        /// 200=NewSensorData, 210=EmaValue, 211=EmaMin, 212=EmaMax, 213=EmaMean, 214=EmaCount.
        /// </summary>
        public byte Property { get; init; }
    }

    /// <summary>The right-hand side of a condition.</summary>
    public sealed record PolicyTargetDto
    {
        /// <summary>0=Const (compare against <see cref="Value"/>), 1=LastValue (compare against the sensor's last value).</summary>
        public byte Type { get; init; }

        /// <summary>The constant compared against when type is Const; ignored for LastValue.</summary>
        public string Value { get; init; }
    }

    /// <summary>
    /// Notification destination. Chat keys are chat ids (Guid strings); display names
    /// are an echo that the server overwrites on every write. A chat must be global or
    /// bound to the template's folder, otherwise the write answers 400.
    /// </summary>
    public sealed record PolicyDestinationDto
    {
        /// <summary>Chat id (Guid string) to chat display name; at least one chat for a custom destination.</summary>
        public Dictionary<string, string> Chats { get; init; } = new();

        /// <summary>Mode flag: destination not initialized (zero chats reconstruct as this).</summary>
        public bool IsNotInitialized { get; init; }

        /// <summary>Mode flag: no destination.</summary>
        public bool IsEmpty { get; init; }

        /// <summary>Mode flag: send through the folder's default chats.</summary>
        public bool UseDefaultChats { get; init; }

        /// <summary>Mode flag: send through every chat bound to the folder.</summary>
        public bool AllChats { get; init; }
    }

    /// <summary>Send schedule of a fired alert.</summary>
    public sealed record PolicyScheduleDto
    {
        /// <summary>Send time offset within the aggregation window, in 100-ns ticks.</summary>
        public long TimeTicks { get; init; }

        /// <summary>Send immediately, outside the aggregation window.</summary>
        public bool InstantSend { get; init; }

        /// <summary>
        /// Re-send interval of a still-triggered policy (AlertRepeatMode enum):
        /// 0=Immediately, 5=FiveMinutes, 6=TenMinutes, 7=FifteenMinutes, 10=ThirtyMinutes,
        /// 20=Hourly, 50=Daily, 100=Weekly.
        /// </summary>
        public byte RepeateMode { get; init; }
    }

    /// <summary>
    /// One TTL interval. `Interval` is a SPARSE TimeInterval enum (long):
    /// -100=FromFolder, -10=FromParent, -1=Ticks (authoritative — read Ticks),
    /// 0=None, 26784000000000=Month (31 days), 80352000000000=ThreeMonths,
    /// 160704000000000=SixMonths, 315360000000000=Year (365 days).
    /// Undefined values answer 400 (they would throw inside the timeout scan later).
    /// </summary>
    public sealed record TimeIntervalDto
    {
        /// <summary>TimeInterval enum value (see the record summary for the full list).</summary>
        public long Interval { get; init; }

        /// <summary>Custom interval in 100-ns ticks; authoritative when Interval is -1 (Ticks), otherwise ignored.</summary>
        public long Ticks { get; init; }
    }
}
