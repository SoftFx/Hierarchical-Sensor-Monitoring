using HSMServer.Model.Authentication;
using System;

namespace HSMServer.Filters.FolderRoleFilters
{
    public abstract class FolderRoleFilterBase : UserRoleFilterBase
    {
        public FolderRoleFilterBase(string argumentName, params ProductRoleEnum[] parameters) : base(argumentName, parameters) { }


        protected override bool HasRole(User user, Guid? entityId, ProductRoleEnum role) =>
            user.FoldersRoles.TryGetValue(entityId.Value, out var folderRole) && folderRole == role;
    }
}
