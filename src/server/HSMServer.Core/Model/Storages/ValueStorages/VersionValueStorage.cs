namespace HSMServer.Core.Model;

public class VersionValueStorage : ValuesStorage<VersionValue>
{
    protected override int CacheSize => 20;
}