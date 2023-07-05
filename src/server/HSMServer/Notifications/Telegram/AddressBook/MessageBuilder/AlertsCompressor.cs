using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class AlertsCompressor : CDictBase<(string, int), List<GroupedNotification>>
    {
        //TODO: renove after creating Restore module
        //private readonly ConcurrentDictionary<Guid, (BaseSensorModel, SensorStatus)> _inRestore = new();
        //private readonly ConcurrentDictionary<Guid, BaseSensorModel> _sensors = new();
        //private readonly ConcurrentQueue<GroupedPath> _groups = new();

        //TODO: renove after creating Restore module
        //internal bool TryGetOrAdd(BaseSensorModel sensor, out string oldStatus)
        //{
        //    var id = sensor.Id;

        //    //TryAddInRestore(sensor, firstStatus);

        //    if (TryGetValue(id, out oldStatus))
        //        return true;

        //    _sensors[id] = sensor;
        //    this[id] = oldStatus;

        //    return false;
        //}

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
            foreach (var group in this)
                yield return group.Value.ToString();

            Clear();
        }

        //TODO: renove after creating Restore module
        //internal IEnumerable<string> GetGroupedPaths(CGuidHash hash)
        //{
        //    //foreach (var id in hash)
        //    //    if (!_inRestore.ContainsKey(id))
        //    //    {
        //    //        if (_sensors.TryGetValue(id, out var sensor) && !TryAddInRestore(sensor))
        //    //            ApplyToGroups(sensor);

        //    //        if (!_inRestore.ContainsKey(id))
        //    //            hash.Remove(id);
        //    //    }

        //    ////foreach ((var id, (var sensor, var firstState)) in _inRestore)
        //    ////    if (hash.Contains(id) && !sensor.IsWaitRestore)
        //    ////    {
        //    ////        if (sensor.Status.IsOk && firstState.IsOk())
        //    ////            RemoveSensor(id);
        //    ////        else
        //    ////            ApplyToGroups(sensor);

        //    ////        hash.Remove(id);
        //    ////    }

        //    //foreach (var group in _groups)
        //    //    yield return group.ToString();

        //    //_groups.Clear();
        //}

        //TODO: renove after creating Restore module
        //private bool TryAddInRestore(BaseSensorModel sensor, SensorStatus? firstStatus = null)
        //{
        //    //var ok = !sensor.Status.IsOk && sensor.IsWaitRestore;

        //    //if (ok)
        //    //    _inRestore.TryAdd(sensor.Id, (sensor, firstStatus ?? sensor.Status.Status));

        //    //return ok;

        //    return false;
        //}
    }
}
