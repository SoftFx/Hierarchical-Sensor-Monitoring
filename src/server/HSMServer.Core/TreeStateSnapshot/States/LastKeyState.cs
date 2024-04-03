using System;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.Model;

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
    
    public void Update(AccessKeyModel key)
    {
        IP = key.IP;
        LastUseTime = key.LastUseTime;
    }
}