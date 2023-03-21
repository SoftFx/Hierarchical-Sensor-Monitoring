using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace HSMServer.Model.Groups
{
    public class GroupModel : IServerModel<GroupEntity, GroupUpdate>
    {
        public Guid Id { get; init; }

        public string Name { get; init; }

        public Guid AuthorId { get; init; }

        public DateTime CreationDate { get; init; }

        public string Description { get; init; }

        public Color Color { get; init; }

        public List<Guid> ProductIds { get; init; }

        public Dictionary<Guid, ProductRoleEnum> UserRoles { get; init; }


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
            ProductIds = entity.ProductIds?.Select(Guid.Parse).ToList();
            UserRoles = entity.UserRoles?.ToDictionary(r => Guid.Parse(r.Key), r => (ProductRoleEnum)r.Value);
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
                ProductIds = ProductIds?.Select(p => p.ToString())?.ToList(),
                UserRoles = UserRoles?.ToDictionary(r => r.Key.ToString(), r => (byte)r.Value),
            };

        public void Update(GroupUpdate update)
        {
            throw new NotImplementedException();
        }
    }
}
