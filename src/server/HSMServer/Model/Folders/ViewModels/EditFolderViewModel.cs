using HSMServer.Attributes;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using System;
using System.ComponentModel.DataAnnotations;
using System.Drawing;

namespace HSMServer.Model.Folders.ViewModels
{
    public class EditFolderViewModel
    {
        public string CreationDate { get; }

        public string Author { get; }


        public FolderProductsViewModel Products { get; set; }

        public Guid? Id { get; set; }

        [Required(ErrorMessage = "{0} is required.")]
        [StringLength(10, ErrorMessage = "{0} length should be less than {1}.")]
        [UniqueFolderValidation(ErrorMessage = "Folder name must be unique.")]
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
