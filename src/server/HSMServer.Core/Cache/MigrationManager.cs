using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Cache
{
    internal sealed class MigrationManager
    {
        private static readonly InitiatorInfo _migrator = InitiatorInfo.AsSystemMigrator();

        private static readonly HashSet<PolicyProperty> _numberToEmaSet =
        [
            PolicyProperty.Value,
            PolicyProperty.Mean,
            PolicyProperty.Max,
            PolicyProperty.Min,
            PolicyProperty.Count
        ];

        private static readonly HashSet<PolicyProperty> _emaToScheduleSet =
        [
            PolicyProperty.EmaValue,
            PolicyProperty.EmaMean,
            PolicyProperty.EmaMax,
            PolicyProperty.EmaMin,
            PolicyProperty.EmaCount,
        ];


        internal IEnumerable<SensorUpdate> GetMigrationUpdates(List<BaseSensorModel> sensors)
        {
            foreach (var sensor in sensors)
                if (IsDefaultSensor(sensor) && IsNumberSensor(sensor.Type))
                {
                    if (TryBuildNumberToEmaMigration(sensor, out var update))
                        yield return update;

                    if (TryBuildNumberToScheduleMigration(sensor, out update))
                        yield return update;
                }
        }


        private static bool TryBuildNumberToEmaMigration(BaseSensorModel sensor, out SensorUpdate update)
        {
            static bool IsTarget(Policy policy) => IsTargetPolicy(policy, _numberToEmaSet);

            static PolicyUpdate Migration(PolicyUpdate update)
            {
                var conditions = update.Conditions[0];

                update.Conditions[0] = conditions with
                {
                    Property = conditions.Property switch
                    {
                        PolicyProperty.Value => PolicyProperty.EmaValue,
                        PolicyProperty.Mean => PolicyProperty.EmaMean,
                        PolicyProperty.Max => PolicyProperty.EmaMax,
                        PolicyProperty.Min => PolicyProperty.EmaMin,
                        PolicyProperty.Count => PolicyProperty.EmaCount,
                        _ => conditions.Property,
                    }
                };

                return update;
            }


            var result = TryMigratePolicy(sensor, IsTarget, Migration, out update);

            if (result)
                update = update with { Statistics = StatisticsOptions.EMA };

            return result;
        }

        private static bool TryBuildNumberToScheduleMigration(BaseSensorModel sensor, out SensorUpdate update)
        {
            static bool IsTarget(Policy policy) => IsTargetPolicy(policy, _emaToScheduleSet);

            static PolicyUpdate Migration(PolicyUpdate update) => update with
            {
                Schedule = new PolicyScheduleUpdate
                {
                    RepeatMode = AlertRepeatMode.Hourly,
                    Time = new DateTime(1, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                    InstantSend = true,
                }
            };

            return TryMigratePolicy(sensor, IsTarget, Migration, out update);
        }

        private static bool TryMigratePolicy(BaseSensorModel sensor, Predicate<Policy> isTarget, Func<PolicyUpdate, PolicyUpdate> migrator, out SensorUpdate sensorUpdate)
        {
            sensorUpdate = null;

            var alerts = new List<PolicyUpdate>();
            var hasMigrations = false;

            foreach (var policy in sensor.Policies)
            {
                var update = ToUpdate(policy);

                if (isTarget(policy))
                {
                    update = migrator(update);
                    hasMigrations = true;
                }

                alerts.Add(update);
            }

            if (!hasMigrations)
                return false;

            sensorUpdate = new SensorUpdate()
            {
                Id = sensor.Id,
                Policies = alerts,
                Initiator = _migrator,
            };

            return true;
        }

        private static bool IsTargetPolicy(Policy policy, HashSet<PolicyProperty> targetProperties)
        {
            if (policy.Conditions.Count == 1)
            {
                var condition = policy.Conditions[0];

                return targetProperties.Contains(condition.Property);
            }

            return false;
        }


        private static bool IsDefaultSensor(BaseSensorModel sensor) => IsComputerSensor(sensor) || IsModuleSensor(sensor);

        private static bool IsComputerSensor(BaseSensorModel sensor) => sensor.Path.Contains(".computer");

        private static bool IsModuleSensor(BaseSensorModel sensor) => sensor.Path.Contains(".module");


        private static bool IsNumberSensor(SensorType type) => type.IsBar() || type is SensorType.Integer or SensorType.Double or SensorType.Counter;


        private static PolicyUpdate ToUpdate(Policy policy) =>
            new()
            {
                Conditions = policy.Conditions.Select(ToUpdate).ToList(),
                Destination = ToUpdate(policy.Destination),
                Schedule = ToUpdate(policy.Schedule),

                ConfirmationPeriod = policy.ConfirmationPeriod,
                Id = policy.Id,
                Status = policy.Status,
                Template = policy.Template,
                IsDisabled = policy.IsDisabled,
                Icon = policy.Icon,

                Initiator = _migrator,
            };

        private static PolicyScheduleUpdate ToUpdate(PolicySchedule schedule) =>
            new()
            {
                InstantSend = schedule.InstantSend,
                RepeatMode = schedule.RepeatMode,
                Time = schedule.Time,
            };

        private static PolicyDestinationUpdate ToUpdate(PolicyDestination destination) =>
            new(destination.Chats, destination.AllChats);

        private static PolicyConditionUpdate ToUpdate(PolicyCondition condition) =>
            new()
            {
                Operation = condition.Operation,
                Target = condition.Target,
                Property = condition.Property
            };
    }
}
