using HSMServer.Authentication;
using HSMServer.Notifications.Chats;
using System;
using System.Linq;
using System.Security.Claims;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>
    /// The two chat predicates every management-surface service composes from the
    /// evaluator's primitives — chats carry no resource kind of their own. One home, so the
    /// verification and the write surfaces cannot drift apart.
    /// </summary>
    internal static class ChatSight
    {
        /// <summary>
        /// The caller sees the chat when it is GLOBAL (bound to no folder — attachable from
        /// every node's policies) or bound to at least one folder the caller can see; the
        /// read service's list predicate (ChatsReadService.IsChatVisible).
        /// </summary>
        public static bool IsVisible(ClaimsPrincipal caller, Chat chat, IApiTokenAuthorizationService authorization)
        {
            // chat.Folders mutates from binding edits without a lock — snapshot before asking.
            var folders = StableCopy.Of(() => chat.Folders);

            return folders.Count == 0 || folders.Any(id => authorization.IsVisible(caller, ApiTokenResource.Folder(id)));
        }

        /// <summary>
        /// The caller may CHANGE the chat — its detail rule, the token-side twin of the web
        /// editor's gate (TelegramRoleFilterById): an admin or the manager of a folder the
        /// chat is bound to. A global chat passes no folders, so changing it is admin-only.
        /// </summary>
        public static bool CanOperate(ClaimsPrincipal caller, Chat chat, IApiTokenAuthorizationService authorization) =>
            authorization.CanSeeChatDetail(caller, StableCopy.Of(() => chat.Folders));
    }
}
