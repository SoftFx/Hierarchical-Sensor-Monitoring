using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace HSMServer.Model.Folders
{
    public class EditFolderViewModel
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


        public EditFolderViewModel() { }

        public EditFolderViewModel(List<ProductNodeViewModel> userProducts)
        {
            AllProducts = userProducts.Select(p => new SelectListItem() { Text = p.Name, Value = p.Id.ToString() }).ToList();
        }

        internal EditFolderViewModel(FolderModel folder, List<ProductNodeViewModel> userProducts) : this(userProducts)
        {
            CreationDate = folder.CreationDate.ToDefaultFormat();
            Author = folder.Author;
            Id = folder.Id;
            Name = folder.Name;
            Description = folder.Description;
            Color = folder.Color;
            SelectedProducts = folder.Products;
        }


        internal FolderEntity ToEntity(Guid authorId) =>
            new()
            {
                AuthorId = authorId.ToString(),
                DisplayName = Name,
                Color = Color.ToArgb(),
                Description = Description,
            };
    }
}
