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
        private const string WithoutFolderName = "Other products";
        private const string WithoutFolderDescription = "Products without folder";


        public List<ProductViewModel> Products { get; }

        public Guid? Id { get; }

        public string Description { get; } = WithoutFolderDescription;

        public string Name { get; } = WithoutFolderName;

        public Color BackgroundColor { get; } = Color.White;

        public Color FontColor => BackgroundColor.ToSuitableFont();

        public string Background => BackgroundColor.ToRGBA();

        public string Foreground => FontColor.ToRGB();


        public FolderViewModel(IEnumerable<ProductViewModel> products)
        {
            Products = products?.OrderBy(p => p.Name).ToList() ?? new(1);
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
