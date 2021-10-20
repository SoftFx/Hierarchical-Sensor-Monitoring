using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.Core.Authentication
{
    public class UsersComparer : IEqualityComparer<User>
    {
        public bool Equals(User x, User y)
        {
            if (x == null && y == null)
                return true;

            if (x == null || y == null)
                return false;

            return x.Id == y.Id;
        }

        public int GetHashCode(User obj)
        {
            return obj.Id.GetHashCode();
        }
    }
}
