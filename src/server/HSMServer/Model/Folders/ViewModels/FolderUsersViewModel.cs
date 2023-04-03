using HSMServer.Model.Authentication;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderUsersViewModel
    {
        public Dictionary<User, ProductRoleEnum> Users { get; }

        public HashSet<User> NotAdminUsers { get; }

        public string FolderName { get; }


        public FolderUsersViewModel() { }

        public FolderUsersViewModel(FolderModel folder, IEnumerable<User> notAdminUsers)
        {
            FolderName = folder.Name;
            Users = folder.UserRoles;

            NotAdminUsers = notAdminUsers.OrderBy(u => u.Name).ToHashSet();
            NotAdminUsers.ExceptWith(Users.Keys);
        }
    }
}
