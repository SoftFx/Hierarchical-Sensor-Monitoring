using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.ChangeSets
{
    /// <summary>
    /// Persistence facade of the undo journal (#1570): create-or-append of
    /// change sets, status transitions, and the entity-deletion cleanup.
    /// Transport-agnostic; the administration services append here, the
    /// rollback engine reads and mutates status here.
    /// </summary>
    // Concurrency: one process-wide lock guards every read-modify-write cycle
    // (append, status transition, purge). Journal appends are rare relative to
    // the writes they record, so a global lock is both correct and enough —
    // per-id gates would only pay off with concurrent writes grouped under
    // ONE change set id, which no client flow produces today.
    //
    // Durability: storage failures PROPAGATE (the ChangeSets collection does
    // not swallow them). The administration-service hook catches and logs
    // loudly instead of failing an already-applied write — the trade-off is
    // recorded in ADR-0010.
    public interface IChangeSetJournal
    {
        /// <summary>The change set or null when the id is unknown.</summary>
        ChangeSetModel Get(Guid changeSetId);

        /// <summary>All change sets, newest first.</summary>
        List<ChangeSetModel> GetAll();

        /// <summary>
        /// Append one entry, creating the change set on first use (author and
        /// creation time fixed by the first write). The entry's Sequence is
        /// assigned here — append order is the journal order.
        /// </summary>
        void Append(Guid changeSetId, InitiatorInfo author, ChangeSetEntry entry);

        /// <summary>Mark every non-restored entry restored and the set terminal.</summary>
        void MarkRolledBack(Guid changeSetId, Guid rollbackChangeSetId);

        /// <summary>Record that one entry's compensating write was applied.</summary>
        void MarkEntryRestored(Guid changeSetId, int entrySequence, Guid rollbackChangeSetId);

        /// <summary>
        /// Deletion cleanup: drop entries (and fan-out images) referencing the
        /// deleted entity, or templates bound to the deleted folder. Change
        /// sets that become entry-less are removed; touched survivors report
        /// <see cref="ChangeSetStatus.Partial"/>. Returns the number of change
        /// sets touched (removed or marked partial).
        /// </summary>
        int PurgeReferences(Guid entityId, Guid? folderId = null);
    }


    public sealed class ChangeSetJournal : IChangeSetJournal
    {
        private readonly IDatabaseCore _database;
        private readonly object _sync = new();

        public ChangeSetJournal(IDatabaseCore database)
        {
            _database = database;
        }


        public ChangeSetModel Get(Guid changeSetId)
        {
            lock (_sync)
            {
                return _database.ChangeSets.TryReadEntity(changeSetId, out var entity)
                    ? ChangeSetMapper.ToModel(entity)
                    : null;
            }
        }

        public List<ChangeSetModel> GetAll() =>
            _database.ChangeSets.ReadCollection()
                .Select(ChangeSetMapper.ToModel)
                .OrderByDescending(model => model.CreatedAtUtc)
                .ThenBy(model => model.Id)
                .ToList();

        public void Append(Guid changeSetId, InitiatorInfo author, ChangeSetEntry entry)
        {
            if (entry is null)
                throw new ArgumentNullException(nameof(entry));

            lock (_sync)
            {
                var model = _database.ChangeSets.TryReadEntity(changeSetId, out var existing)
                    ? ChangeSetMapper.ToModel(existing)
                    : new ChangeSetModel
                    {
                        Id = changeSetId,
                        Author = author?.Info ?? "unknown",
                        AuthorType = author?.Type ?? InitiatorType.System,
                        CreatedAtUtc = DateTime.UtcNow,
                        Status = ChangeSetStatus.Applied,
                    };

                if (model.Status == ChangeSetStatus.RolledBack)
                    throw new InvalidOperationException(
                        $"Change set {changeSetId} was already rolled back and accepts no further writes.");

                entry.Sequence = model.Entries.Count == 0 ? 0 : model.Entries.Max(e => e.Sequence) + 1;
                model.Entries.Add(entry);

                var persisted = ChangeSetMapper.ToEntity(model);

                if (existing is null)
                    _database.ChangeSets.AddEntity(persisted);
                else
                    _database.ChangeSets.UpdateEntity(persisted);
            }
        }

        public void MarkRolledBack(Guid changeSetId, Guid rollbackChangeSetId)
        {
            lock (_sync)
            {
                if (!_database.ChangeSets.TryReadEntity(changeSetId, out var existing))
                    return;

                var model = ChangeSetMapper.ToModel(existing);

                model.Status = ChangeSetStatus.RolledBack;

                foreach (var entry in model.Entries.Where(e => e.RestoredBy is null))
                    entry.RestoredBy = rollbackChangeSetId;

                _database.ChangeSets.UpdateEntity(ChangeSetMapper.ToEntity(model));
            }
        }

        public void MarkEntryRestored(Guid changeSetId, int entrySequence, Guid rollbackChangeSetId)
        {
            lock (_sync)
            {
                if (!_database.ChangeSets.TryReadEntity(changeSetId, out var existing))
                    return;

                var model = ChangeSetMapper.ToModel(existing);
                var entry = model.Entries.FirstOrDefault(e => e.Sequence == entrySequence);

                if (entry is null || entry.RestoredBy is not null)
                    return;

                entry.RestoredBy = rollbackChangeSetId;

                _database.ChangeSets.UpdateEntity(ChangeSetMapper.ToEntity(model));
            }
        }

        public int PurgeReferences(Guid entityId, Guid? folderId = null)
        {
            var touched = 0;

            lock (_sync)
            {
                foreach (var entity in _database.ChangeSets.ReadCollection())
                {
                    var model = ChangeSetMapper.ToModel(entity);
                    var changed = false;

                    // Entry-level drops: the entity itself is gone, or a
                    // folder-bound template entry lost its folder.
                    foreach (var entry in model.Entries.Where(e => References(e, entityId, folderId)).ToList())
                    {
                        model.Entries.Remove(entry);
                        changed = true;
                    }

                    // Fan-out images of surviving entries: drop images of the
                    // deleted node (the entry itself survives, partial).
                    foreach (var entry in model.Entries.Where(e => e.Fanout is { Count: > 0 }))
                    {
                        var before = entry.Fanout.Count;

                        entry.Fanout.RemoveAll(image => image.NodeId == entityId);

                        if (entry.Fanout.Count != before)
                            changed = true;
                    }

                    if (!changed)
                        continue;

                    touched++;

                    if (model.Entries.Count == 0)
                        _database.ChangeSets.RemoveEntity(model.Id);
                    else
                    {
                        model.Status = model.Status == ChangeSetStatus.RolledBack
                            ? ChangeSetStatus.RolledBack
                            : ChangeSetStatus.Partial;

                        _database.ChangeSets.UpdateEntity(ChangeSetMapper.ToEntity(model));
                    }
                }
            }

            return touched;

            static bool References(ChangeSetEntry entry, Guid entityId, Guid? folderId) =>
                entry.EntityId == entityId ||
                (folderId is { } folder && entry.Kind == ChangeSetEntryKind.AlertTemplate && entry.ParentId == folder);
        }
    }
}
