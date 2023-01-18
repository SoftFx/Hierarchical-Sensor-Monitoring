namespace HSMDataCollector.PublicInterface
{
    public interface IUnixCollection 
    {
        IUnixCollection AddProcessCPUSensor(string nodePath);
    }
}
