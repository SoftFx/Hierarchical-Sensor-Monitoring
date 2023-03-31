using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderProductsViewModel
    {
        public List<ProductNodeViewModel> DisplayProducts { get; } = new();

        public List<Guid> Products { get; set; } = new();


        public required List<ProductNodeViewModel> AvailableProducts { get; init; }

        public List<SelectListItem> AvailableProductsItems =>
            AvailableProducts?.Select(p => new SelectListItem(p.Name, p.Id.ToString())).OrderBy(p => p.Text).ToList();

        public List<string> SelectedProducts { get; set; } = new();


        public FolderProductsViewModel() { }


        internal void FillFolderProducts(List<ProductNodeViewModel> folderProducts)
        {
            DisplayProducts.AddRange(folderProducts.OrderBy(p => p.Name));
            Products = DisplayProducts.Select(p => p.Id).ToList();
        }

        internal List<ProductNodeViewModel> GetFolderProducts(TreeViewModel.TreeViewModel treeViewModel)
        {
            var folderProducts = new List<ProductNodeViewModel>();

            foreach (var productId in Products.Union(SelectedProducts.Select(Guid.Parse)))
                if (treeViewModel.Nodes.TryGetValue(productId, out var product))
                    folderProducts.Add(product);

            return folderProducts;
        }
    }
}
