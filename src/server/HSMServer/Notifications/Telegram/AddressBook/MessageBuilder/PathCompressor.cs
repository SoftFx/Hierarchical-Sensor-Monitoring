using HSMServer.Core;
using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class PathCompressor : ConcurrentDictionary<Guid, string>
    {
        private readonly ConcurrentDictionary<Guid, (BaseSensorModel, SensorStatus)> _inRestore = new();
        private readonly ConcurrentDictionary<Guid, BaseSensorModel> _sensors = new();
        private readonly ConcurrentQueue<GroupedPath> _groups = new();


        internal bool TryGetOrAdd(BaseSensorModel sensor, out string oldStatus)
        {
            var id = sensor.Id;

            //TryAddInRestore(sensor, firstStatus);

            if (TryGetValue(id, out oldStatus))
                return true;

            _sensors[id] = sensor;
            this[id] = oldStatus;

            return false;
        }

        internal IEnumerable<string> GetGroupedPaths(CGuidHash hash)
        {
            //foreach (var id in hash)
            //    if (!_inRestore.ContainsKey(id))
            //    {
            //        if (_sensors.TryGetValue(id, out var sensor) && !TryAddInRestore(sensor))
            //            ApplyToGroups(sensor);

            //        if (!_inRestore.ContainsKey(id))
            //            hash.Remove(id);
            //    }

            ////foreach ((var id, (var sensor, var firstState)) in _inRestore)
            ////    if (hash.Contains(id) && !sensor.IsWaitRestore)
            ////    {
            ////        if (sensor.Status.IsOk && firstState.IsOk())
            ////            RemoveSensor(id);
            ////        else
            ////            ApplyToGroups(sensor);

            ////        hash.Remove(id);
            ////    }

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

            _groups.Enqueue(new GroupedPath(path));
        }


        //private bool TryAddInRestore(BaseSensorModel sensor, SensorStatus? firstStatus = null)
        //{
        //    //var ok = !sensor.Status.IsOk && sensor.IsWaitRestore;

        //    //if (ok)
        //    //    _inRestore.TryAdd(sensor.Id, (sensor, firstStatus ?? sensor.Status.Status));

        //    //return ok;

        //    return false;
        //}


        private void RemoveSensor(Guid id)
        {
            _inRestore.Remove(id, out _);
            _sensors.Remove(id, out _);
            this.Remove(id, out _);
        }
    }
}
