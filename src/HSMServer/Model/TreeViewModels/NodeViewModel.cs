using HSMSensorDataObjects;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public abstract class NodeViewModel
    {
        private const int NodeNameMaxLength = 35;


        public string Name { get; protected set; }

        public DateTime UpdateTime { get; protected set; }

        public virtual SensorStatus Status { get; protected set; }

        public NodeViewModel Parent { get; internal set; }


        public string GetShortName(string name) =>
            name.Length > NodeNameMaxLength ? $"{name[..NodeNameMaxLength]}..." : name;
    }
}
