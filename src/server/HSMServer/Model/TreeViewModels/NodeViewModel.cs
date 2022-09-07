using HSMServer.Core.Model;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public abstract class NodeViewModel
    {
        private const int NodeNameMaxLength = 35;


        public string Name { get; protected set; }

        public DateTime UpdateTime { get; internal set; }

        public virtual SensorStatus Status { get; internal set; }

        public NodeViewModel Parent { get; internal set; }

        public string Path { get; internal set; }


        public string GetShortName(string name) =>
            name.Length > NodeNameMaxLength ? $"{name[..NodeNameMaxLength]}..." : name;
    }
}
