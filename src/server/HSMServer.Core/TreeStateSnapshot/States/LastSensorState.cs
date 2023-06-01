using HSMServer.Core.TreeStateSnapshot.States;
using System;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class LastSensorState : ILastState
    {
        public LastHistoryPeriod History { get; set; } = new();


        public bool IsDefault => History.IsDefault;
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