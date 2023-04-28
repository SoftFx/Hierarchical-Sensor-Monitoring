using HSMServer.Model.Authentication;
using System;

namespace HSMServer.Filters.FolderRoleFilters
{
    public abstract class FolderRoleFilterBase : UserRoleFilterBase
    {
        public FolderRoleFilterBase(params ProductRoleEnum[] parameters) : base(parameters) { }


        protected override bool HasRole(User user, Guid? entityId, ProductRoleEnum role) =>
            user.FoldersRoles.TryGetValue(entityId.Value, out var folderRole) && folderRole == role;
    }
}
