using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using System.Collections.Generic;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderInfoViewModel : NodeInfoBaseViewModel
    {
        public List<(SensorStatus Status, int Count)> ProductStatuses { get; } = new();

        public int TotalProducts { get; }


        internal FolderInfoViewModel(FolderModel folder) : base(folder)
        {
            ProductStatuses = folder.Products.Values.ToGroupedList(x => x.Status);

            TotalProducts = folder.Products.Count;
        }
    }
}
