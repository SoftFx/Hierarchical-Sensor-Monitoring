using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderInfoViewModel : NodeInfoBaseViewModel
    {
        public List<(SensorStatus Status, int Count)> ProductStatuses { get; } = new();

        public int TotalProducts { get; }

        public Guid Id { get; }



        // public constructor without parameters for action Home/UpdateFolderInfo
        public FolderInfoViewModel() : base() { }

        internal FolderInfoViewModel(FolderModel folder) : base(folder)
        {
            ProductStatuses = folder.Products.Values.ToGroupedList(x => x.Status);

            TotalProducts = folder.Products.Count;
            Id = folder.Id;
        }
    }
}
