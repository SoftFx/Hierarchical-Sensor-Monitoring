using HSMSensorDataObjects;
using HSMServer.Helpers;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public abstract class NodeViewModel
    {
        private const int NodeNameMaxLength = 35;


        public Guid Id { get; protected set; }

        public string Name { get; protected set; }

        public DateTime UpdateTime { get; protected set; }

        public virtual SensorStatus Status { get; protected set; }

        public NodeViewModel Parent { get; internal set; }

        public string EncodedId => SensorPathHelper.EncodeGuid(Id);


        public string GetShortName(string name) =>
            name.Length > NodeNameMaxLength ? $"{name[..NodeNameMaxLength]}..." : name;
    }
}
