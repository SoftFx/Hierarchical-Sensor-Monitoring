using HSMCommon.Extensions;
using HSMServer.Model.Authentication;
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
        /// <param name="user">
        /// The user the panel is rendered for: the product statistics count only the products this user has
        /// a role on (an admin: all of them), so the panel says nothing about the others.
        /// </param>
        internal FolderInfoViewModel(FolderModel folder, User user) : base(folder)
        {
            var products = folder.Products.Values
                .Where(p => user is not null && (user.IsAdmin || (user.ProductsRoles is not null && user.IsProductAvailable(p.Id))))
                .ToList();

            ProductStatuses = products.ToGroupedList(x => x.Status);

            TotalProducts = products.Count;
            Id = folder.Id;
        }
    }
}
