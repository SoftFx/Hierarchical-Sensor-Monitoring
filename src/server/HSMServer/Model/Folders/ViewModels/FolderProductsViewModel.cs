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

        public List<SelectListItem> AvailableProducts { get; }

        public List<string> SelectedProducts { get; set; } = new();

        public List<Guid> Products { get; set; } = new();


        public FolderProductsViewModel() { }

        internal FolderProductsViewModel(List<ProductNodeViewModel> availableProducts, List<string> selectedProducts)
        {
            AvailableProducts = availableProducts?.Select(p => new SelectListItem(p.Name, p.Id.ToString())).OrderBy(p => p.Text).ToList();
            SelectedProducts = selectedProducts;
        }


        internal void InitFolderProducts(Dictionary<Guid, ProductNodeViewModel> folderProducts)
        {
            DisplayProducts.AddRange(folderProducts.Values.OrderBy(p => p.Name));
            Products = DisplayProducts.Select(p => p.Id).ToList();
        }

        internal List<ProductNodeViewModel> GetProducts(TreeViewModel.TreeViewModel treeViewModel)
        {
            var folderProducts = new List<ProductNodeViewModel>(1 << 3);

            foreach (var productId in Products.Union(SelectedProducts.Select(Guid.Parse)))
                if (treeViewModel.Nodes.TryGetValue(productId, out var product))
                    folderProducts.Add(product);

            return folderProducts;
        }
    }
}
