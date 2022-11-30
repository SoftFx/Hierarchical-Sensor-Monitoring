using HSMServer.Extensions;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public abstract class TreeNodeViewModel
    {
        public string EncodedId { get; }

        public string Name { get; protected set; }

        public string UpdateTime { get; protected set; }

        public string Status { get; protected set; }


        public string Tooltip =>
            $"{Name}{Environment.NewLine}{(string.IsNullOrEmpty(UpdateTime) ? "no data" : UpdateTime)}";

        public string Title => Name?.Replace('\\', ' ') ?? string.Empty;


        internal TreeNodeViewModel(string encodedId)
        {
            EncodedId = encodedId;
        }


        internal void Update(NodeViewModel viewModel)
        {
            Name = viewModel.Name;
            UpdateTime = viewModel.UpdateTime.ToDefaultFormat();
            Status = viewModel.Status.ToIcon();
        }
    }
}
