using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public abstract class NodeViewModel
    {
        private const int NodeNameMaxLength = 35;


        public string Name { get; protected set; }

        public DateTime UpdateTime { get; internal set; }

        public SensorStatus Status { get; internal set; }

        public NodeViewModel Parent { get; internal set; }

        public string Tooltip =>
            $"{Name}{Environment.NewLine}{(UpdateTime != DateTime.MinValue ? UpdateTime.ToDefaultFormat() : "no data")}";


        public string GetShortName(string name) =>
            name.Length > NodeNameMaxLength ? $"{name[..NodeNameMaxLength]}..." : name;
    }
}
