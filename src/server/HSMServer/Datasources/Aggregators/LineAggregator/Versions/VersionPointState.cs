using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Datasources.Aggregators
{
    public readonly struct VersionPointState : ILinePointState<Version>
    {
        internal HashSet<(DateTime, Version)> AggrState { get; init; } = [];


        public DateTime Time { get; init; }

        public Version Value { get; init; }


        public VersionPointState() { }


        internal VersionPointState SaveState()
        {
            AggrState.Add((Time, Value));

            return this;
        }


        public static VersionPointState GetLastState(VersionPointState first, VersionPointState second)
        {
            var union = first.AggrState.Union(second.AggrState).ToHashSet();

            var (lastUpdate, maxVersion) = union.Max();

            return new()
            {
                AggrState = union,

                Value = maxVersion,
                Time = lastUpdate,
            };
        }
    }
}