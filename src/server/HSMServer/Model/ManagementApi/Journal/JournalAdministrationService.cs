using HSMServer.Authentication;
using HSMServer.Core.Cache;
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

        /// <summary>Entity kind; null on legacy and non-configuration records.</summary>
        public string Kind { get; init; }

        public string ProductId { get; init; }

        public string Path { get; init; }

        public string Property { get; init; }

        public string OldValue { get; init; }

        public string NewValue { get; init; }

        /// <summary>The undo group of the write; null when the record is not rollbackable.</summary>
        public string ChangeSetId { get; init; }

        /// <summary>True when the record's write is undoable (admin-only rollback).</summary>
        public bool Rollbackable { get; init; }
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
            // Group status for the rendered page (#1588 review round 4):
            // a terminal (rolled-back) group's records keep their kind and
            // link, but the Rollback action must not come back on refresh —
            // one cheap journal read per DISTINCT group of the page.
            var terminalGroups = new HashSet<string>(StringComparer.Ordinal);

            var page = _journal.GetRecords(from, to)
                .Where(r => Matches(r, filter))
                .Where(r => access.CanRead(ResourceOf(r)))
                .OrderByDescending(r => r.Key.Time)
                .ThenBy(r => r.Key.Id)
                .Skip(offset)
                .Take(limit)
                .Select(r =>
                {
                    var dto = Render(r);

                    if (dto.Rollbackable && IsTerminal(dto.ChangeSetId, terminalGroups))
                        dto = dto with { Rollbackable = false };

                    return dto;
                })
                .ToList();

            return PolicyWriteResult<List<JournalRecordDto>>.Ok(page);
        }

        private bool IsTerminal(string changeSetId, HashSet<string> terminalGroups)
        {
            if (terminalGroups.Contains(changeSetId))
                return true;

            if (Guid.TryParse(changeSetId, out var id) &&
                _changeSetJournal.Get(id) is { Status: HSMServer.Core.ChangeSets.ChangeSetStatus.RolledBack })
            {
                terminalGroups.Add(changeSetId);

                return true;
            }

            return false;
        }

        public PolicyWriteResult<JournalRecordDetailsDto> GetRecord(string recordId, ClaimsPrincipal user,
            IJournalRecordAccess access = null)
        {
            access ??= new TokenJournalRecordAccess(_authorization, _users, user);

            if (!TryResolve(recordId, access, out var record))
                return PolicyWriteResult<JournalRecordDetailsDto>.Fail(PolicyWriteOutcome.NotFound);

            return PolicyWriteResult<JournalRecordDetailsDto>.Ok(new JournalRecordDetailsDto
            {
                Record = Render(record),
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

        // The sight boundary of a record: the stamped root product when
        // present (every record the unified write paths produce), else the
        // entity resolved against the live tree — legacy records and records
        // of deleted entities fall to the admin-only Global scope.
        private ApiTokenResource ResourceOf(JournalRecordModel record)
        {
            if (Guid.TryParse(record.ProductId, out var productId) && productId != Guid.Empty)
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

        private static bool Matches(JournalRecordModel record, JournalFilter filter) =>
            (filter.Author is null ||
             record.Initiator?.Contains(filter.Author, StringComparison.OrdinalIgnoreCase) == true) &&
            (filter.Kind is null ||
             string.Equals(record.Kind ?? OtherKind, filter.Kind, StringComparison.OrdinalIgnoreCase)) &&
            (filter.ProductId is not { } product || record.ProductId == product.ToString()) &&
            (filter.EntityId is not { } entity || record.Key.Id == entity);

        private static JournalRecordDto Render(JournalRecordModel record) => new()
        {
            Id = JournalRecordId.Of(record),
            Time = new DateTime(record.Key.Time, DateTimeKind.Utc),
            Author = record.Initiator,
            Kind = record.Kind,
            ProductId = record.ProductId,
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
