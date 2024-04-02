using System;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.Model;

namespace HSMServer.Core.TreeStateSnapshot.States;

public class KeyState : ILastState<KeyStateEntity>
{
    public string IP { get; private set; }
    
    public DateTime LastUse { get; private set; }

    public bool IsDefault => false;
    
    
    public void FromEntity(KeyStateEntity entity)
    {
        IP = entity.IP;
        LastUse = new DateTime(entity.LastUseTime);
    }

    public KeyStateEntity ToEntity() => new()
    {
        IP = IP,
        LastUseTime = LastUse.Ticks
    };
    
    public void Update(AccessKeyModel key)
    {
        IP = key.IP;
        LastUse = key.LastUseTime;
    }
}