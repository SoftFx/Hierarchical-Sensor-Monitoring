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

        // Absorbed sensor id -> survivor id (#1720), for values forwarded by id
        // to an occupant that a merge removed while they were in flight. One
        // pair per merge, never pruned: merges are operator actions.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Guid> _mergedInto = new();


        /// <summary>Raised after a sensor moved; the second argument is the old parent node id.</summary>
        public event Action<BaseSensorModel, Guid> SensorMovedEvent;


        public async Task<SensorMoveResult> MoveSensorAsync(Guid sensorId, Guid destinationProductId, string destinationPath,
            SensorMoveConflict onConflict = SensorMoveConflict.Fail, InitiatorInfo initiator = null, CancellationToken token = default)
        {
            initiator ??= InitiatorInfo.System;

            // A second pass only for a merge whose occupant appeared after the
            // first look: the caller asked for a merge, not for a conflict.
            for (var attempt = 0; ; attempt++)
            {
                if (!TryGetSensorById(sensorId, out var sensor))
                    return new SensorMoveResult(SensorMoveOutcome.SensorNotFound, $"Sensor {sensorId} was not found.");

                SensorAbsorbRequest absorb = null;

                if (onConflict is SensorMoveConflict.Merge && TryGetOccupant(sensorId, destinationProductId, destinationPath, out var occupant, out var occupantPath))
                {
                    if (occupant.Type != sensor.Type)
                        return TypeConflict(occupant, sensor, $"{occupant.RootProductName}/{occupantPath}");

                    // Merge (#1720), step 1 on the occupant's own queue. Not
                    // cancellable from here on: a merge is not left half-done
                    // by a caller that went away.
                    absorb = new SensorAbsorbRequest(sensorId, occupant.Id, destinationProductId, occupantPath, initiator);
                    var absorbed = await ProcessRequestAsync(destinationProductId, absorb, CancellationToken.None);

                    absorb.Error ??= absorbed.IsOk ? null : absorbed.Error;

                    if (absorb.Vanished)
                        absorb = null;
                    else if (absorb.Error is not null && !absorb.Absorbed)
                        return new SensorMoveResult(absorb.Conflict ?? SensorMoveOutcome.Failed, absorb.Error);
                    else
                        token = CancellationToken.None;
                }

                var request = new SensorMoveRequest(sensorId, sensor.Root.Id, destinationProductId, destinationPath, initiator);
                var queued = await ProcessRequestAsync(request.SourceRootId, request, token);
                var result = request.Result ?? new SensorMoveResult(SensorMoveOutcome.Failed, queued.IsOk ? MovedConcurrentlyError : queued.Error);

                if (absorb is null)
                {
                    if (result.Outcome is SensorMoveOutcome.Conflict && onConflict is SensorMoveConflict.Merge && attempt == 0)
                        continue;

                    return result;
                }

                return await CompleteMergeAsync(sensorId, absorb, result);
            }
        }

        // A read-only mirror of the move's validation (MoveSensorUnderLock) and
        // of what CompleteMove/merge would change. Lock-free: a prediction, the
        // real move re-validates everything.
        public SensorMovePreview PreviewMove(Guid sensorId, Guid destinationProductId, string destinationPath,
            SensorMoveConflict onConflict = SensorMoveConflict.Fail)
        {
            if (!TryGetSensorById(sensorId, out var sensor) || sensor.Parent is null)
                return new SensorMovePreview(SensorMoveOutcome.SensorNotFound, $"Sensor {sensorId} was not found.");

            var check = new SensorPathCheck(destinationProductId, destinationPath ?? string.Empty);

            if (!check.TryCheckRequest(out var pathError))
                return new SensorMovePreview(SensorMoveOutcome.InvalidPath, pathError);

            if (!TryGetProduct(destinationProductId, out var root) || !root.IsRoot)
                return new SensorMovePreview(SensorMoveOutcome.DestinationNotFound, $"Product {destinationProductId} was not found.");

            var oldPath = sensor.FullPath;
            var newPath = $"{root.DisplayName}/{check.Path}";

            if (sensor.Root.Id == root.Id && string.Equals(sensor.Path, check.Path, StringComparison.Ordinal))
                return new SensorMovePreview(SensorMoveOutcome.Unchanged) { OldPath = oldPath, NewPath = newPath };

            var outcome = SensorMoveOutcome.Moved;
            Guid? occupantId = null;

            if (TryGetSensorFromCache(root.Id, check.Path, out var occupant) && occupant.Id != sensor.Id)
            {
                occupantId = occupant.Id;

                if (onConflict is not SensorMoveConflict.Merge)
                    return new SensorMovePreview(SensorMoveOutcome.Conflict, $"Sensor '{newPath}' already exists (id {occupant.Id}).")
                    { OldPath = oldPath, NewPath = newPath, OccupantId = occupantId };

                if (occupant.Type != sensor.Type)
                    return new SensorMovePreview(SensorMoveOutcome.Conflict, TypeConflict(occupant, sensor, newPath).Error)
                    { OldPath = oldPath, NewPath = newPath, OccupantId = occupantId };

                outcome = SensorMoveOutcome.Merged;
            }

            // The deepest existing node on the way, and the ones to create.
            var parent = root;
            var created = new List<string>();
            var parts = check.PathParts;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                var next = created.Count == 0
                    ? parent.SubProducts.Values.FirstOrDefault(p => p.DisplayName == parts[i])
                    : null;

                if (next is null)
                    created.Add($"{root.DisplayName}/{string.Join('/', parts[..(i + 1)])}");
                else
                    parent = next;
            }

            var attached = sensor.Policies.Select(p => p.TemplateId)
                .Concat(sensor.Policies.TTLPolicies.Select(t => t.TemplateId))
                .OfType<Guid>()
                .ToHashSet();

            var detached = new List<(Guid, string)>();
            var applied = new List<(Guid, string)>();

            foreach (var template in _alertTemplates.Values.OrderBy(t => t.Id))
            {
                var matches = template.IsMatch(newPath, sensor.Type, root.FolderId);

                if (matches && !attached.Contains(template.Id))
                    applied.Add((template.Id, template.Name));
                else if (!matches && attached.Contains(template.Id))
                    detached.Add((template.Id, template.Name));
            }

            // A setting the sensor does not set itself resolves through the
            // parent chain: at the destination, the deepest existing node's
            // chain (nodes the move creates inherit from it as well).
            var settings = new List<(string, string, string)>();

            void Compare(string name, Model.NodeSettings.TimeIntervalSettingProperty own, Model.NodeSettings.TimeIntervalSettingProperty destination)
            {
                if (own.IsSet)
                    return;

                var before = own.Value?.ToString();
                var after = destination.Value?.ToString();

                if (!string.Equals(before, after, StringComparison.Ordinal))
                    settings.Add((name, before, after));
            }

            Compare("TTL", sensor.Settings.TTL, parent.Settings.TTL);
            Compare("KeepHistory", sensor.Settings.KeepHistory, parent.Settings.KeepHistory);
            Compare("SelfDestroy", sensor.Settings.SelfDestroy, parent.Settings.SelfDestroy);

            return new SensorMovePreview(outcome)
            {
                OldPath = oldPath,
                NewPath = newPath,
                OccupantId = occupantId,
                CreatedNodes = created,
                TemplatesDetached = detached,
                TemplatesApplied = applied,
                SettingsChanged = settings,
            };
        }

        private bool TryGetOccupant(Guid sensorId, Guid productId, string destinationPath, out BaseSensorModel occupant, out string path)
        {
            var check = new SensorPathCheck(productId, destinationPath ?? string.Empty);
            path = check.Path;
            occupant = null;

            return check.TryCheckRequest(out _) && TryGetSensorFromCache(productId, path, out occupant) && occupant.Id != sensorId;
        }

        // After step 1 the occupant is gone and its path and aliases point at
        // the survivor in memory: make sure the survivor's entity carries them
        // (the move's own write does when it succeeds), then refresh its cache.
        private async Task<SensorMoveResult> CompleteMergeAsync(Guid sensorId, SensorAbsorbRequest absorb, SensorMoveResult moved)
        {
            var survivorExists = TryGetSensorById(sensorId, out var survivor);

            if (!moved.IsOk)
            {
                if (survivorExists)
                    await ProcessRequestAsync(survivor.Root.Id, new SensorAliasMaintenanceRequest(sensorId, survivor.Root.Id, DateTime.UtcNow)
                    {
                        ForcePersist = true,
                        Redispatch = true,
                    }, CancellationToken.None);

                var next = survivorExists
                    ? "Its path already routes to this sensor; retry the move."
                    : "This sensor no longer exists, so neither does the merged history.";

                return moved with
                {
                    Outcome = SensorMoveOutcome.Failed,
                    Error = $"Sensor {absorb.OccupantId} was merged into this sensor, but the move failed: {moved.Error} {next}",
                    MergedSensorId = absorb.OccupantId,
                    MergedValues = absorb.Copied,
                    MergeCollisions = absorb.Collisions,
                };
            }

            // Step 3 on the queue that owns the survivor now.
            if (survivorExists)
                await ProcessRequestAsync(survivor.Root.Id, new SensorMergeRefreshRequest(sensorId, survivor.Root.Id, Hops: 0), CancellationToken.None);

            return moved with
            {
                Outcome = SensorMoveOutcome.Merged,
                Error = absorb.Error,
                MergedSensorId = absorb.OccupantId,
                MergedValues = absorb.Copied,
                MergeCollisions = absorb.Collisions,
            };
        }

        private static SensorMoveResult TypeConflict(BaseSensorModel occupant, BaseSensorModel sensor, string fullPath) =>
            new(SensorMoveOutcome.Conflict,
                $"Sensor '{fullPath}' (id {occupant.Id}) is {occupant.Type}, the moved sensor is {sensor.Type}: sensors of different types are never merged.");

        public async Task<bool> RemoveSensorAliasAsync(Guid sensorId, Guid productId, string path,
            InitiatorInfo initiator = null, CancellationToken token = default)
        {
            var removePath = new SensorPathCheck(productId, path ?? string.Empty).Path;

            // A sensor that moves between the lookup and the handler is chased
            // to its new queue a bounded number of times.
            for (var attempt = 0; attempt <= MaxAliasHops; attempt++)
            {
                if (!TryGetSensorById(sensorId, out var sensor))
                    return false;

                var request = new SensorAliasMaintenanceRequest(sensorId, sensor.Root.Id, DateTime.UtcNow)
                {
                    RemoveProductId = productId,
                    RemovePath = removePath,
                    Initiator = initiator ?? InitiatorInfo.System,
                };

                await ProcessRequestAsync(sensor.Root.Id, request, token);

                if (!request.Misrouted)
                    return request.Removed;
            }

            _logger.Warn($"Alias removal gave up: sensor {sensorId} kept moving between products ({productId}/{removePath})");
            return false;
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

                // A sensor that moved meanwhile is simply picked up next hour.
                var root = sensor.Root.Id;
                await ProcessRequestAsync(root, new SensorAliasMaintenanceRequest(sensor.Id, root, DateTime.UtcNow), token);
            }

            var expired = _aliasesExpired - expiredBefore;
            if (expired > 0)
                _logger.Info($"Alias sweep: {expired} sensor alias(es) expired by the sensors' SelfDestroy interval");
        }

        private long _aliasesExpired;


        // What a published move hands to its post-publish side effects.
        private sealed record PublishedMove(BaseSensorModel Sensor, ProductModel OldParent, ProductModel DestinationRoot,
            string OldFullPath, string NewFullPath);


        private void MoveSensor(SensorMoveRequest request)
        {
            PublishedMove published;

            try
            {
                (request.Result, published) = MoveSensorUnderLock(request);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Sensor move failed: {request.SensorId} -> {request.DestinationProductId}/{request.DestinationPath}");
                request.Result = new SensorMoveResult(SensorMoveOutcome.Failed, ex.Message);
                return;
            }

            if (published is not null)
                CompleteMove(published, request.Initiator);
        }

        // Outside the tree-structure lock: nothing here changes the tree's
        // structure, and holding the lock across template applies, the journal
        // and UI subscribers would stall sensor creation on every product.
        // The move is already persisted and published, so every step is isolated.
        private void CompleteMove(PublishedMove move, InitiatorInfo initiator)
        {
            var sensor = move.Sensor;

            // Templates mutate the sensor's policies, so they run on the queue
            // that owns the sensor now: inline for a move inside the product,
            // on the destination's queue for a move across products.
            try
            {
                if (move.OldParent.Root.Id == move.DestinationRoot.Id)
                    ReevaluateTemplates(sensor);
                else
                    Dispatch(move.DestinationRoot, new SensorTemplateReevaluationRequest(sensor.Id, move.DestinationRoot.Id, Hops: 0));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Template re-evaluation of moved sensor {sensor.Id} failed");
            }

            try
            {
                _journalService.AddRecord(new JournalRecordModel(sensor.Id, initiator)
                {
                    Enviroment = "Move sensor",
                    PropertyName = "Path",
                    OldValue = move.OldFullPath,
                    NewValue = move.NewFullPath,
                    Path = move.NewFullPath,
                    Kind = JournalKinds.TreeCrud,
                    ProductId = move.DestinationRoot.Id.ToString(),
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Journaling the move of sensor {sensor.Id} failed");
            }

            _logger.Info($"Sensor moved: Id = {sensor.Id}, {move.OldFullPath} -> {move.NewFullPath}, Initiator = {initiator}");

            try
            {
                SensorMovedEvent?.Invoke(sensor, move.OldParent.Id);
                SensorUpdateView(sensor);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"A subscriber failed on the move of sensor {sensor.Id}");
            }
        }

        private (SensorMoveResult, PublishedMove) MoveSensorUnderLock(SensorMoveRequest request)
        {
            lock (_treeStructureLock)
            {
                // The request was enqueued on the sensor's root at that time; a
                // sensor moved away since then belongs to another queue now.
                if (!TryGetSensorById(request.SensorId, out var sensor) || sensor.Parent is null)
                    return (new SensorMoveResult(SensorMoveOutcome.SensorNotFound, $"Sensor {request.SensorId} was not found."), null);

                if (sensor.Root.Id != request.SourceRootId)
                    return (new SensorMoveResult(SensorMoveOutcome.Failed, MovedConcurrentlyError), null);

                var pathCheck = new SensorPathCheck(request.DestinationProductId, request.DestinationPath ?? string.Empty);

                if (!pathCheck.TryCheckRequest(out var pathError))
                    return (new SensorMoveResult(SensorMoveOutcome.InvalidPath, pathError), null);

                if (!TryGetProduct(request.DestinationProductId, out var destinationRoot) || !destinationRoot.IsRoot)
                    return (new SensorMoveResult(SensorMoveOutcome.DestinationNotFound, $"Product {request.DestinationProductId} was not found."), null);

                var oldParent = sensor.Parent;
                var oldRoot = sensor.Root;
                var oldName = sensor.DisplayName;
                var oldPath = sensor.Path;
                var oldFullPath = sensor.FullPath;
                var newPath = pathCheck.Path;
                var newFullPath = $"{destinationRoot.DisplayName}/{newPath}";

                if (oldRoot.Id == destinationRoot.Id && string.Equals(oldPath, newPath, StringComparison.Ordinal))
                    return (new SensorMoveResult(SensorMoveOutcome.Unchanged) { OldPath = oldFullPath, NewPath = newFullPath }, null);

                var destinationCache = GetOrAddCachedValue(destinationRoot);

                if (destinationCache.Sensors.TryGetValue(newPath, out var occupant))
                    return (new SensorMoveResult(SensorMoveOutcome.Conflict,
                        $"Sensor '{newFullPath}' already exists (id {occupant.Id}).") { OldPath = oldFullPath, NewPath = newFullPath }, null);

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
                    WriteSensorEntity(sensor);
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

                // Publish last (see the class comment).
                oldCache.Aliases[oldPath] = sensor;
                destinationCache.Sensors[newPath] = sensor;

                return (new SensorMoveResult(SensorMoveOutcome.Moved) { OldPath = oldFullPath, NewPath = newFullPath },
                    new PublishedMove(sensor, oldParent, destinationRoot, oldFullPath, newFullPath));
            }
        }

        // "As if the sensor had always been there" (#1718 decision 5): attached
        // templates that no longer match are detached, matching ones not yet
        // attached are applied, attached and matching ones are left alone —
        // re-applying them would only churn the journal. Judged by what is
        // ATTACHED, not by what matched at the old path, so a re-evaluation
        // that was lost (a sensor moved faster than its follow-ups) is
        // repaired by the next one.
        private void ReevaluateTemplates(BaseSensorModel sensor)
        {
            var attached = sensor.Policies.Select(p => p.TemplateId)
                .Concat(sensor.Policies.TTLPolicies.Select(t => t.TemplateId))
                .OfType<Guid>()
                .ToHashSet();

            foreach (var template in _alertTemplates.Values.OrderBy(t => t.Id))
            {
                try
                {
                    var matchesNow = template.IsMatch(sensor);
                    var isAttached = attached.Contains(template.Id);

                    if (matchesNow && !isAttached)
                        ApplyTemplateToSensor(new ApplyTemplateRequest(sensor.Id, template));
                    else if (!matchesNow && isAttached)
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
                // Forwarded to a sensor a merge absorbed meanwhile: its data
                // lives in the survivor now, and so does this value.
                if (_mergedInto.TryGetValue(request.SensorId, out var survivorId) && request.Hops < MaxAliasHops &&
                    TryGetSensorById(survivorId, out var survivor))
                    Dispatch(survivor.Root, request with { SensorId = survivorId, ExpectedRootId = survivor.Root.Id, Hops = request.Hops + 1 });
                else
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

        // Hand every alias of `from` to `to` and re-point their index entries.
        // Under the tree-structure lock.
        private void AdoptAliases(BaseSensorModel from, BaseSensorModel to)
        {
            foreach (var alias in from.Aliases)
            {
                if (from.RemoveAlias(alias.ProductId, alias.Path) is null)
                    continue;

                to.AddAlias(alias);

                if (_cache.TryGetValue(alias.ProductId, out var cachedValue))
                    cachedValue.Aliases.TryUpdate(alias.Path, to, from);
            }
        }

        // Merge step 1 (#1720), on the occupant's own queue: no value of the
        // occupant is being added while this runs, and values already queued
        // behind it resolve through the alias this step installs. Until the
        // occupant leaves the index nothing is lost on a failure (copied rows
        // only fill ticks the survivor did not have); after that the survivor
        // owns the path through an alias, so the occupant's data has a home
        // whatever happens to the move that follows.
        private void AbsorbSensor(SensorAbsorbRequest request)
        {
            try
            {
                if (!TryGetSensorById(request.OccupantId, out var occupant) || occupant.Root.Id != request.RootId ||
                    !_cache.TryGetValue(request.RootId, out var cachedValue) ||
                    !cachedValue.Sensors.TryGetValue(request.Path, out var indexed) || indexed.Id != occupant.Id)
                {
                    // Removed or moved away since the caller looked: the path is
                    // free (or someone else's — the move will say) — no merge.
                    request.Vanished = true;
                    return;
                }

                if (!TryGetSensorById(request.SurvivorId, out var survivor))
                {
                    request.Error = $"Sensor {request.SurvivorId} was not found.";
                    return;
                }

                if (survivor.Type != occupant.Type)
                {
                    request.Error = TypeConflict(occupant, survivor, occupant.FullPath).Error;
                    request.Conflict = SensorMoveOutcome.Conflict;
                    return;
                }

                // A bar's in-progress bar exists only in memory: store it so it
                // is copied with the rest.
                if (occupant is IBarSensor bar && bar.LocalLastValue != default)
                    SaveSensorValueToDb(bar.LocalLastValue, occupant.Id);

                (request.Copied, request.Collisions) = _database.MergeSensorValues(occupant.Id, survivor.Id);

                lock (_treeStructureLock)
                {
                    cachedValue.Sensors.TryRemove(new KeyValuePair<string, BaseSensorModel>(request.Path, occupant));
                    cachedValue.Aliases[request.Path] = survivor;

                    survivor.AddAlias(new SensorAlias(request.RootId, request.Path, DateTime.UtcNow));
                    AdoptAliases(occupant, survivor);
                }

                request.Absorbed = true;
                _mergedInto[occupant.Id] = survivor.Id;

                if (request.Collisions > 0)
                    _logger.Warn($"Sensor merge: {request.Collisions} row(s) of [{occupant.Id}] dropped, the survivor [{survivor.Id}] had a row at the same tick");

                try
                {
                    _journalService.AddRecord(new JournalRecordModel(survivor.Id, request.Initiator)
                    {
                        Enviroment = "Merge sensor",
                        PropertyName = "History",
                        OldValue = $"{occupant.FullPath} ({occupant.Id})",
                        NewValue = $"{request.Copied} value(s) taken over, {request.Collisions} same-tick collision(s) kept this sensor's row",
                        Path = survivor.FullPath,
                        Kind = JournalKinds.TreeCrud,
                        ProductId = request.RootId.ToString(),
                    });
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Journaling the merge of sensor {occupant.Id} failed");
                }

                _logger.Info($"Sensor merged: [{occupant.Id}] {occupant.FullPath} into [{survivor.Id}] {survivor.FullPath}: {request.Copied} copied, {request.Collisions} collisions");

                // The occupant is already out of the index; a failed database
                // removal would bring it back at this path on restart.
                if (!RemoveSensor(occupant.Id, InitiatorInfo.AsSystemInfo($"Merged into sensor {survivor.Id}")))
                    request.Error = $"Sensor {occupant.Id} was merged, but removing it from the database failed (see the log): it comes back at {request.Path} after a restart and must then be removed.";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Sensor merge failed: [{request.OccupantId}] into [{request.SurvivorId}]");
                request.Error = ex.Message;
            }
        }

        // Merge step 3: the survivor's newest value may now be a copied row.
        private void RefreshMergedSensor(SensorMergeRefreshRequest request)
        {
            if (TryGetSensorById(request.SensorId, out var sensor) &&
                OnOwningQueue(sensor, request.ExpectedRootId, request.Hops,
                    (root, hops) => request with { ExpectedRootId = root, Hops = hops }, "merge refresh"))
            {
                sensor.RefreshAfterMerge();
                SensorUpdateView(sensor);
            }
        }

        private void ReevaluateMovedSensorTemplates(SensorTemplateReevaluationRequest request)
        {
            if (TryGetSensorById(request.SensorId, out var sensor) &&
                OnOwningQueue(sensor, request.ExpectedRootId, request.Hops,
                    (root, hops) => request with { ExpectedRootId = root, Hops = hops }, "template re-evaluation"))
                ReevaluateTemplates(sensor);
        }

        private void UpdateThroughAlias(AliasRegistrationRequest request)
        {
            if (TryGetSensorById(request.Update.Id, out var sensor) &&
                OnOwningQueue(sensor, request.ExpectedRootId, request.Hops,
                    (root, hops) => request with { ExpectedRootId = root, Hops = hops }, "registration through an alias") &&
                !TryUpdateSensor(request.Update, out var error))
                _logger.Error($"Registration through an alias failed for sensor {sensor.Id}: {error}");
        }

        // A request routed by a sensor's root at enqueue time runs only on the
        // queue that owns the sensor now: otherwise it follows the sensor (at
        // most MaxAliasHops times) and the caller skips it here. Keeps the
        // sensor model single-writer across moves (ADR-0018).
        private bool OnOwningQueue(BaseSensorModel sensor, Guid expectedRootId, int hops,
            Func<Guid, int, IUpdateRequest> reroute, string what)
        {
            if (sensor.Root.Id == expectedRootId)
                return true;

            if (hops >= MaxAliasHops)
                _logger.Warn($"Dropped {what} for sensor {sensor.Id}: it kept moving between products");
            else
                Dispatch(sensor.Root, reroute(sensor.Root.Id, hops + 1));

            return false;
        }

        private void CountDroppedAliasValue(string reason) =>
            CountAndWarnRateLimited(ref _aliasValuesDropped, () => $"Aliased value dropped: {reason}");

        private void MaintainAliases(SensorAliasMaintenanceRequest request)
        {
            if (!TryGetSensorById(request.SensorId, out var sensor))
                return;

            if (sensor.Root.Id != request.ExpectedRootId)
            {
                if (request.Redispatch)
                    OnOwningQueue(sensor, request.ExpectedRootId, request.Hops,
                        (root, hops) => request with { ExpectedRootId = root, Hops = hops }, "alias removal");
                else
                    request.Misrouted = true;

                return;
            }

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

                changed |= request.ForcePersist;
            }

            if (changed)
                WriteSensorEntity(sensor);
        }

        // Alias hits count as persisted only after the write returned (#1719 review).
        private void WriteSensorEntity(BaseSensorModel sensor)
        {
            var entity = sensor.ToEntity();

            _database.UpdateSensor(entity);
            sensor.MarkAliasHitsPersisted(entity);
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
                        WriteSensorEntity(sensor);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Persisting dropped aliases of sensor {sensor.Id} failed");
                    }
                }
            }
        }

        private void DispatchAliasRemoval(BaseSensorModel sensor, Guid productId, string path, string reason) =>
            Dispatch(sensor.Root, new SensorAliasMaintenanceRequest(sensor.Id, sensor.Root.Id, DateTime.UtcNow)
            {
                Redispatch = true,
                RemoveProductId = productId,
                RemovePath = path,
                Initiator = InitiatorInfo.AsSystemInfo(reason),
            });

        // Fire-and-forget onto another product's queue: a queue handler must
        // never wait on another queue (aicontext/features/server/overview.md).
        private void Dispatch(ProductModel root, IUpdateRequest request)
        {
            // A product removed meanwhile must not get its queue back.
            if (!TryGetProduct(root.Id, out _))
            {
                _logger.Warn($"Dropped {request.GetType().Name} for removed product {root.Id}");
                return;
            }

            _ = GetOrAddCachedValue(root).Queue.AddItemAsync(request);
        }

        private CachedValue GetOrAddCachedValue(ProductModel root) =>
            _cache.GetOrAdd(root.Id, _ => new CachedValue(root, this));

        private string AliasFullPath(Guid productId, string path) =>
            TryGetProductNameById(productId, out var name) ? $"{name}/{path}" : $"{productId}/{path}";
    }
}
