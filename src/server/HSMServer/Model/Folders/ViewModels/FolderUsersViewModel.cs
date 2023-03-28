using HSMServer.Model.Authentication;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderUsersViewModel
    {
        public Dictionary<User, ProductRoleEnum> Users { get; }

        public HashSet<User> NotAdminUsers { get; }


        public FolderUsersViewModel() { }

        public FolderUsersViewModel(Dictionary<User, ProductRoleEnum> folderUsers, IEnumerable<User> notAdminUsers)
        {
            Users = folderUsers;

            NotAdminUsers = notAdminUsers.OrderBy(u => u.Name).ToHashSet();
            NotAdminUsers.ExceptWith(Users.Keys);
        }
    }
}
