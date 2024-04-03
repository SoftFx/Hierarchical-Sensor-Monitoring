using System;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.Model;

namespace HSMServer.Core.TreeStateSnapshot.States;

public class KeyState : ILastState<KeyStateEntity>
{
    public string IP { get; private set; }
    
    public DateTime LastUseTime { get; private set; }

    public bool IsDefault => false;
    
    
    public void FromEntity(KeyStateEntity entity)
    {
        IP = entity.IP;
        LastUseTime = new DateTime(entity.LastUseTicks);
    }

    public KeyStateEntity ToEntity() => new()
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