using System;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;

namespace HSMServer.Core.TreeStateSnapshot.States;

public class KeyState : ILastState<KeyStateEntity>
{
    public string IP { get; set; }
    
    public DateTime LastUse { get; set; }

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
}