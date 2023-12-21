using HSMCommon.Collections;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Notifications.Telegram.AddressBook
{
    internal sealed class AlertsGrouper : CDictBase<(string, int), List<GroupedNotification>>
    {
        internal void ApplyToGroup(AlertResult result)
        {
            var groups = GetOrAdd(result.Key);

            foreach (var group in groups)
                if (group.TryApply(result.LastState))
                    return;

            groups.Add(new GroupedNotification(result));
        }

        internal IEnumerable<string> GetGroups()
        {
            foreach (var (_, groups) in this)
                foreach (var group in groups)
                    yield return group.ToString();

            Clear();
        }
    }
}