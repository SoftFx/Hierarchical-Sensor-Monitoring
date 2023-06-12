using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.TreeStateSnapshot.States;
using System;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class LastSensorState : ILastState<SensorStateEntity>
    {
        public LastHistoryPeriod History { get; } = new();

        public bool IsExpired { get; set; } //TTL


        public bool IsDefault => History.IsDefault && !IsExpired;


        public void FromEntity(SensorStateEntity entity)
        {
            IsExpired = entity.IsExpired;
            History.From = new DateTime(entity.HistoryFrom);
            History.To = entity.HistoryTo == 0L ? DateTime.MaxValue : new DateTime(entity.HistoryTo);
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


        public DateTime From { get; set; } = _fromDefault;

        public DateTime To { get; set; } = _toDefault;

        internal bool IsDefault => From == _fromDefault && To == _toDefault;
    }
}