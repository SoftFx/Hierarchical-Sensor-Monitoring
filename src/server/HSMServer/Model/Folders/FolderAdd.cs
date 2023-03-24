using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace HSMServer.Model.Folders
{
    internal record FolderAdd
    {
        internal List<ProductNodeViewModel> Products { get; init; }

        internal string Name { get; init; }

        internal Guid AuthorId { get; init; }

        internal string Description { get; init; }

        internal Color Color { get; init; }

        internal string Author { get; init; }
    }
}
