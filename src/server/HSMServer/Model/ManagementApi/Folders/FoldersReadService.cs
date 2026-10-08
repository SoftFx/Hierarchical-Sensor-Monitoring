using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using HSMServer.Authentication;
using HSMServer.Folders;
using HSMServer.Model.ManagementApi.SensorTree;

namespace HSMServer.Model.ManagementApi.Folders
{
    /// <summary>
    /// The read surface for access-grouping folders: list and item, with the
    /// owner-sight visibility rule. Transport-agnostic — the REST controller
    /// and the MCP folder tools both call it; the caller supplies the token
    /// principal and gets DTOs or a <see cref="SensorTreeReadResult{T}"/>
    /// failure back, never an IActionResult (the shared read envelope of the
    /// management area).
    /// </summary>
    // Folders are the folderId source of alert-template writes: the REST
    // template endpoint validates folderId against IFolderManager (the folder
    // must exist, be visible and contain products), but until this surface no
    // /api/v1 endpoint enumerated Folder entities — ids were discoverable
    // only indirectly, from the templates that referenced them.
    //
    // Visibility semantics (the templates pattern verbatim — folders ARE the
    // per-item boundary, unlike the global chats/schedules):
    //  - an item is listed exactly when GET {id} would answer it: the
    //    owner-sight half of the read decision (IsVisible at the Folder
    //    boundary), memoized per distinct folder within one request;
    //    out-of-sight folders are silently absent, never a per-item 403;
    //  - unknown and invisible ids answer the SAME NotFound — the area's
    //    anti-enumeration rule.
    public sealed class FoldersReadService
    {
        private readonly IFolderManager _folders;
        private readonly IApiTokenAuthorizationService _authorization;

        public FoldersReadService(IFolderManager folders, IApiTokenAuthorizationService authorization)
        {
            _folders = folders;
            _authorization = authorization;
        }


        /// <summary>
        /// Folders visible to the token's owner, ordered by name then id,
        /// paginated with the area's shared clamps. An owner who sees nothing
        /// gets an empty list: out-of-sight folders are simply not listed,
        /// never a per-item failure.
        /// </summary>
        public ApiPageDto<FolderDto> ListFolders(ClaimsPrincipal user, int page, int pageSize,
            CancellationToken cancellationToken = default)
        {
            (page, pageSize) = ApiPagination.Normalize(page, pageSize);

            // The list predicate is the owner-sight half of the item read
            // decision, memoized per DISTINCT folder (the evaluator re-resolves
            // user + token on every call; IsVisible records nothing, unlike
            // per-item authorization).
            var decisionByFolder = new Dictionary<Guid, bool>();

            bool IsListable(Guid folderId) =>
                decisionByFolder.TryGetValue(folderId, out var listable)
                    ? listable
                    : decisionByFolder[folderId] = _authorization.IsVisible(user, FolderResource(folderId));

            var visible = _folders.GetValues()
                .Where(folder =>
                {
                    // A disconnected caller must not keep the per-item evaluator
                    // pass running (the sensor-tree scan's rule).
                    cancellationToken.ThrowIfCancellationRequested();

                    return IsListable(folder.Id);
                })
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(folder => folder.Id)
                .ToList();

            var totalPages = ApiPagination.TotalPagesOf(visible.Count, pageSize);
            page = ApiPagination.ClampPage(page, totalPages);

            return new ApiPageDto<FolderDto>
            {
                Items = [.. visible.Skip((page - 1) * pageSize).Take(pageSize).Select(FolderDtoMapper.ToDto)],
                Page = page,
                PageSize = pageSize,
                TotalCount = visible.Count,
                TotalPages = totalPages,
            };
        }


        /// <summary>
        /// One folder by id. Unknown and invisible ids answer the SAME
        /// NotFound (the area's anti-enumeration rule).
        /// </summary>
        public SensorTreeReadResult<FolderDto> GetFolder(Guid id, ClaimsPrincipal user)
        {
            // The storage indexer answers null for an unknown id (the area's
            // dictionary semantics).
            var folder = _folders[id];

            if (folder is null)
                return SensorTreeReadResult<FolderDto>.Fail(SensorTreeReadOutcome.NotFound);

            // Reads authorize at the folder's OWN boundary: the evaluator's
            // 404 arm keeps invisible indistinguishable from unknown; the
            // Forbidden arm is unreachable in the owner-mirror read model and
            // kept only so the evaluator's full decision surface maps to a
            // response (the templates' item pattern).
            return _authorization.AuthorizeRead(user, FolderResource(id)) switch
            {
                ApiTokenAuthorization.Allowed =>
                    SensorTreeReadResult<FolderDto>.Ok(FolderDtoMapper.ToDto(folder)),
                ApiTokenAuthorization.Forbidden => SensorTreeReadResult<FolderDto>.Fail(
                    SensorTreeReadOutcome.Forbidden, message: "The token's owner cannot see this folder."),
                _ => SensorTreeReadResult<FolderDto>.Fail(SensorTreeReadOutcome.NotFound),
            };
        }


        private static ApiTokenResource FolderResource(Guid folderId) =>
            new(ApiTokenResourceKind.Folder, folderId);
    }
}
