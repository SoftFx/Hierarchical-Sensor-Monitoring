using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMDatabase.AccessManager
{
    public interface ISnapshotDatabase
    {
        IEntitySnapshotNode BuildNode(bool isFinal);

        bool TryGetLastNode(out IEntitySnapshotNode node);
    }


    public interface IEntitySnapshotNode
    {
        IEntitySnapshotCollection<SensorStateEntity> Sensors { get; }

        bool IsFinal { get; }


        Task Save();
    }


    public interface IEntitySnapshotCollection
    {
        Task Save();
    }


    public interface IEntitySnapshotCollection<T> : IEntitySnapshotCollection
    {
        Dictionary<Guid, T> Data { get; set; }


        Dictionary<Guid, T> Read();
    }
}
