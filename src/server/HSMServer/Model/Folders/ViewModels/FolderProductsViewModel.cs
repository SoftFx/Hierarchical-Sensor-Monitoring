using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.Folders.ViewModels
{
    public class FolderProductsViewModel
    {
        public List<ProductNodeViewModel> Products { get; } = new();


        public required List<ProductNodeViewModel> AvailableProducts { get; init; }

        public List<SelectListItem> AvailableProductsItems =>
            AvailableProducts?.Select(p => new SelectListItem() { Text = p.Name, Value = p.Id.ToString() }).ToList();

        public List<string> SelectedProducts { get; set; } = new();


        public FolderProductsViewModel() { }


        internal List<ProductNodeViewModel> GetAddedProducts(TreeViewModel.TreeViewModel treeViewModel)
        {
            var selectedProducts = new List<ProductNodeViewModel>();

            foreach (var productId in SelectedProducts)
                if (Guid.TryParse(productId, out var id) && treeViewModel.Nodes.TryGetValue(id, out var product))
                    selectedProducts.Add(product);

            return selectedProducts;
        }
    }
}
