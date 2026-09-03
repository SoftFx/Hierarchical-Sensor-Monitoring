using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.AlertTemplates
{
    // Wire contract of the /api/v1/alertTemplates REST surface (#1351, epic #1347). The
    // shape mirrors the durable AlertTemplateEntity one field per field — the round-trip
    // goes through the battle-tested entity mappers (AlertTemplateModel.ToEntity /
    // AlertTemplateModel(entity)) — with three deliberate deviations, each a round-trip
    // trap rather than data:
    //   - PolicyDestinationEntity.Kind: legacy, never written by ToEntity and never read
    //     back — a published field that is always null and ignored;
    //   - PolicyEntity.TTL on TTL policies: written but never read (the interval is
    //     authoritative in the parallel TTLs list, position by position);
    //   - the legacy single Path / TTLPolicy / TTL fields of AlertTemplateEntity.
    // Ids are Guid strings (entities carry raw byte arrays); an absent optional id is
    // null on the wire and an empty array in the entity.
    public sealed record AlertTemplateDto
    {
        // Output-only: assigned by the server on create (a client-chosen id is ignored —
        // the cache Add is an upsert by id, so honoring a client id would let a scoped
        // token overwrite a template it cannot even see). On update it must equal the
        // route id or be absent.
        public Guid Id { get; init; }

        public string Name { get; init; }

        // A SensorType enum value (0..10) or AlertTemplateModel.AnyType (100).
        public byte SensorType { get; init; }

        public Guid FolderId { get; init; }

        public List<string> Paths { get; init; } = [];

        // Parallel lists: TTL policies and their intervals, matched by position. The
        // interval comes from TTLs[i]; lengths must be equal.
        public List<AlertPolicyDto> TtlPolicies { get; init; } = [];

        public List<TimeIntervalDto> Ttls { get; init; } = [];

        public List<AlertPolicyDto> Policies { get; init; } = [];
    }

    // Alert policy (condition list + notification destination + schedule). Byte fields
    // are the domain enum values: Operation = PolicyOperation, Property = PolicyProperty,
    // Combination = PolicyCombination, Target.Type = TargetType, SensorStatus =
    // SensorStatus, RepeateMode = AlertRepeatMode. Enum membership is validated
    // structurally before the domain ever casts.
    public sealed record AlertPolicyDto
    {
        // A Guid.Empty id is regenerated server-side on every write: an empty id would
        // persist and collide in per-sensor policy collections at apply time.
        public Guid Id { get; init; }

        public List<PolicyConditionDto> Conditions { get; init; } = [];

        public PolicyDestinationDto Destination { get; init; } = new();

        public PolicyScheduleDto Schedule { get; init; } = new();

        public byte SensorStatus { get; init; }

        public bool IsDisabled { get; init; }

        public string Template { get; init; }

        public string Icon { get; init; }

        public long? ConfirmationPeriod { get; init; }

        public Guid? TemplateId { get; init; }

        public Guid? ScheduleId { get; init; }

        public Guid? TemplateAlertId { get; init; }
    }

    public sealed record PolicyConditionDto
    {
        public PolicyTargetDto Target { get; init; }

        public byte Combination { get; init; }

        public byte Operation { get; init; }

        public byte Property { get; init; }
    }

    public sealed record PolicyTargetDto
    {
        public byte Type { get; init; }

        public string Value { get; init; }
    }

    // Chat keys are Guid strings; the display name is an echo that the server
    // overwrites with the manager's current name on every write. Chat availability is
    // validated against the template's folder (a chat bound to another folder is not
    // offerable), mirroring the web UI's dropdown rule.
    public sealed record PolicyDestinationDto
    {
        public Dictionary<string, string> Chats { get; init; } = new();

        public bool IsNotInitialized { get; init; }

        public bool IsEmpty { get; init; }

        public bool UseDefaultChats { get; init; }

        public bool AllChats { get; init; }
    }

    public sealed record PolicyScheduleDto
    {
        public long TimeTicks { get; init; }

        public bool InstantSend { get; init; }

        public byte RepeateMode { get; init; }
    }

    public sealed record TimeIntervalDto
    {
        public long Interval { get; init; }

        public long Ticks { get; init; }
    }

    // List envelope: 1-based page, server-clamped size, stable ordering (name, then id).
    public sealed record AlertTemplatePageDto
    {
        public List<AlertTemplateDto> Items { get; init; } = [];

        public int Page { get; init; }

        public int PageSize { get; init; }

        public int TotalCount { get; init; }

        public int TotalPages { get; init; }
    }
}
