using HSMServer.Model.Authentication;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.FolderRoleFilters
{
    public sealed class FolderRoleFilterByUserRights : FolderRoleFilterBase
    {
        public FolderRoleFilterByUserRights(string argumentName, params ProductRoleEnum[] roles) : base(argumentName, roles) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext context = null) =>
            arg is UserRightViewModel model ? model.EntityId : null;
    }
}
