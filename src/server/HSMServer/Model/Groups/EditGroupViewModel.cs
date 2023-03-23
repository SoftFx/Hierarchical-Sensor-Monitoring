using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace HSMServer.Model.Groups
{
    public class EditGroupViewModel
    {
        public List<SelectListItem> AllProducts { get; }

        public List<ProductNodeViewModel> SelectedProducts { get; }

        public string CreationDate { get; }

        public string Author { get; }


        public Guid? Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public Color Color { get; set; }

        public List<string> Products { get; set; } = new();


        public EditGroupViewModel() { }

        public EditGroupViewModel(List<ProductNodeViewModel> userProducts)
        {
            AllProducts = userProducts.Select(p => new SelectListItem() { Text = p.Name, Value = p.Id.ToString() }).ToList();
        }

        internal EditGroupViewModel(GroupModel group, List<ProductNodeViewModel> userProducts) : this(userProducts)
        {
            CreationDate = group.CreationDate.ToDefaultFormat();
            Author = group.Author;
            Id = group.Id;
            Name = group.Name;
            Description = group.Description;
            Color = group.Color;
            SelectedProducts = group.Products.Select(p => new ProductNodeViewModel(p) { RootProduct = null }).ToList(); // TODO selectedProducts should get products from _treeViewModel.Nodes
        }


        internal GroupEntity ToEntity(Guid authorId) =>
            new()
            {
                AuthorId = authorId.ToString(),
                DisplayName = Name,
                Color = Color.ToArgb(),
                Description = Description,
            };
    }
}
