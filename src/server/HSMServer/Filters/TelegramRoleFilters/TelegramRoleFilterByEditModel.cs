using HSMServer.Model.Authentication;
using HSMServer.Model.Notifications;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.TelegramRoleFilters
{
    public class TelegramRoleFilterByEditModel : TelegramRoleFilterBase
    {
        public TelegramRoleFilterByEditModel(string argumentName, params ProductRoleEnum[] roles) : base(argumentName, roles) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext context = null) =>
            arg is TelegramChatViewModel chatVM ? chatVM.Id : null;
    }
}
