using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class PathCompressor : ConcurrentDictionary<Guid, (string, string)>
    {
        private readonly ConcurrentDictionary<Guid, BaseSensorModel> _inRestore = new();
        private readonly ConcurrentDictionary<Guid, BaseSensorModel> _sensors = new();
        private readonly List<GroupedPath> _groups = new();


        internal bool TryGetOrAdd(BaseSensorModel sensor, out (string oldStatus, string) key)
        {
            var id = sensor.Id;

            TryAddInRestore(sensor);

            if (TryGetValue(id, out key))
                return true;

            _sensors[id] = sensor;
            this[id] = key;

            return false;
        }

        internal IEnumerable<string> GetGroupedPaths(CHash hash)
        {
            foreach (var id in hash)
                if (!_inRestore.ContainsKey(id))
                {
                    if (_sensors.TryGetValue(id, out var sensor) && !TryAddInRestore(sensor))
                        ApplyToGroups(sensor);

                    if (!_inRestore.ContainsKey(id))
                        hash.Remove(id);
                }

            foreach ((var id, var sensor) in _inRestore)
                if (hash.Contains(id) && !sensor.IsWaitRestore)
                {
                    if (sensor.Status.IsOk)
                        RemoveSensor(id);
                    else
                        ApplyToGroups(sensor);

                    hash.Remove(id);
                }

            foreach (var group in _groups)
                yield return group.ToString();

            _groups.Clear();
        }


        private void ApplyToGroups(BaseSensorModel sensor)
        {
            RemoveSensor(sensor.Id);

            var path = sensor.Path.Split(GroupedPath.Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var group in _groups)
                if (group.Apply(path))
                    return;

            _groups.Add(new GroupedPath(path));
        }


        private bool TryAddInRestore(BaseSensorModel sensor)
        {
            var ok = !sensor.Status.IsOk && sensor.IsWaitRestore;

            if (ok)
                _inRestore.TryAdd(sensor.Id, sensor);

            return ok;
        }


        private void RemoveSensor(Guid id)
        {
            _inRestore.Remove(id, out _);
            _sensors.Remove(id, out _);
            this.Remove(id, out _);
        }
    }
}
