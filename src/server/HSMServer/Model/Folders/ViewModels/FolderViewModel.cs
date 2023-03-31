using HSMServer.Extensions;
using HSMServer.Model.ViewModel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace HSMServer.Model.Folders.ViewModels
{
    public sealed class FolderViewModel
    {
        private const string DefaultFolderName = "Other products";
        private const string DefaultFolderDescription = "Products without folder";


        public List<ProductViewModel> Products { get; }

        public Guid? Id { get; }

        public string Description { get; } = DefaultFolderDescription;

        public string Name { get; } = DefaultFolderName;

        public Color BackgroundColor { get; } = Color.White;

        public Color FontColor => BackgroundColor.ToSuitableFont();

        public string Background => BackgroundColor.ToRGBA();

        public string Foreground => FontColor.ToRGB();


        public FolderViewModel(IEnumerable<ProductViewModel> products)
        {
            Products = products.OrderBy(p => p.Name).ToList();
        }

        public FolderViewModel(FolderModel folder, IEnumerable<ProductViewModel> products) : this(products)
        {
            Description = folder.Description;
            BackgroundColor = folder.Color;
            Name = folder.Name;
            Id = folder.Id;
        }
    }
}
