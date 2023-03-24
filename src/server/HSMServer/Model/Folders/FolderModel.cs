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
        public Dictionary<Guid, ProductRoleEnum> UserRoles { get; } = new();

        public List<ProductNodeViewModel> Products { get; } = new();

        public Guid Id { get; }

        public string Name { get; }

        public Guid AuthorId { get; }

        public DateTime CreationDate { get; }

        public string Description { get; private set; }

        public Color Color { get; private set; }


        public string Author { get; set; }


        public FolderModel()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;
        }

        public FolderModel(FolderEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = Guid.Parse(entity.AuthorId);
            Name = entity.DisplayName;
            Description = entity.Description;
            CreationDate = new DateTime(entity.CreationDate);
            Color = Color.FromArgb(entity.Color);
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
            throw new NotImplementedException();
        }
    }
}
