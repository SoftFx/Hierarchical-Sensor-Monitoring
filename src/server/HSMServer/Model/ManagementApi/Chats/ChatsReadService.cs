using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using HSMServer.Authentication;
using HSMServer.Model.ManagementApi.SensorTree;
using HSMServer.Notifications.Chats;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>
    /// The read surface for notification chats: list and item, with the
    /// visibility rule that decides what a caller may see. Transport-agnostic —
    /// the REST controller and the MCP chat tools both call it; the caller
    /// supplies the token principal and gets DTOs or a
    /// <see cref="SensorTreeReadResult{T}"/> failure back, never an
    /// IActionResult (the shared read envelope of the management area).
    /// </summary>
    // Chats are the id source the alert/template write endpoints validate
    // destination.chats against: without this surface an API-only client
    // cannot discover a single valid chat id, so the existing write endpoints
    // are not usable end-to-end (the gap this service closes).
    //
    // Visibility semantics (composed from the evaluator's existing primitives,
    // the schedules' caller-wide gate + the templates' memoized folder sight):
    //  - the LIST and the ITEM answers require the caller-wide gate — the
    //    owner is an admin or currently holds a role on at least one
    //    product/folder (CanSeeAnyBoundary, denial audit record inside). An
    //    owner who sees no boundary has no sensors to attach chats to, so
    //    global chat names must not leak to them either;
    //  - a gated caller sees a chat when it is GLOBAL (bound to no folder —
    //    attachable from every node's policies, and the web UI's alert editor
    //    offers such chats to every manager) or bound to at least one folder
    //    the owner can see; out-of-sight chats are silently absent from the
    //    list, and an invisible id answers the SAME NotFound as an unknown one.
    public sealed class ChatsReadService
    {
        private readonly IChatsManager _chats;
        private readonly IApiTokenAuthorizationService _authorization;

        public ChatsReadService(IChatsManager chats, IApiTokenAuthorizationService authorization)
        {
            _chats = chats;
            _authorization = authorization;
        }


        /// <summary>
        /// Chats visible to the token's owner, ordered by name then id,
        /// paginated with the area's shared clamps. Requires the caller-wide
        /// gate; within it, out-of-sight chats are simply not listed, never a
        /// per-item failure.
        /// </summary>
        public SensorTreeReadResult<ApiPageDto<ChatDto>> ListChats(ClaimsPrincipal user, int page, int pageSize,
            CancellationToken cancellationToken = default)
        {
            if (!_authorization.CanSeeAnyBoundary(user))
                return SensorTreeReadResult<ApiPageDto<ChatDto>>.Fail(
                    SensorTreeReadOutcome.Forbidden, message: ManagementApiErrors.NoBoundarySightMessage);

            (page, pageSize) = ApiPagination.Normalize(page, pageSize);

            // The per-folder sight decision, memoized per DISTINCT folder within
            // one request (the evaluator re-resolves user + token on every call;
            // IsVisible records nothing, unlike per-item authorization).
            var isFolderVisible = _authorization.MemoizedFolderVisibility(user);

            var visibleChats = _chats.GetValues()
                .Where(chat =>
                {
                    // A disconnected caller must not keep the per-item evaluator
                    // pass running (the sensor-tree scan's rule).
                    cancellationToken.ThrowIfCancellationRequested();

                    return IsChatVisible(chat, isFolderVisible);
                })
                .OrderBy(chat => chat.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(chat => chat.Id)
                .ToList();

            var totalPages = ApiPagination.TotalPagesOf(visibleChats.Count, pageSize);
            page = ApiPagination.ClampPage(page, totalPages);

            return SensorTreeReadResult<ApiPageDto<ChatDto>>.Ok(new ApiPageDto<ChatDto>
            {
                Items = [.. visibleChats.Skip((page - 1) * pageSize).Take(pageSize).Select(ChatDtoMapper.ToDto)],
                Page = page,
                PageSize = pageSize,
                TotalCount = visibleChats.Count,
                TotalPages = totalPages,
            });
        }


        /// <summary>
        /// One chat by id (same caller-wide gate as the list). Unknown and
        /// invisible ids answer the SAME NotFound — the area's
        /// anti-enumeration rule.
        /// </summary>
        public SensorTreeReadResult<ChatDto> GetChat(Guid id, ClaimsPrincipal user)
        {
            if (!_authorization.CanSeeAnyBoundary(user))
                return SensorTreeReadResult<ChatDto>.Fail(
                    SensorTreeReadOutcome.Forbidden, message: ManagementApiErrors.NoBoundarySightMessage);

            // The storage indexer answers null for an unknown id (the area's
            // dictionary semantics) — the same lookup shape AlertReadService
            // uses for templates.
            var chat = _chats[id];

            if (chat is null)
                return SensorTreeReadResult<ChatDto>.Fail(SensorTreeReadOutcome.NotFound);

            // The list predicate is the item read decision, so an item is
            // listed exactly when GET {id} would answer it (the templates'
            // invariant). The memoization is per request; a single-item lookup
            // walks at most the chat's bound folders.
            return IsChatVisible(chat, _authorization.MemoizedFolderVisibility(user))
                ? SensorTreeReadResult<ChatDto>.Ok(ChatDtoMapper.ToDto(chat))
                : SensorTreeReadResult<ChatDto>.Fail(SensorTreeReadOutcome.NotFound);
        }


        // A chat is visible when it is GLOBAL (bound to no folder) or bound to
        // at least one folder the token's owner can see. Chats carry no
        // ApiTokenResourceKind of their own — there is no chat boundary in the
        // evaluator — so this composition of the folder sight IS the chat
        // sight rule, and it matches the availability rule policy/template
        // writes validate destination chats against (global chats everywhere,
        // folder-bound chats only inside their folders).
        private static bool IsChatVisible(Chat chat, Func<Guid, bool> isFolderVisible) =>
            chat.Folders.Count == 0 || chat.Folders.Any(isFolderVisible);
    }
}
