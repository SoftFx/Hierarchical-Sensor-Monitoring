using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Journal;
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
    /// (#1570, #1588): resolves the write's undo-group id (the server-minted
    /// per-request id), snapshots policy lists as entity images, appends
    /// entries to the internal undo store, and writes the UNIFIED-JOURNAL
    /// record for the kinds the cache-layer events do not cover (templates,
    /// schedules, and every rollback entry) — swallowing journal failures
    /// AFTER LOUD LOGGING so a write that already applied is never reported
    /// failed over a lost undo point (ADR-0010 trade-off).
    /// </summary>
    public sealed class ChangeSetRecorder
    {
        private readonly IChangeSetJournal _journal;
        private readonly IJournalService _journalService;
        private readonly ILogger<ChangeSetRecorder> _logger;

        public ChangeSetRecorder(IChangeSetJournal journal, IJournalService journalService,
            ILogger<ChangeSetRecorder> logger)
        {
            _journal = journal;
            _journalService = journalService;
            _logger = logger;
        }


        /// <summary>
        /// The undo-group id a write journals under: the request-scoped id
        /// minted per HTTP request / MCP call / form post (#1588), or a fresh
        /// one when the caller passes no options.
        /// </summary>
        public static Guid ResolveChangeSetId(ChangeSetWriteOptions write) => write?.ChangeSetId ?? Guid.NewGuid();

        /// <summary>
        /// The write initiator, resolved identically everywhere it is needed
        /// (policy, template and schedule writes, the rollback engine): the
        /// token's OWNER as a user (the token acts as its owner, #1384); a
        /// cookie principal (the web UI) carries no token claims and falls to
        /// the authenticated identity name. ONE helper because the undo
        /// journal's author strings must stay consistent across transports.
        /// </summary>
        public static InitiatorInfo InitiatorOf(System.Security.Claims.ClaimsPrincipal user, IUserManager users)
        {
            var ownerClaim = user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;
            var owner = Guid.TryParse(ownerClaim, out var ownerId) ? users[ownerId] : null;

            var name = owner?.Name ?? user?.Identity?.Name;

            return InitiatorInfo.AsUser(string.IsNullOrEmpty(name) ? "api-token" : name);
        }

        /// <summary>
        /// The write initiator of a journaled write, stamped with the
        /// request's undo-group id (#1588): the cache-layer journal records
        /// copy the stamp onto the record, which is what links a policy-list
        /// change to its rollback group. Shared by every administration
        /// service so the stamping rule cannot drift between them.
        /// </summary>
        public static InitiatorInfo InitiatorOf(System.Security.Claims.ClaimsPrincipal user, IUserManager users,
            ChangeSetWriteOptions write)
        {
            var initiator = InitiatorOf(user, users);

            return write?.ChangeSetId is { } id ? initiator.AsJournaled(id) : initiator;
        }

        /// <summary>
        /// Append one entry to the internal undo store; null when the journal
        /// write failed (the undo point is lost — logged loudly). The id is
        /// the request's group id — with server-minted ids (#1588) the entry
        /// always lands in exactly that group.
        /// </summary>
        public Guid? Record(Guid changeSetId, InitiatorInfo author, ChangeSetEntry entry)
        {
            try
            {
                var landed = _journal.Append(changeSetId, author, entry);

                // The unified-journal record of the kinds the cache-layer
                // events do not cover: policy lists journal through the
                // ChangesHandler records (stamped with the group via the
                // initiator), templates and schedules have no cache journal
                // hook — the recorder IS their journal writer.
                if (entry.Kind is ChangeSetEntryKind.AlertTemplate or ChangeSetEntryKind.AlertSchedule)
                    WriteJournalRecord(landed, author, entry);

                return landed;
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Undo point lost: journal append failed for change set {ChangeSetId} ({Kind} {Operation} on {EntityId})",
                    changeSetId, entry.Kind, entry.Operation, entry.EntityId);

                return null;
            }
        }

        /// <summary>
        /// The rollback twin of <see cref="Record"/> (#1588): appends the
        /// compensating entry to the ROLLBACK group and always writes its
        /// unified-journal record — the rollback group must be visible in the
        /// journal (it is the redo handle: rolling back the rollback re-applies
        /// the original writes). Policy restores also leave their per-node
        /// ChangesHandler records; this record is the group's primary trace.
        /// </summary>
        public bool RecordRollback(Guid rollbackChangeSetId, InitiatorInfo author, ChangeSetEntry entry)
        {
            try
            {
                _journal.Append(rollbackChangeSetId, author, entry);
                WriteJournalRecord(rollbackChangeSetId, author, entry);

                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Undo point lost: rollback journal append failed for change set {ChangeSetId} ({Kind} {Operation} on {EntityId})",
                    rollbackChangeSetId, entry.Kind, entry.Operation, entry.EntityId);

                return false;
            }
        }

        private void WriteJournalRecord(Guid changeSetId, InitiatorInfo author, ChangeSetEntry entry) =>
            _journalService.AddRecord(new JournalRecordModel(entry.EntityId, author ?? InitiatorInfo.System)
            {
                Enviroment = entry.Kind switch
                {
                    ChangeSetEntryKind.AlertTemplate => "Alert template",
                    ChangeSetEntryKind.AlertSchedule => "Alert schedule",
                    _ => "Rollback",
                },
                PropertyName = $"{entry.Kind}:{entry.Operation}",
                OldValue = entry.Operation == ChangeSetOperation.Delete ? entry.Path : null,
                NewValue = entry.Operation != ChangeSetOperation.Delete ? entry.Path : null,
                Path = entry.Path,
                Kind = KindOf(entry),
                ChangeSetId = changeSetId.ToString(),
            });

        private static string KindOf(ChangeSetEntry entry) => entry.Kind switch
        {
            ChangeSetEntryKind.SensorDataPolicies => JournalKinds.SensorDataPolicies,
            ChangeSetEntryKind.SensorTtlPolicies => JournalKinds.SensorTtlPolicies,
            ChangeSetEntryKind.ProductTtlPolicies => JournalKinds.ProductTtlPolicies,
            ChangeSetEntryKind.AlertTemplate => JournalKinds.AlertTemplate,
            ChangeSetEntryKind.AlertSchedule => JournalKinds.AlertSchedule,
            _ => null,
        };


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
