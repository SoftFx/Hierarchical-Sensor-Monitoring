namespace TestLevelDB.Server
{
    public interface IServer
    {
        string Type { get; }


        Task InitServer();

        Task CloseDatabase();


        Task AddData(Data[] data);


        Task ReadEachFirstSensorData();

        Task ReadEachLastSensorData();

        Task ReadEachAllSensorData();
    }
}