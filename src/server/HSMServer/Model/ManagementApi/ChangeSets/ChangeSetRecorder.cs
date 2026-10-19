using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.ManagementApi.ChangeSets
{
    /// <summary>
    /// The single hook helper every journaled administration service uses
    /// (#1570): resolves the effective change-set id of a write (client id or
    /// a fresh one for an ungrouped write), snapshots policy lists as entity
    /// images, and appends entries to the undo journal — swallowing journal
    /// failures AFTER LOUD LOGGING so a write that already applied is never
    /// reported failed over a lost undo point (ADR-0010 trade-off).
    /// </summary>
    public sealed class ChangeSetRecorder
    {
        private readonly IChangeSetJournal _journal;
        private readonly ILogger<ChangeSetRecorder> _logger;

        public ChangeSetRecorder(IChangeSetJournal journal, ILogger<ChangeSetRecorder> logger)
        {
            _journal = journal;
            _logger = logger;
        }


        /// <summary>
        /// The change-set id a write journals under: the client-supplied id
        /// when grouping, a fresh id when the write stands alone. Also used as
        /// the dry-run echo id.
        /// </summary>
        public static Guid ResolveChangeSetId(ChangeSetWriteOptions write) => write?.ChangeSetId ?? Guid.NewGuid();

        /// <summary>
        /// Append one entry; false when the journal write failed (the undo
        /// point is lost — logged loudly). Callers echo the change-set id on
        /// the write response ONLY on true, so a client never receives an
        /// undo handle that does not exist.
        /// </summary>
        public bool Record(Guid changeSetId, InitiatorInfo author, ChangeSetEntry entry)
        {
            try
            {
                _journal.Append(changeSetId, author, entry);

                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Undo point lost: journal append failed for change set {ChangeSetId} ({Kind} {Operation} on {EntityId})",
                    changeSetId, entry.Kind, entry.Operation, entry.EntityId);

                return false;
            }
        }


        // Policy-list snapshots: the full-list image the rollback restores.
        // Taken under the caller's write gate (the *Core methods re-resolve
        // the node there), so the image is the state this write serializes
        // against.
        public static List<PolicyEntity> SnapshotDataPolicies(BaseSensorModel sensor) =>
            [.. sensor.Policies.Select(p => p.ToEntity())];

        public static List<PolicyEntity> SnapshotSensorTtlPolicies(BaseSensorModel sensor) =>
            [.. sensor.Policies.TTLPolicies.Select(p => p.ToEntity())];

        public static List<PolicyEntity> SnapshotProductTtlPolicies(ProductModel product) =>
            [.. product.Policies.TTLPolicies.Select(p => p.ToEntity())];

        // The node's root product id — the informational authorization
        // boundary of an entry. Defensive by design: the Parent chain is not
        // guaranteed to bottom out at a product in every harness, and the
        // undo journal must never fail a write over display metadata.
        public static Guid RootProductIdOf(BaseNodeModel node)
        {
            try
            {
                return node.Root?.Id ?? node.Id;
            }
            catch
            {
                return node.Id;
            }
        }
    }
}
