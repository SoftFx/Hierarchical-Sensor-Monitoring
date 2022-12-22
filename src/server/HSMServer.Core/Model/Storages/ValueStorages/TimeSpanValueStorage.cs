namespace HSMServer.Core.Model;

public class TimeSpanValueStorage : ValuesStorage<TimeSpanValue>
{
    protected override int CacheSize => 100;
}