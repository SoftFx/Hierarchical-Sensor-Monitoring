using HSMServer.Controllers;
using HSMServer.Filters.FolderRoleFilters;
using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;

namespace HSMServer.Filters.TelegramRoleFilters
{
    public abstract class TelegramRoleFilterBase : FolderRoleFilterBase
    {
        public TelegramRoleFilterBase(string argumentName, params ProductRoleEnum[] parameters) : base(argumentName, parameters) { }


        protected override bool TryCheckRole(ActionExecutingContext context, User user)
        {
            if (TryGetEntityId(context, out var chatId))
                foreach (var folderId in GetFolderIds(chatId.Value, context))
                    foreach (var role in _roles)
                        if (HasRole(user, folderId, role))
                            return true;

            return false;
        }


        private static List<Guid> GetFolderIds(Guid chatId, ActionExecutingContext context)
        {
            var folderIds = new List<Guid>(1 << 2);

            if (context.Controller is NotificationsController controller && controller.ChatsManager.TryGetValue(chatId, out var chat))
                folderIds.AddRange(chat.Folders);

            return folderIds;
        }
    }
}
