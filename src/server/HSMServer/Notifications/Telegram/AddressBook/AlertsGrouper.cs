using HSMCommon.Collections;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Notifications.Telegram.AddressBook
{
    internal sealed class AlertsGrouper : CDictBase<(string, int), List<GroupedNotification>>
    {
        internal void ApplyToGroup(AlertResult result)
        {
            var groups = GetOrAdd(result.Key);

            foreach (var group in groups)
                if (group.TryApply(result))
                    return;

            groups.Add(new GroupedNotification(result));
        }

        internal IEnumerable<string> GetGroups()
        {
            foreach (var group in this.SelectMany(p => p.Value).OrderBy(g => g.FirstNotifyTime))
                yield return group.ToString();

            Clear();
        }
    }
}