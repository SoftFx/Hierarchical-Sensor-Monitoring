using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using System;
using System.Text;

namespace HSMServer.Model.MultiToastViewModels
{
    public sealed class MultiActionsToastViewModel
    {
        private readonly LimitedQueue<string> _folders = new(5);
        private readonly LimitedQueue<string> _products = new(5);
        private readonly LimitedQueue<string> _nodes = new(5);
        private readonly LimitedQueue<string> _sensors = new(10);


        private readonly StringBuilder _errorBuilder = new(1 << 5);

        private readonly StringBuilder _responseBuilder = new(1 << 5);


        public string ErrorMessage => _errorBuilder.ToString();

        public string ResponseInfo => _responseBuilder.ToString();


        public void AddItem(NodeViewModel item)
        {
            if (item.RootProduct.Id == item.Id)
            {
                _products.Enqueue((item as ProductNodeViewModel)?.Name);
                return;
            }

            if (item is SensorNodeViewModel sensorNodeViewModel)
            {
                _sensors.Enqueue(sensorNodeViewModel.FullPath);
                return;
            }

            _nodes.Enqueue((item as ProductNodeViewModel)?.FullPath);
        }

        public void AddItem(FolderModel folder) => _folders.Enqueue(folder?.Name);


        public MultiActionsToastViewModel BuildResponse(string header)
        {
            _folders.ToBuilder(_responseBuilder, $"{header} folders:");
            _products.ToBuilder(_responseBuilder, $"{header} products:");
            _nodes.ToBuilder(_responseBuilder, $"{header} nodes:", Environment.NewLine);
            _sensors.ToBuilder(_responseBuilder, $"{header} sensors:", Environment.NewLine);

            return this;
        }

        public void AddError(string errorMessage) => _errorBuilder.AppendLine(errorMessage);

        public void AddRemoveFolderError(string name) => AddError($"Folder {name} cannot be deleted");

        public void AddCantChangeIntervalError(string name, string type, string policy, TimeInterval interval) => AddError($"{type} {name} can't have {policy} {interval} interval");

        public void AddRoleError(string name, string action) => AddError($"You should be Manager or Admin to {action} {name}");
    }
}
