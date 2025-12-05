using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.TreeStateSnapshot.States;
using System;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class LastSensorState : ILastState<SensorStateEntity>
    {
        public LastHistoryPeriod History { get; private set; } = new();

        public bool IsExpired { get; set; } //TTL


        public bool IsDefault => History.IsDefault && !IsExpired;


        public void SetLastUpdate(DateTime lastUpdate, bool isExpired = false)
        {
            IsExpired = isExpired;
            History.Update(lastUpdate);
        }


        public void FromEntity(SensorStateEntity entity)
        {
            IsExpired = entity.IsExpired;

            History = new LastHistoryPeriod(new DateTime(entity.HistoryFrom), entity.HistoryTo == 0L ? DateTime.MaxValue : new DateTime(entity.HistoryTo));
        }

        public SensorStateEntity ToEntity() =>
            new()
            {
                IsExpired = IsExpired,
                HistoryFrom = History.From.Ticks,
                HistoryTo = History.To == DateTime.MaxValue ? 0L : History.To.Ticks,
            };
    }


    public sealed class LastHistoryPeriod
    {
        private static readonly DateTime _fromDefault = DateTime.MinValue;
        private static readonly DateTime _toDefault = DateTime.MinValue;

        public LastHistoryPeriod()
        { }

        public LastHistoryPeriod(DateTime from, DateTime to)
        {
            From = from;
            To = to;
        }

        public DateTime From { get; private set; } = _fromDefault;

        public DateTime To { get; private set; } = _toDefault;

        internal bool IsDefault => From == _fromDefault && To == _toDefault;

        public void Update(DateTime time)
        {
            if (From == _fromDefault)
                From = time;

            if (To < time)
                To = time;
        }

        public void Cut(DateTime time)
        {
            From = time;
        }
    }
}