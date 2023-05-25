using HSMServer.Model.Authentication;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.FolderRoleFilters
{
    public sealed class FolderRoleFilterByFolderId : FolderRoleFilterBase
    {
        protected override string ArgumentName { get; set; } = "folderId";


        public FolderRoleFilterByFolderId(params ProductRoleEnum[] roles) : base(roles) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext context = null) =>
            arg is Guid folderId ? folderId : null;
    }
}
