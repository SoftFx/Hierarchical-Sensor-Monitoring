using HSMServer.Model.Authentication;
using HSMServer.Model.Folders.ViewModels;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace HSMServer.Filters.FolderRoleFilters
{
    public sealed class FolderRoleFilterByEditAlerts : FolderRoleFilterBase
    {
        public FolderRoleFilterByEditAlerts(string argumentName, params ProductRoleEnum[] roles) : base(argumentName, roles) { }


        protected override Guid? GetEntityId(object arg, ActionExecutingContext context = null) =>
            arg is FolderAlertsViewModel folderAlerts ? folderAlerts.Id : null;
    }
}
