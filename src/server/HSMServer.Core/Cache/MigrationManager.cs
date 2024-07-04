using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Cache
{
    internal sealed class MigrationManager
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private static readonly InitiatorInfo _softMigrator = InitiatorInfo.AsSoftSystemMigrator();
        private static readonly InitiatorInfo _forceMigrator = InitiatorInfo.AsSystemMigrator();


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

        private static readonly PolicyUpdate _timeInGcPolicy = new()
        {
            Conditions = [new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.EmaMean, new TargetValue(TargetType.Const, "20"))],
            Destination = new PolicyDestinationUpdate(),
            Icon = "⚠",
            Initiator = _forceMigrator,
            Schedule = GetDefaultScheduleUpdate(),
            Template = "[$product]$path $property $operation $target $unit",
        };


        internal delegate bool SensorMigrationApplyEvent(SensorUpdate update, out string error);

        internal event SensorMigrationApplyEvent ApplySensorMigration;
        internal event Action<ProductUpdate> ApplyProductMigration;


        internal void RunSensorMigrations(List<BaseSensorModel> sensors)
        {
            foreach (var update in GetSensorsMigrationUpdates(sensors))
                try
                {
                    ApplySensorMigration?.Invoke(update, out _);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Sensor migration is failed: {update.Id} {ex}");
                }
        }

        internal void RunProductMigrations(List<ProductModel> products)
        {
            foreach (var update in GetProductsMigrationUpdates(products))
                try
                {
                    ApplyProductMigration?.Invoke(update);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Product migration is failed: {update.Id} {ex}");
                }
        }

        private static IEnumerable<ProductUpdate> GetProductsMigrationUpdates(List<ProductModel> products)
        {
            foreach (var product in products)
            {
                if (TryMigrateProductDefaultChatToFolder(product, out var update))
                    yield return update;

                if (TryMigrateProductDefaultChatToParent(product, out update))
                    yield return update;

                if (TryMigrateTTLCustomDestinationToParent(product, out update))
                    yield return update;

                if (TryMigrateTTLNotParentDestinationToParent(product, out update)) //TODO: should be changed to soft initiator
                    yield return update;
            }
        }

        private static IEnumerable<SensorUpdate> GetSensorsMigrationUpdates(List<BaseSensorModel> sensors)
        {
            foreach (var sensor in sensors)
            {
                if (IsDefaultSensor(sensor))
                {
                    if (IsNumberSensor(sensor.Type))
                    {
                        if (TryBuildNumberToEmaMigration(sensor, out var updateDefault))
                            yield return updateDefault;

                        if (TryBuildNumberToScheduleMigration(sensor, out updateDefault))
                            yield return updateDefault;

                        if (TryBuildTimeInGcSensorMigration(sensor, out updateDefault))
                            yield return updateDefault;
                    }

                    if (IsBoolSensor(sensor.Type) && TryMigrateServiceAliveTtlToSchedule(sensor, out var updateTtl))
                        yield return updateTtl;
                }

                if (TryMigratePolicyDestinationToDefaultChat(sensor, out var update))
                    yield return update;

                if (TryMigrateSensorTTLPolicyDestinationToDefaultChat(sensor, out update))
                    yield return update;
            }
        }


        private static bool TryMigratePolicyDestinationToDefaultChat(BaseSensorModel sensor, out SensorUpdate update)
        {
            static bool IsTarget(Policy policy) => policy.Destination.IsNotInitialized;

            static PolicyUpdate Migration(PolicyUpdate update) => ToFromParentDestination(update);

            return TryMigratePolicy(sensor, IsTarget, Migration, out update);
        }


        private static bool TryMigrateSensorTTLPolicyDestinationToDefaultChat(BaseSensorModel sensor, out SensorUpdate update)
        {
            static bool IsTarget(Policy policy) => policy.Destination.IsNotInitialized;

            return TryMigrateTtlPolicy(sensor, IsTarget, ToFromParentDestination, out update);
        }

        private static bool TryMigrateTTLCustomDestinationToParent(ProductModel product, out ProductUpdate update)
        {
            Dictionary<Guid, string> oldChats = [];

            static bool IsTarget(Policy policy) => policy.Destination.IsCustom;

            PolicyUpdate Migration(PolicyUpdate update)
            {
                oldChats = new(update.Destination.Chats);

                return ToFromParentDestination(update);
            }

            var ok = TryMigrateProductTtlPolicy(product, IsTarget, Migration, out update);

            if (ok && oldChats.Count > 0)
                update = update with { DefaultChats = product.Settings.DefaultChats.CurValue.ApplyNewChats(oldChats) };

            return ok;
        }

        private static bool TryMigrateTTLNotParentDestinationToParent(ProductModel product, out ProductUpdate update)
        {
            static bool IsTarget(Policy policy) => !policy.Destination.IsFromParentChats;

            return TryMigrateProductTtlPolicy(product, IsTarget, ToFromParentDestination, out update);
        }


        private static bool TryMigrateServiceAliveTtlToSchedule(BaseSensorModel sensor, out SensorUpdate update)
        {
            bool IsTarget(Policy policy) => sensor.DisplayName == "Service alive" && !policy.Schedule.IsActive;

            static PolicyUpdate Migration(PolicyUpdate update) =>
                update with { Schedule = GetDefaultScheduleUpdate(false) };

            return TryMigrateTtlPolicy(sensor, IsTarget, Migration, out update);
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
            static bool IsTarget(Policy policy) => IsTargetPolicy(policy, _emaToScheduleSet) && !policy.UseScheduleManagerLogic;

            static PolicyUpdate Migration(PolicyUpdate update) =>
                update with { Schedule = GetDefaultScheduleUpdate() };

            return TryMigratePolicy(sensor, IsTarget, Migration, out update);
        }

        private static bool TryBuildTimeInGcSensorMigration(BaseSensorModel sensor, out SensorUpdate update)
        {
            static bool IsTarget(BaseSensorModel sensor) => sensor.DisplayName == "Time in GC" && !sensor.Statistics.HasEma();


            var result = TryAddPolicy(sensor, IsTarget, _timeInGcPolicy, out update);

            if (result)
                update = update with { Statistics = StatisticsOptions.EMA };

            return result;
        }

        private static bool TryMigratePolicy(BaseSensorModel sensor, Predicate<Policy> isTarget, Func<PolicyUpdate, PolicyUpdate> migrator, out SensorUpdate sensorUpdate)
        {
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

            sensorUpdate = new SensorUpdate()
            {
                Id = sensor.Id,
                Policies = alerts,
                Initiator = _forceMigrator,
            };

            return hasMigrations;
        }

        private static bool TryAddPolicy(BaseSensorModel sensor, Predicate<BaseSensorModel> isTarget, PolicyUpdate policyToAdd, out SensorUpdate sensorUpdate)
        {
            static bool IsTargetPolicy(Policy policy) => false;

            static PolicyUpdate ExistingPoliciesMigration(PolicyUpdate update) => update;

            sensorUpdate = null;

            if (isTarget(sensor))
            {
                TryMigratePolicy(sensor, IsTargetPolicy, ExistingPoliciesMigration, out sensorUpdate);

                sensorUpdate.Policies.Add(policyToAdd);

                return true;
            }

            return false;
        }

        private static bool TryMigrateTtlPolicy<T>(BaseNodeModel node, Predicate<Policy> isTarget, Func<PolicyUpdate, PolicyUpdate> migrator, out T update)
            where T : BaseNodeUpdate, new()
        {
            var ttl = node.Policies.TimeToLive;
            var needMigration = isTarget(ttl);

            update = !needMigration ? null : new T()
            {
                Id = node.Id,
                TTLPolicy = migrator(ToUpdate(ttl)),
                Initiator = _softMigrator,
            };

            return needMigration;
        }

        private static bool TryMigrateProductTtlPolicy(ProductModel node, Predicate<Policy> isTargetPredict, Func<PolicyUpdate, PolicyUpdate> migrator, out ProductUpdate update)
        {
            var ttl = node.Policies.TimeToLive;
            var isTarget = isTargetPredict(ttl);
            var needForceMigration = node.ChangeTable.TtlPolicy.NeedMigrate;
            var needMigartion = isTarget || needForceMigration;

            update = !needMigartion ? null : new ProductUpdate()
            {
                Id = node.Id,
                TTLPolicy = isTarget ? migrator(ToUpdate(ttl)) : ToUpdate(ttl),
                Initiator = needForceMigration ? _forceMigrator : _softMigrator,
            };

            return needMigartion;
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

        private static bool TryMigrateProductDefaultChatToFolder(ProductModel product, out ProductUpdate update)
        {
            static bool IsTarget(BaseNodeModel node) => node is ProductModel product && product.Parent == null && product.FolderId != null;

            return TryMigrateNodeDefaultChatToParent(product, IsTarget, DefaultChatsMode.FromFolder, out update);
        }

        private static bool TryMigrateProductDefaultChatToParent(ProductModel product, out ProductUpdate update)
        {
            static bool IsTarget(BaseNodeModel node) => node is ProductModel product && product.Parent != null;

            return TryMigrateNodeDefaultChatToParent(product, IsTarget, DefaultChatsMode.FromParent, out update);
        }

        private static bool TryMigrateNodeDefaultChatToParent(ProductModel product, Predicate<ProductModel> isTarget, DefaultChatsMode mode, out ProductUpdate update)
        {
            if (product.Settings.DefaultChats.CurValue.IsNotInitialized && isTarget(product))
            {
                update = new ProductUpdate
                {
                    Id = product.Id,
                    DefaultChats = new PolicyDestinationSettings(mode),
                    Initiator = _softMigrator,
                };

                return true;
            }

            update = null;

            return false;
        }


        private static bool IsDefaultSensor(BaseSensorModel sensor) => IsComputerSensor(sensor) || IsModuleSensor(sensor);

        private static bool IsComputerSensor(BaseSensorModel sensor) => sensor.Path.Contains(".computer");

        private static bool IsModuleSensor(BaseSensorModel sensor) => sensor.Path.Contains(".module");


        private static bool IsNumberSensor(SensorType type) => type.IsBar() || type is SensorType.Integer or SensorType.Double or SensorType.Rate;

        private static bool IsBoolSensor(SensorType type) => type is SensorType.Boolean;


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

                Initiator = _forceMigrator,
            };

        private static PolicyScheduleUpdate ToUpdate(PolicySchedule schedule) =>
            new()
            {
                InstantSend = schedule.InstantSend,
                RepeatMode = schedule.RepeatMode,
                Time = schedule.Time,
            };

        private static PolicyDestinationUpdate ToUpdate(PolicyDestination destination) => new(destination);

        private static PolicyConditionUpdate ToUpdate(PolicyCondition condition) =>
            new()
            {
                Operation = condition.Operation,
                Target = condition.Target,
                Property = condition.Property
            };


        private static PolicyScheduleUpdate GetDefaultScheduleUpdate(bool instantSend = true) => new()
        {
            RepeatMode = AlertRepeatMode.Hourly,
            InstantSend = instantSend,
            Time = new DateTime(1, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };

        private static PolicyUpdate ToFromParentDestination(PolicyUpdate update) =>
            update with { Destination = new PolicyDestinationUpdate(PolicyDestinationMode.FromParent) };
    }
}
