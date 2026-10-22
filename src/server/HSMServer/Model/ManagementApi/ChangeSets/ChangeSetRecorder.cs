using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
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
        /// The write initiator, resolved identically everywhere it is needed
        /// (policy, template and schedule writes, the rollback engine): the
        /// token's OWNER as a user (the token acts as its owner, #1384); a
        /// cookie principal (the web UI) carries no token claims and falls to
        /// the authenticated identity name. ONE helper because the journal's
        /// fork rule compares the author STRINGS this produces — drifting
        /// copies would silently split a user's grouped writes.
        /// </summary>
        public static InitiatorInfo InitiatorOf(System.Security.Claims.ClaimsPrincipal user, IUserManager users)
        {
            var ownerClaim = user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;
            var owner = Guid.TryParse(ownerClaim, out var ownerId) ? users[ownerId] : null;

            var name = owner?.Name ?? user?.Identity?.Name;

            return InitiatorInfo.AsUser(string.IsNullOrEmpty(name) ? "api-token" : name);
        }

        /// <summary>
        /// Append one entry; null when the journal write failed (the undo
        /// point is lost — logged loudly). The returned id is the set the
        /// entry ACTUALLY landed in: the journal forks a client-supplied id
        /// whose set belongs to another author or is already rolled back.
        /// Callers echo THAT id on the write response only on success, so a
        /// client never receives an undo handle that does not exist.
        /// </summary>
        public Guid? Record(Guid changeSetId, InitiatorInfo author, ChangeSetEntry entry)
        {
            try
            {
                return _journal.Append(changeSetId, author, entry);
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Undo point lost: journal append failed for change set {ChangeSetId} ({Kind} {Operation} on {EntityId})",
                    changeSetId, entry.Kind, entry.Operation, entry.EntityId);

                return null;
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
