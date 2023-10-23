using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.TelegramRoleFilters
{
    public sealed class TelegramRoleFilterById : TelegramRoleFilterBase
    {
        public TelegramRoleFilterById(string argumentName, params ProductRoleEnum[] parameters) : base(argumentName, parameters) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext context = null) =>
            arg is Guid chatId ? chatId : null;
    }
}
