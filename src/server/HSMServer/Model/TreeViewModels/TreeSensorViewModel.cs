using HSMServer.Extensions;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public sealed class TreeSensorViewModel : TreeNodeViewModel
    {
        public Guid Id { get; private set; }

        public string StateCssClass { get; private set; }


        internal TreeSensorViewModel(string encodedId) : base(encodedId) { }


        internal void Update(SensorNodeViewModel viewModel)
        {
            base.Update(viewModel);

            Id = viewModel.Id;
            StateCssClass = viewModel.State.ToCssClass();
        }
    }
}
