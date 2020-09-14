using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.Core;

namespace HSMClient
{
    public class SensorsServiceClient
    {
        public SensorsServiceClient()
        {
            var channel = new Channel("localhost", 5001, ChannelCredentials.Insecure);
            //SensorsServiceClient.
        }
    }
}
