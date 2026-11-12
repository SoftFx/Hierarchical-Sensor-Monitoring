using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Model.Sensors;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.Cache
{
    // Sensor move and aliases (#1718, ADR-0018). Canonical description:
    // aicontext/features/server/sensor-move/feature.md.
    //
    // A move keeps the sensor's Guid — history, journal, dashboards, policies
    // and undo references are all keyed by it — and rewrites only the entity's
    // parent and name. The old path becomes an alias: a second, per-product
    // ingestion index (CachedValue.Aliases) consulted only when the real index
    // misses, so the hot path pays nothing for it.
    //
    // Concurrency: a move runs on the queue of the sensor's CURRENT root
    // product, so ingestion into the sensor through that root is serialized
    // with it. A cross-product move also touches the destination root's index
    // and nodes, which that root's own queue mutates when it creates a sensor;
    // both take _treeStructureLock, and sensor creation re-checks the index
    // under it. The moved sensor is published in the destination index LAST,
    // after every mutation of the model, so the destination queue never sees
    // a half-moved sensor.
    public sealed partial class TreeValuesCache
    {
        private const string MovedConcurrentlyError = "The sensor was moved or removed concurrently; retry.";

        // Bound on the re-dispatch of an aliased value whose sensor keeps
        // moving between queues: past it the value is dropped and logged.
        private const int MaxAliasHops = 3;

        private readonly object _treeStructureLock = new();

        private long _aliasValuesDropped;


        /// <summary>Raised after a sensor moved; the second argument is the old parent node id.</summary>
        public event Action<BaseSensorModel, Guid> SensorMovedEvent;


        public async Task<SensorMoveResult> MoveSensorAsync(Guid sensorId, Guid destinationProductId, string destinationPath,
            InitiatorInfo initiator = null, CancellationToken token = default)
        {
            if (!TryGetSensorById(sensorId, out var sensor))
                return new SensorMoveResult(SensorMoveOutcome.SensorNotFound, $"Sensor {sensorId} was not found.");

            var request = new SensorMoveRequest(sensorId, sensor.Root.Id, destinationProductId, destinationPath,
                initiator ?? InitiatorInfo.System);

            var queued = await ProcessRequestAsync(request.SourceRootId, request, token);

            return request.Result ?? new SensorMoveResult(SensorMoveOutcome.Failed, queued.IsOk ? MovedConcurrentlyError : queued.Error);
        }

        public async Task<bool> RemoveSensorAliasAsync(Guid sensorId, Guid productId, string path,
            InitiatorInfo initiator = null, CancellationToken token = default)
        {
            if (!TryGetSensorById(sensorId, out var sensor))
                return false;

            var request = new SensorAliasMaintenanceRequest(sensorId, DateTime.UtcNow)
            {
                RemoveProductId = productId,
                RemovePath = new SensorPathCheck(productId, path ?? string.Empty).Path,
                Initiator = initiator ?? InitiatorInfo.System,
            };

            await ProcessRequestAsync(sensor.Root.Id, request, token);

            return request.Removed;
        }

        // The hourly sweep's alias pass: expiry and hit persistence run on each
        // sensor's own queue, so the entity write stays single-writer.
        internal async Task RunAliasMaintenanceAsync(CancellationToken token = default)
        {
            var expiredBefore = _aliasesExpired;

            foreach (var sensor in _sensorsById.Values)
            {
                if (token.IsCancellationRequested)
                    return;

                if (sensor.Aliases.Count == 0)
                    continue;

                await ProcessRequestAsync(sensor.Root.Id, new SensorAliasMaintenanceRequest(sensor.Id, DateTime.UtcNow), token);
            }

            var expired = _aliasesExpired - expiredBefore;
            if (expired > 0)
                _logger.Info($"Alias sweep: {expired} sensor alias(es) expired by the sensors' SelfDestroy interval");
        }

        private long _aliasesExpired;


        private void MoveSensor(SensorMoveRequest request)
        {
            try
            {
                request.Result = MoveSensorUnderLock(request);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Sensor move failed: {request.SensorId} -> {request.DestinationProductId}/{request.DestinationPath}");
                request.Result = new SensorMoveResult(SensorMoveOutcome.Failed, ex.Message);
            }
        }

        private SensorMoveResult MoveSensorUnderLock(SensorMoveRequest request)
        {
            lock (_treeStructureLock)
            {
                // The request was enqueued on the sensor's root at that time; a
                // sensor moved away since then belongs to another queue now.
                if (!TryGetSensorById(request.SensorId, out var sensor) || sensor.Parent is null)
                    return new SensorMoveResult(SensorMoveOutcome.SensorNotFound, $"Sensor {request.SensorId} was not found.");

                if (sensor.Root.Id != request.SourceRootId)
                    return new SensorMoveResult(SensorMoveOutcome.Failed, MovedConcurrentlyError);

                var pathCheck = new SensorPathCheck(request.DestinationProductId, request.DestinationPath ?? string.Empty);

                if (!pathCheck.TryCheckRequest(out var pathError))
                    return new SensorMoveResult(SensorMoveOutcome.InvalidPath, pathError);

                if (!TryGetProduct(request.DestinationProductId, out var destinationRoot) || !destinationRoot.IsRoot)
                    return new SensorMoveResult(SensorMoveOutcome.DestinationNotFound, $"Product {request.DestinationProductId} was not found.");

                var oldParent = sensor.Parent;
                var oldRoot = sensor.Root;
                var oldName = sensor.DisplayName;
                var oldPath = sensor.Path;
                var oldFullPath = sensor.FullPath;
                var newPath = pathCheck.Path;
                var newFullPath = $"{destinationRoot.DisplayName}/{newPath}";

                if (oldRoot.Id == destinationRoot.Id && string.Equals(oldPath, newPath, StringComparison.Ordinal))
                    return new SensorMoveResult(SensorMoveOutcome.Unchanged) { OldPath = oldFullPath, NewPath = newFullPath };

                var destinationCache = GetOrAddCachedValue(destinationRoot);

                if (destinationCache.Sensors.TryGetValue(newPath, out var occupant))
                    return new SensorMoveResult(SensorMoveOutcome.Conflict,
                        $"Sensor '{newFullPath}' already exists (id {occupant.Id}).") { OldPath = oldFullPath, NewPath = newFullPath };

                var matchedBefore = _alertTemplates.Values.Where(t => t.IsMatch(sensor)).Select(t => t.Id).ToHashSet();

                // Missing nodes are created exactly as ingestion creates them;
                // an empty one left behind by a failed move is swept like any
                // other empty node.
                var newParent = AddNonExistingProductsAndGetParentProduct(destinationRoot, pathCheck);

                var oldCache = GetOrAddCachedValue(oldRoot);
                oldCache.Sensors.TryRemove(new KeyValuePair<string, BaseSensorModel>(oldPath, sensor));

                oldParent.DetachMovedSensor(sensor);
                sensor.MoveTo(newParent, pathCheck.SensorName);
                newParent.AttachMovedSensor(sensor);

                // Moving back onto one of its own aliases retires that alias.
                var retiredAlias = sensor.RemoveAlias(destinationRoot.Id, newPath);
                var newAlias = new SensorAlias(oldRoot.Id, oldPath, DateTime.UtcNow);
                sensor.AddAlias(newAlias);

                try
                {
                    _database.UpdateSensor(sensor.ToEntity());
                }
                catch
                {
                    // Nothing was persisted: restore the in-memory model.
                    sensor.RemoveAlias(oldRoot.Id, oldPath);
                    if (retiredAlias is not null)
                        sensor.AddAlias(retiredAlias);

                    newParent.DetachMovedSensor(sensor);
                    sensor.MoveTo(oldParent, oldName);
                    oldParent.AttachMovedSensor(sensor);
                    oldCache.Sensors.TryAdd(oldPath, sensor);

                    throw;
                }

                // A real sensor now owns the destination path: any alias another
                // sensor kept there is displaced (ADR-0018).
                if (destinationCache.Aliases.TryRemove(newPath, out var displaced) && displaced.Id != sensor.Id)
                    DispatchAliasRemoval(displaced, destinationRoot.Id, newPath, "Displaced by a moved sensor");

                ReevaluateTemplates(sensor, matchedBefore);

                // From here on the move is persisted: nothing below may keep the
                // sensor out of the index, so every side effect is isolated.
                try
                {
                    _journalService.AddRecord(new JournalRecordModel(sensor.Id, request.Initiator)
                    {
                        Enviroment = "Move sensor",
                        PropertyName = "Path",
                        OldValue = oldFullPath,
                        NewValue = newFullPath,
                        Path = newFullPath,
                        Kind = JournalKinds.TreeCrud,
                        ProductId = destinationRoot.Id.ToString(),
                    });
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Journaling the move of sensor {sensor.Id} failed");
                }

                _logger.Info($"Sensor moved: Id = {sensor.Id}, {oldFullPath} -> {newFullPath}, Initiator = {request.Initiator}");

                // Publish last (see the class comment).
                oldCache.Aliases[oldPath] = sensor;
                destinationCache.Sensors[newPath] = sensor;

                try
                {
                    SensorMovedEvent?.Invoke(sensor, oldParent.Id);
                    SensorUpdateView(sensor);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"A subscriber failed on the move of sensor {sensor.Id}");
                }

                return new SensorMoveResult(SensorMoveOutcome.Moved) { OldPath = oldFullPath, NewPath = newFullPath };
            }
        }

        // "As if the sensor had always been there" (#1718 decision 5): templates
        // that stopped matching are detached, newly matching ones applied.
        // Templates matching both before and after are left alone — re-applying
        // them would only churn the journal.
        private void ReevaluateTemplates(BaseSensorModel sensor, HashSet<Guid> matchedBefore)
        {
            foreach (var template in _alertTemplates.Values.OrderBy(t => t.Id))
            {
                try
                {
                    var matchesNow = template.IsMatch(sensor);

                    if (matchesNow && !matchedBefore.Contains(template.Id))
                        ApplyTemplateToSensor(new ApplyTemplateRequest(sensor.Id, template));
                    else if (!matchesNow && matchedBefore.Contains(template.Id))
                        RemoveTemplateFromSensor(new RemoveTemplateFromSensorRequest(sensor.Id, template.Id));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Template {template.Id} re-evaluation failed for moved sensor {sensor.Id}");
                }
            }
        }


        private enum AliasResolution
        {
            None,
            Local,
            Forwarded,
        }

        // Called only after the real index missed. `type` is the incoming
        // value's or registration's type when it has one: a different type is
        // not the moved sensor any more, so it displaces the alias and the
        // caller creates a sensor as usual.
        private AliasResolution ResolveAlias(Guid productId, string path, SensorType? type, out BaseSensorModel sensor)
        {
            sensor = null;

            if (!_cache.TryGetValue(productId, out var cachedValue) || !cachedValue.Aliases.TryGetValue(path, out var target))
                return AliasResolution.None;

            var alias = TryGetSensorById(target.Id, out _) ? target.GetAlias(productId, path) : null;

            if (alias is null)
            {
                // Stale entry: the sensor was removed or dropped the alias.
                cachedValue.Aliases.TryRemove(new KeyValuePair<string, BaseSensorModel>(path, target));
                return AliasResolution.None;
            }

            if (type.HasValue && type.Value != target.Type)
            {
                cachedValue.Aliases.TryRemove(new KeyValuePair<string, BaseSensorModel>(path, target));
                DispatchAliasRemoval(target, productId, path, $"Displaced by a {type.Value} sensor");

                return AliasResolution.None;
            }

            alias.Hit(DateTime.UtcNow);
            sensor = target;

            return target.Root.Id == productId ? AliasResolution.Local : AliasResolution.Forwarded;
        }

        private void AddAliasValue(AliasValueRequest request)
        {
            if (!TryGetSensorById(request.SensorId, out var sensor))
            {
                CountDroppedAliasValue($"sensor {request.SensorId} was removed");
                return;
            }

            if (sensor.Root.Id != request.ExpectedRootId)
            {
                // Moved again while the value was in flight: follow it.
                if (request.Hops >= MaxAliasHops)
                    CountDroppedAliasValue($"sensor {request.SensorId} kept moving");
                else
                    Dispatch(sensor.Root, request with { ExpectedRootId = sensor.Root.Id, Hops = request.Hops + 1 });

                return;
            }

            AddValueToSensor(sensor, sensor.Path, request.Value);
        }

        private void CountDroppedAliasValue(string reason) =>
            CountAndWarnRateLimited(ref _aliasValuesDropped, () => $"Aliased value dropped: {reason}");

        private void MaintainAliases(SensorAliasMaintenanceRequest request)
        {
            if (!TryGetSensorById(request.SensorId, out var sensor))
                return;

            var changed = false;

            if (request.RemoveProductId is { } removeProductId)
            {
                if (sensor.RemoveAlias(removeProductId, request.RemovePath) is not null)
                {
                    UnindexAlias(sensor, removeProductId, request.RemovePath);

                    _journalService.AddRecord(new JournalRecordModel(sensor.Id, request.Initiator)
                    {
                        Enviroment = "Remove sensor alias",
                        PropertyName = "Alias",
                        OldValue = AliasFullPath(removeProductId, request.RemovePath),
                        Path = sensor.FullPath,
                        Kind = JournalKinds.TreeCrud,
                        ProductId = sensor.Root.Id.ToString(),
                    });

                    request.Removed = changed = true;
                }
            }
            else
            {
                var interval = sensor.Settings.SelfDestroy.Value;

                foreach (var alias in sensor.Aliases)
                {
                    // An alias of a deleted product can never be hit again.
                    var orphaned = !TryGetProduct(alias.ProductId, out _);

                    if (orphaned || AliasExpired(interval, alias.LastActivity, request.UtcNow))
                    {
                        sensor.RemoveAlias(alias.ProductId, alias.Path);
                        UnindexAlias(sensor, alias.ProductId, alias.Path);

                        Interlocked.Increment(ref _aliasesExpired);
                        _logger.Info($"Sensor alias removed ({(orphaned ? "product deleted" : "SelfDestroy interval elapsed")}): " +
                                     $"{AliasFullPath(alias.ProductId, alias.Path)} -> [{sensor.Id}] {sensor.FullPath}");

                        changed = true;
                    }
                    else if (alias.HasUnpersistedHit)
                        changed = true;
                }
            }

            if (changed)
                _database.UpdateSensor(sensor.ToEntity());
        }

        // Mirrors BaseSensorModel.ShouldDestroy's interval states (None never
        // fires, a non-positive tick interval never fires) on a caller clock.
        private bool AliasExpired(TimeIntervalModel interval, DateTime lastActivity, DateTime utcNow)
        {
            if (interval is null || interval.IsNone || (interval.UseTicks && interval.Ticks <= 0))
                return false;

            try
            {
                return interval.UseTicks
                    ? (utcNow - lastActivity).Ticks > interval.Ticks
                    : utcNow > interval.GetShiftedTime(lastActivity);
            }
            catch (Exception ex)
            {
                // A legacy interval GetShiftedTime cannot shift: keep the alias.
                _logger.Warn($"Alias expiry check skipped for interval {interval}: {ex.Message}");
                return false;
            }
        }

        private void UnindexAlias(BaseSensorModel sensor, Guid productId, string path)
        {
            if (_cache.TryGetValue(productId, out var cachedValue))
                cachedValue.Aliases.TryRemove(new KeyValuePair<string, BaseSensorModel>(path, sensor));
        }

        // Every alias of a removed sensor leaves the ingestion index with it.
        private void UnindexAliases(BaseSensorModel sensor)
        {
            foreach (var alias in sensor.Aliases)
                UnindexAlias(sensor, alias.ProductId, alias.Path);
        }

        // Startup: aliases enter the index after every real sensor did, and
        // one shadowed by a real sensor (or by another alias) is dropped.
        private void IndexAliasesOnStartup()
        {
            foreach (var sensor in _sensorsById.Values)
            {
                var aliases = sensor.Aliases;
                if (aliases.Count == 0)
                    continue;

                var dropped = false;

                foreach (var alias in aliases)
                {
                    if (TryGetProduct(alias.ProductId, out var root) && root.IsRoot)
                    {
                        var cachedValue = GetOrAddCachedValue(root);

                        if (!cachedValue.Sensors.ContainsKey(alias.Path) && cachedValue.Aliases.TryAdd(alias.Path, sensor))
                            continue;
                    }

                    _logger.Warn($"Sensor alias dropped on startup (product gone or path taken): {AliasFullPath(alias.ProductId, alias.Path)} -> [{sensor.Id}] {sensor.FullPath}");
                    sensor.RemoveAlias(alias.ProductId, alias.Path);
                    dropped = true;
                }

                if (dropped)
                {
                    try
                    {
                        _database.UpdateSensor(sensor.ToEntity());
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Persisting dropped aliases of sensor {sensor.Id} failed");
                    }
                }
            }
        }

        private void DispatchAliasRemoval(BaseSensorModel sensor, Guid productId, string path, string reason) =>
            Dispatch(sensor.Root, new SensorAliasMaintenanceRequest(sensor.Id, DateTime.UtcNow)
            {
                RemoveProductId = productId,
                RemovePath = path,
                Initiator = InitiatorInfo.AsSystemInfo(reason),
            });

        // Fire-and-forget onto another product's queue: a queue handler must
        // never wait on another queue (aicontext/features/server/overview.md).
        private void Dispatch(ProductModel root, IUpdateRequest request) =>
            _ = GetOrAddCachedValue(root).Queue.AddItemAsync(request);

        private CachedValue GetOrAddCachedValue(ProductModel root) =>
            _cache.GetOrAdd(root.Id, _ => new CachedValue(root, this));

        private string AliasFullPath(Guid productId, string path) =>
            TryGetProductNameById(productId, out var name) ? $"{name}/{path}" : $"{productId}/{path}";
    }
}
