using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace HSMServer.Model.Folders
{
    public class FolderModel : IServerModel<FolderEntity, FolderUpdate>
    {
        public Dictionary<User, ProductRoleEnum> UserRoles { get; } = new();

        public List<ProductNodeViewModel> Products { get; } = new();

        public Guid Id { get; }

        public Guid AuthorId { get; }

        public DateTime CreationDate { get; }


        public string Name { get; }

        public Color Color { get; private set; }

        public string Description { get; private set; }


        public string Author { get; set; }


        public FolderModel(FolderEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            Name = entity.DisplayName;
            Description = entity.Description;
            Color = Color.FromArgb(entity.Color);
            AuthorId = Guid.Parse(entity.AuthorId);
            CreationDate = new DateTime(entity.CreationDate);
        }

        internal FolderModel(FolderAdd addModel)
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;

            Name = addModel.Name;
            Color = addModel.Color;
            Author = addModel.Author;
            AuthorId = addModel.AuthorId;
            Products = addModel.Products;
            Description = addModel.Description;
        }


        public FolderEntity ToEntity() =>
            new()
            {
                Id = Id.ToString(),
                DisplayName = Name,
                AuthorId = AuthorId.ToString(),
                CreationDate = CreationDate.Ticks,
                Description = Description,
                Color = Color.ToArgb(),
            };

        public void Update(FolderUpdate update)
        {
            Description = update.Description ?? Description;
            Color = update.Color ?? Color;
        }
    }
}
