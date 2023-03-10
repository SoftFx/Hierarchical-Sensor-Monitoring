using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class PathCompressor : ConcurrentDictionary<Guid, (string, string)>
    {
        private readonly ConcurrentDictionary<Guid, BaseSensorModel> _sensors = new();
        private readonly List<GroupedPath> _groups = new();


        internal bool TryGetOrAdd(BaseSensorModel sensor, out (string oldStatus, string) key)
        {
            var id = sensor.Id;

            if (TryGetValue(id, out key))
                return true;

            _sensors[id] = sensor;
            return false;
        }

        internal IEnumerable<string> GetGroupedPaths(CHash hash)
        {
            foreach (var id in hash)
                if (_sensors.TryGetValue(id, out var sensor))
                    ApplyToGroups(sensor.Path);

            foreach (var group in _groups)
                yield return group.ToString();
        }

        internal new void Clear()
        {
            _sensors.Clear();
            _groups.Clear();

            base.Clear();
        }


        private void ApplyToGroups(string strPath)
        {
            var path = strPath.Split(GroupedPath.Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var group in _groups)
                if (group.Apply(path))
                    return;

            _groups.Add(new GroupedPath(path));
        }
    }
}
