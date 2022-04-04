using HSMSensorDataObjects;
using HSMServer.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Model.TreeViewModels
{
    public class NodeViewModel
    {
        private const int NodeNameMaxLength = 35;

        public string Id { get; protected set; }
        public string EncodedId => SensorPathHelper.Encode(Id);
        public string Name { get; set; }
        public SensorStatus Status { get; set; }
        public DateTime UpdateTime { get; set; }


        public string GetShortName(string name) =>
            name.Length > NodeNameMaxLength ? $"{name[..NodeNameMaxLength]}..." : name;
    }
}
