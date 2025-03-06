using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;
using System;

namespace HSMServer.Model.DataAlerts
{
    public sealed class FileDataAlertViewModel : DataAlertViewModel<FileValue>
    {
        public override SensorType Type => SensorType.Boolean;

        public FileDataAlertViewModel() : base() { }

        public FileDataAlertViewModel(NodeViewModel node) : base(node) { }

        public FileDataAlertViewModel(Policy<FileValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }


        protected override ConditionViewModel CreateCondition(bool isMain) => new FileConditionViewModel(isMain);
    }
}
