namespace HSMDataCollector.Core
{
    internal sealed class Endpoints
    {
        internal string ConnectionAddress { get; }


        internal string Bool => $"{ConnectionAddress}/bool";

        internal string Integer => $"{ConnectionAddress}/int";

        internal string Double => $"{ConnectionAddress}/double";

        internal string String => $"{ConnectionAddress}/string";

        internal string Timespan => $"{ConnectionAddress}/timespan";

        internal string Version => $"{ConnectionAddress}/version";


        internal string DoubleBar => $"{ConnectionAddress}/doubleBar";

        internal string IntBar => $"{ConnectionAddress}/intBar";


        internal string List => $"{ConnectionAddress}/list";

        internal string File => $"{ConnectionAddress}/file";


        internal string TestConnection => $"{ConnectionAddress}/testConnection";


        internal Endpoints(CollectorOptions options)
        {
            ConnectionAddress = $"{options.ServerAddress}:{options.Port}/api/sensors";
        }
    }
}
