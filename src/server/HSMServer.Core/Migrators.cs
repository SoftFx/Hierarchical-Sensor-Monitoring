using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HSMServer.Core
{
    [Obsolete]
    public static class Migrators
    {
        public static TimeIntervalModel ToNewInterval(OldTimeIntervalEntity old, bool folderCrunch = false)
        {
            var oldEnum = (OldTimeInterval)old.TimeInterval;

            if (folderCrunch && old.CustomPeriod == 0 && old.TimeInterval == 0L)
                oldEnum = OldTimeInterval.Custom;

            var newTicks = oldEnum switch
            {
                OldTimeInterval.OneMinute => 600_000_000L,
                OldTimeInterval.FiveMinutes => 3_000_000_000L,
                OldTimeInterval.TenMinutes => 6_000_000_000L,

                OldTimeInterval.Hour => 36_000_000_000L,
                OldTimeInterval.Day => 864_000_000_000L,
                OldTimeInterval.Week => 6_048_000_000_000L,

                _ => old.CustomPeriod,
            };

            var newEnum = oldEnum switch
            {
                OldTimeInterval.TenMinutes or OldTimeInterval.Hour or OldTimeInterval.Day or OldTimeInterval.Week
                or OldTimeInterval.OneMinute or OldTimeInterval.FiveMinutes or OldTimeInterval.Custom => TimeInterval.Ticks,


                OldTimeInterval.Month => TimeInterval.Month,
                OldTimeInterval.ThreeMonths => TimeInterval.ThreeMonths,
                OldTimeInterval.SixMonths => TimeInterval.SixMonths,
                OldTimeInterval.Year => TimeInterval.Year,


                OldTimeInterval.FromFolder => TimeInterval.FromFolder,
                OldTimeInterval.FromParent => TimeInterval.FromParent,
                _ => throw new NotImplementedException(),
            };

            if (newEnum == TimeInterval.Ticks && (newTicks == 0L || newTicks == DateTime.MaxValue.Ticks))
                newEnum = TimeInterval.None;

            return new TimeIntervalModel(newEnum, newTicks);
        }

        public static Dictionary<Guid, T> GetMigrationUpdates<T, U>(List<U> entities, Dictionary<string, JsonObject> rawPolicies, Dictionary<string, PolicyEntity> resavedPolicies)
            where T : BaseNodeUpdate, new()
            where U : BaseNodeEntity
        {
            var updates = new Dictionary<Guid, T>();

            foreach (var entity in entities)
            {
                var entityId = Guid.Parse(entity.Id);

                if (entity.Settings.Count == 0)
                {
                    var newUpd = new T()
                    {
                        Id = entityId,
                    };

                    updates.Add(entityId, newUpd);
                }

                foreach (var policyId in entity.Policies)
                {
                    if (rawPolicies.TryGetValue(policyId, out JsonObject raw) && raw["$type"] is not null)
                    {
                        var policyType = int.Parse(raw["$type"].ToString());

                        if (policyType is 1000 or 1001 or 1002) //TTL, KeepHistory, selfDestroy
                        {
                            var oldInterval = JsonSerializer.Deserialize<OldTimeIntervalEntity>(raw["Interval"]);

                            if (oldInterval is not null)
                            {
                                var newInterval = ToNewInterval(oldInterval);

                                if (updates.TryGetValue(entityId, out var upd))
                                {
                                    if (policyType == 1000)
                                        upd = upd with { TTL = newInterval };
                                    if (policyType == 1001)
                                        upd = upd with { SelfDestroy = newInterval };
                                    if (policyType == 1002)
                                        upd = upd with { KeepHistory = newInterval };

                                    updates[entityId] = upd;
                                }
                            }
                        }

                        if (policyType >= 2001)
                        {
                            var policyIdStr = raw["Id"].ToString();

                            var policyEntity = new PolicyEntity
                            {
                                Conditions = new List<PolicyConditionEntity>()
                                {
                                    new PolicyConditionEntity
                                    {
                                        Target = new PolicyTargetEntity(byte.Parse(raw["Target"]["Type"].ToString()), raw["Target"]["Value"].ToString()),
                                        Property = (byte)Enum.Parse<PolicyProperty>(raw["Property"].ToString()),
                                        Operation = byte.Parse(raw["Operation"].ToString())
                                    }
                                },
                                Id = Guid.Parse(policyIdStr).ToByteArray(),
                                SensorStatus = byte.Parse(raw["Status"].ToString()),
                                Template = raw["Comment"].ToString(),
                                Icon = "↕",
                            };

                            resavedPolicies.Add(policyIdStr, policyEntity);
                        }
                    }
                }

                if (updates.TryGetValue(entityId, out var update))
                {
                    if (update.TTL is null)
                        update = update with { TTL = new TimeIntervalModel() };
                    if (update.SelfDestroy is null)
                        update = update with { SelfDestroy = new TimeIntervalModel() };
                    if (update.KeepHistory is null)
                        update = update with { KeepHistory = new TimeIntervalModel() };

                    updates[entityId] = update;
                }
            }

            return updates;
        }
    }
}