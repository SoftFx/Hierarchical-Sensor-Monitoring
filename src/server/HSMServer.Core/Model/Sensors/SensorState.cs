namespace HSMServer.Core.Model
{
    public enum SensorState : byte
    {
        Available,
        Muted,
        Blocked = byte.MaxValue,
    }
}
