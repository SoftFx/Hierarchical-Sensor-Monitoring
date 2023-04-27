using HSMServer.Model.Authentication;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.FolderRoleFilters
{
    public sealed class FolderRoleFilterByUserRights : FolderRoleFilterBase
    {
        protected override string ArgumentName => "model";


        public FolderRoleFilterByUserRights(params ProductRoleEnum[] roles) : base(roles) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext context = null) =>
            arg is UserRightViewModel model ? model.EntityId : null;
    }
}
