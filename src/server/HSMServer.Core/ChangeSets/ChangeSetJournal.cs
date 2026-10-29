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
        ///
        /// #1588: group ids are SERVER-minted per request (one HTTP request
        /// or MCP call = one rollback group), so a foreign author or a
        /// terminal set can never be appended to through a client-supplied
        /// id — the #1570 fork rule is gone with the client ids.
        /// </summary>
        Guid Append(Guid changeSetId, InitiatorInfo author, ChangeSetEntry entry);

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

        /// <summary>
        /// The batch twin of the single-entity purge: one pass over the
        /// journal for a whole deleted product subtree (the per-node loop
        /// would rescan and deserialize every change set once per node,
        /// under the same journal-wide lock).
        /// </summary>
        int PurgeReferences(IReadOnlyCollection<Guid> entityIds, Guid? folderId = null);
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

        public Guid Append(Guid changeSetId, InitiatorInfo author, ChangeSetEntry entry)
        {
            if (entry is null)
                throw new ArgumentNullException(nameof(entry));

            lock (_sync)
            {
                if (_database.ChangeSets.TryReadEntity(changeSetId, out var existing))
                {
                    var current = ChangeSetMapper.ToModel(existing);

                    // The TERMINAL half of the #1570 fork rule survives
                    // (#1588 review round 2): a rollback can finish while the
                    // request that owns the group is still appending (a
                    // multi-sensor import, a long schedule delete). Appending
                    // into the rolled-back set would strand those undo points
                    // — the engine refuses them and nothing recreates a group.
                    // The write still journals: under a FRESH id, which the
                    // return names (the author half of the rule stays gone —
                    // server-minted ids cannot collide across principals).
                    if (current.Status == ChangeSetStatus.RolledBack)
                        changeSetId = Guid.NewGuid();
                    else
                    {
                        AppendTo(current, entry, existing: true);

                        return changeSetId;
                    }
                }

                var model = new ChangeSetModel
                {
                    Id = changeSetId,
                    Author = author?.Info ?? "unknown",
                    AuthorType = author?.Type ?? InitiatorType.System,
                    CreatedAtUtc = DateTime.UtcNow,
                    Status = ChangeSetStatus.Applied,
                };

                AppendTo(model, entry, existing: false);

                return changeSetId;
            }
        }

        private void AppendTo(ChangeSetModel model, ChangeSetEntry entry, bool existing)
        {
            entry.Sequence = model.Entries.Count == 0 ? 0 : model.Entries.Max(e => e.Sequence) + 1;
            model.Entries.Add(entry);

            var persisted = ChangeSetMapper.ToEntity(model);

            if (existing)
                _database.ChangeSets.UpdateEntity(persisted);
            else
                _database.ChangeSets.AddEntity(persisted);
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

        public int PurgeReferences(Guid entityId, Guid? folderId = null) =>
            PurgeReferences(new[] { entityId }, folderId);

        public int PurgeReferences(IReadOnlyCollection<Guid> entityIds, Guid? folderId = null)
        {
            var touched = 0;
            var ids = entityIds as HashSet<Guid> ?? [.. entityIds];

            lock (_sync)
            {
                foreach (var entity in _database.ChangeSets.ReadCollection())
                {
                    var model = ChangeSetMapper.ToModel(entity);
                    var changed = false;

                    // Entry-level drops: the entity itself is gone, or a
                    // folder-bound template entry lost its folder.
                    foreach (var entry in model.Entries.Where(e => References(e, ids, folderId)).ToList())
                    {
                        model.Entries.Remove(entry);
                        changed = true;
                    }

                    // Fan-out images of surviving entries: drop images of the
                    // deleted nodes (the entry itself survives, partial).
                    foreach (var entry in model.Entries.Where(e => e.Fanout is { Count: > 0 }))
                    {
                        var before = entry.Fanout.Count;

                        entry.Fanout.RemoveAll(image => ids.Contains(image.NodeId));

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

            static bool References(ChangeSetEntry entry, HashSet<Guid> entityIds, Guid? folderId) =>
                entityIds.Contains(entry.EntityId) ||
                (folderId is { } folder && entry.Kind == ChangeSetEntryKind.AlertTemplate && entry.ParentId == folder);
        }
    }
}
