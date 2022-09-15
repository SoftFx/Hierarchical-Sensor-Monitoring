using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public abstract class NodeViewModel
    {
        private const int NodeNameMaxLength = 35;


        public string Name { get; protected set; }

        public DateTime UpdateTime { get; protected set; }

        public SensorStatus Status { get; protected set; }

        public NodeViewModel Parent { get; internal set; }

        public string Path { get; internal set; }


        public string Tooltip =>
            $"{Name}{Environment.NewLine}{(UpdateTime != DateTime.MinValue ? UpdateTime.ToDefaultFormat() : "no data")}";

        public string Title => Name?.Replace('\\', ' ') ?? string.Empty;


        public string GetShortName(string name) =>
            name.Length > NodeNameMaxLength ? $"{name[..NodeNameMaxLength]}..." : name;
    }
}
