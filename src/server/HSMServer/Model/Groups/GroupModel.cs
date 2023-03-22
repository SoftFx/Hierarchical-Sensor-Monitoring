using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace HSMServer.Model.Groups
{
    public class GroupModel : IServerModel<GroupEntity, GroupUpdate>
    {
        public Dictionary<Guid, ProductRoleEnum> UserRoles { get; } = new();

        public List<ProductModel> Products { get; } = new();

        public Guid Id { get; }

        public string Name { get; }

        public Guid AuthorId { get; }

        public DateTime CreationDate { get; }

        public string Description { get; private set; }

        public Color Color { get; private set; }


        public string Author { get; set; }


        public GroupModel()
        {
            Id = Guid.NewGuid();
            CreationDate = DateTime.UtcNow;
        }

        public GroupModel(GroupEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            AuthorId = Guid.Parse(entity.AuthorId);
            Name = entity.DisplayName;
            Description = entity.Description;
            CreationDate = new DateTime(entity.CreationDate);
            Color = Color.FromArgb(entity.Color);
        }


        public GroupEntity ToEntity() =>
            new()
            {
                Id = Id.ToString(),
                DisplayName = Name,
                AuthorId = AuthorId.ToString(),
                CreationDate = CreationDate.Ticks,
                Description = Description,
                Color = Color.ToArgb(),
            };

        public void Update(GroupUpdate update)
        {
            throw new NotImplementedException();
        }
    }
}
