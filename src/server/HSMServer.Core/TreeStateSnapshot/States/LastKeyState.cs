using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using System;

namespace HSMServer.Core.TreeStateSnapshot.States;

public sealed class LastKeyState : ILastState<LastKeyStateEntity>
{
    public string IP { get; private set; }

    public DateTime LastUseTime { get; private set; }

    public bool IsDefault => IP is null && LastUseTime == DateTime.MinValue;


    public void FromEntity(LastKeyStateEntity entity)
    {
        IP = entity.IP;
        LastUseTime = new DateTime(entity.LastUseTicks);
    }

    public LastKeyStateEntity ToEntity() => new()
    {
        IP = IP,
        LastUseTicks = LastUseTime.Ticks
    };

    public void Update(string ip, DateTime time)
    {
        IP = ip;
        LastUseTime = time;
    }
}