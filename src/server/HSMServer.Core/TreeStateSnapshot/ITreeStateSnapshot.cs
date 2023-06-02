using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Core.TreeStateSnapshot
{
    public interface ITreeStateSnapshot
    {
        ISnapshotCollection<LastSensorState> Sensors { get; }

        public bool HasData { get; }

        public bool IsFinal { get; }


        Task FlushState(bool isFinal);
    }


    public interface ISnapshotCollection<T> : IDictionary<Guid, T> { }
}
