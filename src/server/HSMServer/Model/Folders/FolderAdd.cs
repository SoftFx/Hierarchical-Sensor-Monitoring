using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace HSMServer.Model.Folders
{
    public record FolderAdd
    {
        public Dictionary<Guid, ProductNodeViewModel> Products { get; init; }

        public string Name { get; init; }

        public Color Color { get; init; }

        public Guid AuthorId { get; init; }

        public string Author { get; init; }

        public string Description { get; init; }
    }
}
