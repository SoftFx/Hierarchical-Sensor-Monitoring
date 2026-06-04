using System;
using HSMDataCollector.Core;

namespace HSMDataCollector.Client
{
    internal sealed class Endpoints
    {
        internal string ConnectionAddress { get; }


        internal string AddOrUpdateSensor => $"{ConnectionAddress}/addOrUpdate";

        internal string CommandsList => $"{ConnectionAddress}/commands";


        internal string Bool => $"{ConnectionAddress}/bool";

        internal string Integer => $"{ConnectionAddress}/int";

        internal string Double => $"{ConnectionAddress}/double";

        internal string String => $"{ConnectionAddress}/string";

        internal string Timespan => $"{ConnectionAddress}/timespan";

        internal string Version => $"{ConnectionAddress}/version";

        internal string Rate => $"{ConnectionAddress}/rate";


        internal string DoubleBar => $"{ConnectionAddress}/doubleBar";

        internal string IntBar => $"{ConnectionAddress}/intBar";


        internal string List => $"{ConnectionAddress}/list";

        internal string File => $"{ConnectionAddress}/file";


        internal string TestConnection => $"{ConnectionAddress}/testConnection";


        internal Endpoints(CollectorOptions options)
        {
            var builder = new UriBuilder(options.ServerUrl)
            {
                Port = options.Port,
                Scheme = "https",
                Path = "api/sensors",
            };

            ConnectionAddress = $"{builder.Uri}";
        }
    }
}
