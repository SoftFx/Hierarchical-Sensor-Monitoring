using HSMServer.Core.Model;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace HSMServer.Notifications
{
    internal sealed class NotificatableComparator : IEqualityComparer<INotificatable>
    {
        public bool Equals(INotificatable x, INotificatable y) => x.Id == y.Id;

        public int GetHashCode([DisallowNull] INotificatable obj) => obj.Id.GetHashCode();
    }
}
