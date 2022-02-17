using HSMServer.Core.Model.Authentication;
using System.Threading.Tasks;

namespace HSMServer.Core.Authentication
{
    public partial class UserManager
    {
        private protected abstract class UserActionsHandler
        {
            private const int MaxAttemptsCount = 3;

            protected readonly UserManager _userManager;


            protected UserActionsHandler(UserManager userManager)
            {
                _userManager = userManager;
            }


            internal async Task Apply(User user, bool callDatabaseAction = true)
            {
                int count = 0;
                while (count++ < MaxAttemptsCount)
                {
                    if (!TryUserAction(user))
                    {
                        await Task.Yield();
                        continue;
                    }

                    PostUserAction(user);

                    if (callDatabaseAction)
                        DatabaseAction(user);

                    return;
                }
            }

            protected abstract bool TryUserAction(User user);

            protected abstract void PostUserAction(User user);

            protected abstract void DatabaseAction(User user);
        }


        private sealed class AddUserActionHandler : UserActionsHandler
        {
            internal AddUserActionHandler(UserManager userManager)
                : base(userManager) { }


            protected override bool TryUserAction(User user) =>
                _userManager._users.TryAdd(user.Id, user);

            protected override void PostUserAction(User user) =>
                _userManager._userNames.TryAdd(user.UserName, user.Id);

            protected override void DatabaseAction(User user) =>
                _userManager._databaseAdapter.AddUser(user);
        }


        private sealed class RemoveUserActionHandler : UserActionsHandler
        {
            internal RemoveUserActionHandler(UserManager userManager)
                : base(userManager) { }


            protected override bool TryUserAction(User user) =>
                _userManager._users.TryRemove(user.Id, out var _);

            protected override void PostUserAction(User user) =>
                _userManager._userNames.TryRemove(user.UserName, out var _);

            protected override void DatabaseAction(User user) =>
                _userManager._databaseAdapter.RemoveUser(user);
        }


        private sealed class UpdateUserActionHandler : UserActionsHandler
        {
            internal UpdateUserActionHandler(UserManager userManager)
                : base(userManager) { }


            protected override bool TryUserAction(User user)
            {
                if (!_userManager._users.TryGetValue(user.Id, out var existingUser))
                    return false;

                existingUser.Update(user);
                _userManager.UpdateUserEvent?.Invoke(existingUser);

                return true;
            }

            protected override void PostUserAction(User user) { }

            protected override void DatabaseAction(User user)
            {
                if (!_userManager._users.TryGetValue(user.Id, out var existingUser))
                    return;

                _userManager._databaseAdapter.UpdateUser(existingUser);
            }
        }
    }
}
