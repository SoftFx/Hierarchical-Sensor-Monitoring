using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using System;
using System.Drawing;

namespace HSMServer.Model.Folders.ViewModels
{
    public class EditFolderViewModel
    {
        public FolderProductsViewModel Products { get; set; }

        public string CreationDate { get; }

        public string Author { get; }


        public Guid? Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public Color Color { get; set; }


        public EditFolderViewModel() { }

        public EditFolderViewModel(FolderProductsViewModel products)
        {
            Products = products;
        }

        internal EditFolderViewModel(FolderModel folder, FolderProductsViewModel products) : this(products)
        {
            CreationDate = folder.CreationDate.ToDefaultFormat();
            Author = folder.Author;
            Id = folder.Id;
            Name = folder.Name;
            Description = folder.Description;
            Color = folder.Color;
            Products.Products.AddRange(folder.Products);
        }


        internal FolderAdd ToFolderAdd(User author, TreeViewModel.TreeViewModel treeViewModel) =>
            new()
            {
                Name = Name,
                Color = Color,
                Description = Description,
                AuthorId = author.Id,
                Author = author.Name,
                Products = Products?.GetAddedProducts(treeViewModel) ?? new(),
            };
    }
}
