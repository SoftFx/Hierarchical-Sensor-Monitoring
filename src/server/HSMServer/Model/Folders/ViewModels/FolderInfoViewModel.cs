using HSMCommon.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Model.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderInfoViewModel : NodeInfoBaseViewModel
    {
        public List<(SensorStatus Status, int Count)> ProductStatuses { get; } = new();

        public int TotalProducts { get; }

        public Guid Id { get; }


        // public constructor without parameters for action Home/UpdateFolderInfo
        public FolderInfoViewModel() : base() { }

        /// <param name="folder">The folder.</param>
        /// <param name="visibleProducts">
        /// The products of the folder the panel is rendered over: the ones its reader sees
        /// (<see cref="UserTreeAccess.GetVisibleProducts"/>), so the statistics say nothing about the others.
        /// </param>
        internal FolderInfoViewModel(FolderModel folder, IEnumerable<ProductNodeViewModel> visibleProducts) : base(folder)
        {
            var products = visibleProducts.ToList();

            ProductStatuses = products.ToGroupedList(x => x.Status);

            TotalProducts = products.Count;
            Id = folder.Id;
        }
    }
}
