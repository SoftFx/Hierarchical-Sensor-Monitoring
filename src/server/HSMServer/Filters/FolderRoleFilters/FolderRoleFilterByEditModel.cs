using HSMServer.Model.Authentication;
using HSMServer.Model.Folders.ViewModels;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.FolderRoleFilters
{
    public sealed class FolderRoleFilterByEditModel : FolderRoleFilterBase
    {
        protected override string ArgumentName => "editFolder";


        public FolderRoleFilterByEditModel(params ProductRoleEnum[] roles) : base(roles) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext context = null) =>
            arg is EditFolderViewModel folderVM ? folderVM.Id : null;
    }
}
