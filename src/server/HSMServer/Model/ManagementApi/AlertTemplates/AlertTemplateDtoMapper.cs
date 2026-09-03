using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;

namespace HSMServer.Model.ManagementApi.AlertTemplates
{
    // DTO <-> durable-entity mapping for /api/v1/alertTemplates. Both directions ride
    // the existing mappers (AlertTemplateModel.ToEntity / the AlertTemplateModel(entity)
    // constructor); this class only adapts the wire shape: Guid strings instead of raw
    // byte arrays, and the write-side normalizations (server template id, regeneration
    // of empty policy ids, canonical chat display names).
    internal static class AlertTemplateDtoMapper
    {
        internal static AlertTemplateDto ToDto(AlertTemplateModel model)
        {
            var entity = model.ToEntity();

            return new AlertTemplateDto
            {
                Id = new Guid(entity.Id),
                Name = entity.Name,
                SensorType = entity.SensorType,
                FolderId = entity.FolderId,
                Paths = [.. entity.Paths ?? []],
                TtlPolicies = [.. (entity.TTLPolicies ?? []).Select(ToDto)],
                Ttls = [.. (entity.TTLs ?? []).Select(ToDto)],
                Policies = [.. (entity.Policies ?? []).Select(ToDto)],
            };
        }

        internal static AlertTemplateEntity ToEntity(AlertTemplateDto dto, Guid id,
            IReadOnlyDictionary<string, string> canonicalChatNames)
        {
            return new AlertTemplateEntity
            {
                Id = id.ToByteArray(),
                Name = dto.Name,
                SensorType = dto.SensorType,
                FolderId = dto.FolderId,
                Paths = dto.Paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? [],
                TTLPolicies = [.. (dto.TtlPolicies ?? []).Select(p => ToEntity(p, canonicalChatNames))],
                TTLs = [.. (dto.Ttls ?? []).Select(ToEntity)],
                Policies = [.. (dto.Policies ?? []).Select(p => ToEntity(p, canonicalChatNames))],
                // Legacy single-value fields stay unset on purpose: the plural lists are
                // authoritative and the entity constructor migrates them forward anyway.
            };
        }

        private static AlertPolicyDto ToDto(PolicyEntity policy) => new()
        {
            Id = ToGuid(policy.Id),
            Conditions = [.. (policy.Conditions ?? []).Select(ToDto)],
            Destination = ToDto(policy.Destination),
            Schedule = ToDto(policy.Schedule),
            SensorStatus = policy.SensorStatus,
            IsDisabled = policy.IsDisabled,
            Template = policy.Template,
            Icon = policy.Icon,
            ConfirmationPeriod = policy.ConfirmationPeriod,
            TemplateId = ToNullableGuid(policy.TemplateId),
            ScheduleId = ToNullableGuid(policy.ScheduleId),
            TemplateAlertId = ToNullableGuid(policy.TemplateAlertId),
        };

        private static PolicyEntity ToEntity(AlertPolicyDto policy, IReadOnlyDictionary<string, string> canonicalChatNames)
        {
            return new PolicyEntity
            {
                // Empty ids are regenerated: an all-zero id would persist and collide in
                // per-sensor policy collections at apply time.
                Id = (policy.Id == Guid.Empty ? Guid.NewGuid() : policy.Id).ToByteArray(),

                Conditions = [.. (policy.Conditions ?? []).Select(ToEntity)],

                // Display names are echoes: the manager's current name is authoritative,
                // so a stale name in the payload never survives a write.
                Destination = ToEntity(policy.Destination, canonicalChatNames),

                Schedule = ToEntity(policy.Schedule),

                SensorStatus = policy.SensorStatus,
                IsDisabled = policy.IsDisabled,
                Template = policy.Template,
                Icon = policy.Icon,
                ConfirmationPeriod = policy.ConfirmationPeriod,
                TemplateId = ToBytes(policy.TemplateId),
                ScheduleId = ToBytes(policy.ScheduleId),
                TemplateAlertId = ToBytes(policy.TemplateAlertId),
            };
        }

        private static PolicyConditionDto ToDto(PolicyConditionEntity condition) => new()
        {
            Target = new PolicyTargetDto { Type = condition.Target.Type, Value = condition.Target.Value },
            Combination = condition.Combination,
            Operation = condition.Operation,
            Property = condition.Property,
        };

        private static PolicyConditionEntity ToEntity(PolicyConditionDto condition) => new()
        {
            Target = new PolicyTargetEntity(condition.Target.Type, condition.Target.Value),
            Combination = condition.Combination,
            Operation = condition.Operation,
            Property = condition.Property,
        };

        private static PolicyDestinationDto ToDto(PolicyDestinationEntity destination) => new()
        {
            Chats = destination.Chats is { Count: > 0 } chats ? new Dictionary<string, string>(chats) : new(),
            IsNotInitialized = destination.IsNotInitialized,
            IsEmpty = destination.IsEmpty,
            UseDefaultChats = destination.UseDefaultChats,
            AllChats = destination.AllChats,
        };

        private static PolicyDestinationEntity ToEntity(PolicyDestinationDto destination,
            IReadOnlyDictionary<string, string> canonicalChatNames)
        {
            var chats = new Dictionary<string, string>();

            foreach (var (chatId, _) in destination.Chats ?? new Dictionary<string, string>())
                chats[chatId] = canonicalChatNames.GetValueOrDefault(chatId);

            return new PolicyDestinationEntity
            {
                Chats = chats,
                IsNotInitialized = destination.IsNotInitialized,
                IsEmpty = destination.IsEmpty,
                UseDefaultChats = destination.UseDefaultChats,
                AllChats = destination.AllChats,
            };
        }

        private static PolicyScheduleDto ToDto(PolicyScheduleEntity schedule) => new()
        {
            TimeTicks = schedule.TimeTicks,
            InstantSend = schedule.InstantSend,
            RepeateMode = schedule.RepeateMode,
        };

        private static PolicyScheduleEntity ToEntity(PolicyScheduleDto schedule) => new()
        {
            TimeTicks = schedule.TimeTicks,
            InstantSend = schedule.InstantSend,
            RepeateMode = schedule.RepeateMode,
        };

        private static TimeIntervalDto ToDto(TimeIntervalEntity interval) => new()
        {
            Interval = interval.Interval,
            Ticks = interval.Ticks,
        };

        private static TimeIntervalEntity ToEntity(TimeIntervalDto interval) => new(interval.Interval, interval.Ticks);

        private static Guid ToGuid(byte[] bytes) =>
            bytes is { Length: 16 } valid ? new Guid(valid) : Guid.Empty;

        private static Guid? ToNullableGuid(byte[] bytes) =>
            bytes is { Length: 16 } valid ? new Guid(valid) : null;

        private static byte[] ToBytes(Guid? id) =>
            id.HasValue ? id.Value.ToByteArray() : [];
    }
}
