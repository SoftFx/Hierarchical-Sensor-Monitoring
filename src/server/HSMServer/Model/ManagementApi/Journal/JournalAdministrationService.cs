using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HSMServer.Model.ManagementApi.Journal
{
    /// <summary>
    /// The wire form of a journal record's identity: the storage key
    /// (entity id, ticks, record type) rendered as "{id}:{ticks}:{type}".
    /// Rollback by record (#1588) addresses the record's undo group through
    /// this id.
    /// </summary>
    public static class JournalRecordId
    {
        public static string Of(JournalRecordModel record) =>
            $"{record.Key.Id:N}:{record.Key.Time}:{(int)record.Key.Type}";

        public static bool TryParse(string recordId, out JournalKey key)
        {
            key = default;

            if (recordId is null)
                return false;

            var parts = recordId.Split(':');

            if (parts.Length != 3 ||
                !Guid.TryParseExact(parts[0], "N", out var id) ||
                !long.TryParse(parts[1], out var ticks) ||
                !byte.TryParse(parts[2], out var type))
                return false;

            key = new JournalKey(id, ticks, (RecordType)type);

            return true;
        }
    }


    public sealed record JournalFilter
    {
        public string Author { get; init; }

        public DateTime? FromUtc { get; init; }

        public DateTime? ToUtc { get; init; }

        /// <summary>Entity kind (JournalKinds value) or "other" for legacy/non-config records.</summary>
        public string Kind { get; init; }

        /// <summary>Only records of this root product.</summary>
        public Guid? ProductId { get; init; }

        /// <summary>Only records keyed by this entity id.</summary>
        public Guid? EntityId { get; init; }

        public int Limit { get; init; } = 100;

        public int Offset { get; init; }
    }


    public sealed record JournalRecordDto
    {
        public string Id { get; init; }

        public DateTime Time { get; init; }

        public string Author { get; init; }

        /// <summary>Entity kind; "other" on legacy and non-configuration records — the same rows the kind=other filter returns (#1679).</summary>
        public string Kind { get; init; }

        /// <summary>Root product id; null on folder- and global-scoped records (schedules, templates) and on legacy rows whose root product is no longer derivable (#1679). Pre-#1610 template rows render their stamped folder id as stored.</summary>
        public string ProductId { get; init; }

        public string Path { get; init; }

        public string Property { get; init; }

        public string OldValue { get; init; }

        public string NewValue { get; init; }

        /// <summary>The undo group of the write; null when the record is not rollbackable.</summary>
        public string ChangeSetId { get; init; }

        /// <summary>True when the record's write is undoable (admin-only rollback).</summary>
        public bool Rollbackable { get; init; }

        /// <summary>
        /// create | update | delete | rollback — what kind of write the
        /// record is (#1638); null when nothing says (legacy rows). The
        /// property format is untouched; this tag is derived and additive.
        /// </summary>
        public string Operation { get; init; }

        /// <summary>True when the record's undo group was rolled back — the write was undone by a later rollback.</summary>
        public bool RolledBack { get; init; }
    }


    public sealed record JournalRecordDetailsDto
    {
        public JournalRecordDto Record { get; init; }

        /// <summary>The undo group's entries (before/after images) when the record is rollbackable, null otherwise.</summary>
        public ChangeSetDetailsDto ChangeSet { get; init; }
    }


    /// <summary>
    /// Per-record sight decision of the unified-journal surface (#1588): a
    /// record is visible when its resolved resource (product, folder, global)
    /// is in the caller's sight. The token side delegates to the evaluator;
    /// the web UI maps the cookie user's role predicates onto the same
    /// resources.
    /// </summary>
    public interface IJournalRecordAccess
    {
        bool CanRead(ApiTokenResource resource);

        /// <summary>The rollback gate (#1588): on the token side the OWNER must be an admin.</summary>
        bool IsAdmin { get; }

        /// <summary>
        /// The rollback engine's per-resource write seam, null on the token
        /// side (the engine's evaluator default applies); the cookie twin
        /// supplies its CookieUserAccess so a web rollback's compensating
        /// writes authorize through the web user's roles.
        /// </summary>
        ChangeSets.IChangeSetAccess ChangeSetAccess { get; }
    }


    /// <summary>
    /// The unified Journal's read and rollback surface (#1588): journal
    /// records from ALL sources with author/time/kind/product filters, and
    /// the admin-only rollback BY RECORD — the record's undo group replays
    /// through the internal rollback engine (the #1570 mechanics, now an
    /// implementation detail of the Journal).
    /// </summary>
    public sealed class JournalAdministrationService
    {
        private const int MaxLimit = 200;

        public const string OtherKind = "other";

        private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

        // The hard ceiling of a global-list scan (#1588 review round 1): the
        // guid-major key layout makes every list read walk whole shards, so
        // an unbounded caller range would deserialize the server's entire
        // journal history per call.
        private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(90);

        private readonly IJournalService _journal;
        private readonly ITreeValuesCache _cache;
        private readonly HSMServer.Folders.IFolderManager _folders;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;
        private readonly IChangeSetAdministrationService _changeSets;
        private readonly HSMServer.Core.ChangeSets.IChangeSetJournal _changeSetJournal;

        public JournalAdministrationService(IJournalService journal, ITreeValuesCache cache,
            HSMServer.Folders.IFolderManager folders, IApiTokenAuthorizationService authorization,
            IUserManager users, IChangeSetAdministrationService changeSets,
            HSMServer.Core.ChangeSets.IChangeSetJournal changeSetJournal)
        {
            _journal = journal;
            _cache = cache;
            _folders = folders;
            _authorization = authorization;
            _users = users;
            _changeSets = changeSets;
            _changeSetJournal = changeSetJournal;
        }


        // === List / details ===

        public PolicyWriteResult<List<JournalRecordDto>> ListRecords(ClaimsPrincipal user,
            JournalFilter filter, IJournalRecordAccess access = null)
        {
            filter ??= new JournalFilter();
            access ??= new TokenJournalRecordAccess(_authorization, _users, user);

            var limit = Math.Clamp(filter.Limit <= 0 ? 100 : filter.Limit, 1, MaxLimit);
            var offset = Math.Max(filter.Offset, 0);
            var to = filter.ToUtc ?? DateTime.UtcNow;
            var from = filter.FromUtc ?? to - DefaultWindow;

            if (to - from > MaxWindow)
                from = to - MaxWindow;

            // The cheap field filter runs before the per-record resource
            // resolution (several cache lookups each) — only survivors pay
            // for the sight check.
            // Group cache for the rendered page (#1588 review round 4,
            // #1638): a terminal (rolled-back) group's records keep their
            // kind and link but the Rollback action must not come back on
            // refresh, and the page now also derives each record's
            // operation from its group — one journal read per DISTINCT
            // group of the page serves both.
            var groupCache = new Dictionary<string, ChangeSetModel>(StringComparer.Ordinal);

            var page = _journal.GetRecords(from, to)
                .Where(r => Matches(r, filter))
                .Where(r => access.CanRead(ResourceOf(r)))
                .OrderByDescending(r => r.Key.Time)
                .ThenBy(r => r.Key.Id)
                .Skip(offset)
                .Take(limit)
                .Select(r => Enrich(Render(r), r, groupCache))
                .ToList();

            return PolicyWriteResult<List<JournalRecordDto>>.Ok(page);
        }

        // The rendered record with its operation tag and rollback state
        // (#1638): the operation comes from the record's own data first,
        // then from its undo-group entry. stripRollbackable: the LIST strips
        // the Rollback action and marks the write rolled back when the group
        // went terminal; the details response keeps the stored flag.
        private JournalRecordDto Enrich(JournalRecordDto dto, JournalRecordModel record,
            Dictionary<string, ChangeSetModel> groupCache, bool stripRollbackable = true)
        {
            ChangeSetModel GroupOf(string changeSetId)
            {
                if (groupCache.TryGetValue(changeSetId, out var cached))
                    return cached;

                var group = Guid.TryParse(changeSetId, out var id) ? _changeSetJournal.Get(id) : null;

                groupCache.Add(changeSetId, group);

                return group;
            }

            var operation = JournalOperations.Of(record, GroupOf);

            if (dto.Rollbackable && GroupOf(dto.ChangeSetId) is { Status: ChangeSetStatus.RolledBack })
            {
                // The list also strips the Rollback action (the terminal
                // strip, #1588 review round 4); the details response keeps
                // the stored flag.
                dto = stripRollbackable
                    ? dto with { Rollbackable = false, RolledBack = true }
                    : dto with { RolledBack = true };
            }

            return operation is null ? dto : dto with { Operation = operation };
        }

        public PolicyWriteResult<JournalRecordDetailsDto> GetRecord(string recordId, ClaimsPrincipal user,
            IJournalRecordAccess access = null)
        {
            access ??= new TokenJournalRecordAccess(_authorization, _users, user);

            if (!TryResolve(recordId, access, out var record))
                return PolicyWriteResult<JournalRecordDetailsDto>.Fail(PolicyWriteOutcome.NotFound);

            return PolicyWriteResult<JournalRecordDetailsDto>.Ok(new JournalRecordDetailsDto
            {
                // Enriched like the list rows on the ADDITIVE fields only
                // (#1638): the details response keeps the stored
                // rollbackable flag — rewriting it here would not be an
                // additive change (the list's terminal strip stays where it
                // already was, #1588 review round 4).
                Record = Enrich(Render(record), record,
                    new Dictionary<string, ChangeSetModel>(StringComparer.Ordinal), stripRollbackable: false),
                ChangeSet = UndoGroupDetails(record, user, access),
            });
        }


        // === Rollback by record ===

        public Task<PolicyWriteResult<RollbackResultDto>> RollbackAsync(string recordId, ClaimsPrincipal user,
            bool force = false, bool dryRun = false, IJournalRecordAccess access = null)
        {
            access ??= new TokenJournalRecordAccess(_authorization, _users, user);

            if (!TryResolve(recordId, access, out var record))
                return Task.FromResult(PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.NotFound));

            // The rollback gate (#1588): the owner of the acting principal
            // must be an admin on BOTH transports — the per-resource write
            // pre-flight of the rollback engine stays as the second line of
            // defense.
            if (!access.IsAdmin)
                return Task.FromResult(PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.Forbidden,
                    message: JournalRollbackAccessDeniedMessage));

            // The SAME predicate the list renders by: rollbackable is a
            // property of the record's KIND plus its group link, and both
            // transports must agree on it (a settings record can carry a
            // group stamp from its request without being undoable itself).
            if (!JournalKinds.IsRollbackable(record.Kind) || !Guid.TryParse(record.ChangeSetId, out var changeSetId))
                return Task.FromResult(PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.Conflict,
                    message: "The journal record is not rollbackable: its kind has no undo entry (tree CRUD, node settings) or its write predates the unified journal."));

            // The cookie twin rides the engine's compensating writes; the
            // token default flows through the evaluator verbatim.
            return _changeSets.RollbackAsync(changeSetId, user, force, dryRun, access.ChangeSetAccess);
        }

        public const string JournalRollbackAccessDeniedMessage =
            "Rollback requires an administrator (the token's owner must be an admin).";


        // === Resolution and rendering ===

        private bool TryResolve(string recordId, IJournalRecordAccess access, out JournalRecordModel record)
        {
            record = null;

            if (!JournalRecordId.TryParse(recordId, out var key))
                return false;

            // Exact-key point read (#1588 review round 1): a range scan for
            // one record walks whole shards; the storage key addresses it
            // directly.
            record = _journal.GetRecord(key);

            // Anti-enumeration: an unknown and an invisible record answer the
            // same 404.
            return record is not null && access.CanRead(ResourceOf(record));
        }

        // The sight boundary of a record: the stamped root product when it
        // resolves to a live product, else the entity resolved against the
        // live tree — legacy records and records of deleted entities fall to
        // the admin-only Global scope.
        private ApiTokenResource ResourceOf(JournalRecordModel record)
        {
            // The stamp is trusted only when it resolves to a LIVE product
            // (#1610): alertTemplate records written before the fix carry
            // their FOLDER id in ProductId (the recorder stamped the entry's
            // ParentId blindly), and a blind Product() fails boundary
            // resolution — hiding the record from every caller, admin
            // included. An unresolvable stamp falls through to the live-tree
            // resolution below, which lands template ids on their folder and
            // everything unknown on the admin-only Global scope.
            if (Guid.TryParse(record.ProductId, out var productId) && productId != Guid.Empty &&
                _cache.TryGetProduct(productId, out _))
                return ApiTokenResource.Product(productId);

            var id = record.Key.Id;

            if (_cache.TryGetProduct(id, out _))
                return ApiTokenResource.Product(id);

            if (_cache.GetSensor(id) is { } sensor && sensor.Root is { } root)
                return ApiTokenResource.Product(root.Id);

            if (_folders.TryGetValue(id, out _))
                return ApiTokenResource.Folder(id);

            if (_cache.GetAlertTemplate(id) is { } template)
                return ApiTokenResource.Folder(template.FolderId);

            return ApiTokenResource.GlobalScope;
        }

        private bool Matches(JournalRecordModel record, JournalFilter filter) =>
            (filter.Author is null ||
             record.Initiator?.Contains(filter.Author, StringComparison.OrdinalIgnoreCase) == true) &&
            (filter.Kind is null ||
             string.Equals(record.Kind ?? OtherKind, filter.Kind, StringComparison.OrdinalIgnoreCase)) &&
            (filter.ProductId is not { } product || MatchesProductFilter(record, product)) &&
            (filter.EntityId is not { } entity || record.Key.Id == entity);

        // #1601: the product filter narrows PRODUCT-scoped records to the
        // selected root product but must not hide records that carry no
        // root product of their own — global entities (alert schedules),
        // folder-scoped entities (alert templates, folders) and unscoped
        // legacy rows stay visible, otherwise the page looks empty while
        // the journal holds the records.
        private bool MatchesProductFilter(JournalRecordModel record, Guid product) =>
            ResolvedRootProductOf(record) is not { } resolved || resolved == product;

        // The record's root product, or null when the record is global or
        // folder-scoped. A NON-EMPTY stamp is a root product whether or not
        // the product is still alive — a deleted product's history must not
        // leak into every other product's filter — except the folder id an
        // alert template stamps. Unstamped records resolve against the live
        // tree: the entity itself (a live product), a sensor's root, else
        // no root product (global schedules, legacy rows of deleted
        // entities). Mirrors the entity resolution of ResourceOf below.
        private Guid? ResolvedRootProductOf(JournalRecordModel record)
        {
            if (Guid.TryParse(record.ProductId, out var stamped) && stamped != Guid.Empty)
                return IsFolderStamp(record, stamped) ? null : stamped;

            var id = record.Key.Id;

            if (_cache.TryGetProduct(id, out _))
                return id;

            if (_cache.GetSensor(id) is { Root: { } root })
                return root.Id;

            return null;
        }

        // The only non-product stamp is the FOLDER id an alert template
        // journals under: the folder itself when live, else the record's
        // live template naming it (folder-scoped records carry no root
        // product of their own).
        private bool IsFolderStamp(JournalRecordModel record, Guid stamped) =>
            _folders.TryGetValue(stamped, out _) ||
            _cache.GetAlertTemplate(record.Key.Id)?.FolderId == stamped;

        // #1679: legacy rows come back with kind=null and productId=null
        // although the filters MATCH them (kind=other, the resolved root
        // product), so a client grouping or checking the payload by those
        // fields loses every legacy row. Render the filter's own answers:
        // the null kind becomes "other", and a missing product id is
        // backfilled from the live-tree resolution the product filter
        // already uses (null when the row's root product is not derivable —
        // global and folder-scoped records, legacy rows of deleted entities).
        private JournalRecordDto Render(JournalRecordModel record) => new()
        {
            Id = JournalRecordId.Of(record),
            Time = new DateTime(record.Key.Time, DateTimeKind.Utc),
            Author = record.Initiator,
            Kind = record.Kind ?? OtherKind,
            // The same "is the stamp usable?" rule the product filter applies:
            // an unparsable or all-zero stamp resolves through the live tree
            // like a missing one, so the payload echoes what the filter
            // matched for every stored row, not only today's well-formed ones.
            ProductId = Guid.TryParse(record.ProductId, out var stamped) && stamped != Guid.Empty
                ? record.ProductId
                : ResolvedRootProductOf(record)?.ToString(),
            Path = record.Path,
            Property = record.PropertyName,
            OldValue = record.OldValue,
            NewValue = record.NewValue,
            ChangeSetId = JournalKinds.IsRollbackable(record.Kind) ? record.ChangeSetId : null,
            Rollbackable = JournalKinds.IsRollbackable(record.Kind) && record.ChangeSetId is not null,
        };

        private ChangeSetDetailsDto UndoGroupDetails(JournalRecordModel record, ClaimsPrincipal user,
            IJournalRecordAccess access)
        {
            if (!Guid.TryParse(record.ChangeSetId, out var changeSetId))
                return null;

            var details = _changeSets.GetChangeSet(changeSetId, user, access.ChangeSetAccess);

            return details.Success ? details.Value : null;
        }


        // === The admin gate shared by REST and MCP (#1588) ===

        // True when the principal acts as an ADMIN: a token principal
        // resolves its owner user; a cookie principal is the User itself.
        public static bool PrincipalIsAdmin(ClaimsPrincipal principal, IUserManager users)
        {
            if (principal is HSMServer.Model.Authentication.User cookieUser)
                return cookieUser.IsAdmin;

            var ownerClaim = principal?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;

            return Guid.TryParse(ownerClaim, out var ownerId) && users[ownerId] is { IsAdmin: true };
        }


        // === Sight implementations ===

        private sealed class TokenJournalRecordAccess : IJournalRecordAccess
        {
            private readonly IApiTokenAuthorizationService _authorization;
            private readonly IUserManager _users;
            private readonly ClaimsPrincipal _user;

            public TokenJournalRecordAccess(IApiTokenAuthorizationService authorization, IUserManager users,
                ClaimsPrincipal user)
            {
                _authorization = authorization;
                _users = users;
                _user = user;
            }

            public bool CanRead(ApiTokenResource resource) =>
                _authorization.AuthorizeRead(_user, resource) == ApiTokenAuthorization.Allowed;

            public bool IsAdmin => PrincipalIsAdmin(_user, _users);

            // Null: the engine authorizes token principals through the evaluator.
            public ChangeSets.IChangeSetAccess ChangeSetAccess => null;
        }
    }
}
