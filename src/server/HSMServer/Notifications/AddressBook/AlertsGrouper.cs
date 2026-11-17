using HSMCommon.Collections;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Notifications.AddressBook
{
    internal sealed class AlertsGrouper : CDictBase<(string, int), List<GroupedNotification>>
    {
        internal void ApplyToGroup(AlertResult result)
        {
            var groups = GetOrAdd(result.Key, new List<GroupedNotification>());

            foreach (var group in groups)
                if (group.TryApply(result))
                    return;

            groups.Add(new GroupedNotification(result));
        }

        // Hands the groups over and forgets them before any of them is rendered, so a group that
        // fails to render is not kept for the next message.
        internal List<GroupedNotification> TakeGroups()
        {
            var groups = this.SelectMany(p => p.Value).OrderBy(g => g.FirstNotifyTime).ToList();

            Clear();

            return groups;
        }
    }
}