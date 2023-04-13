using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace HSMServer.Model.Folders
{
    public class FolderModel : BaseNodeViewModel, IServerModel<FolderEntity, FolderUpdate>
    {
        public Dictionary<Guid, ProductNodeViewModel> Products { get; } = new();

        public Dictionary<User, ProductRoleEnum> UserRoles { get; } = new();

        public DateTime CreationDate { get; }

        public Guid AuthorId { get; }


        public Color Color { get; private set; }

        public string Author { get; set; }


        public FolderModel(FolderEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            Name = entity.DisplayName;
            Description = entity.Description;
            Color = Color.FromArgb(entity.Color);
            AuthorId = Guid.Parse(entity.AuthorId);
            CreationDate = new DateTime(entity.CreationDate);

            var policies = entity.ServerPolicies;

            ExpectedUpdateInterval = GetPolicy(policies, 0, PredefinedIntervals.ForTimeout);
            SensorRestorePolicy = GetPolicy(policies, 1, PredefinedIntervals.ForRestore);
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

            ExpectedUpdateInterval = GetDefaultPolicy(PredefinedIntervals.ForTimeout);
            SensorRestorePolicy = GetDefaultPolicy(PredefinedIntervals.ForRestore);
        }


        public void Update(FolderUpdate update)
        {
            Description = update.Description ?? Description;
            Color = update.Color ?? Color;

            if (update.ExpectedUpdateInterval != null)
                ExpectedUpdateInterval = new TimeIntervalViewModel(update.ExpectedUpdateInterval, PredefinedIntervals.ForTimeout);
            if (update.RestoreInterval != null)
                SensorRestorePolicy = new TimeIntervalViewModel(update.RestoreInterval, PredefinedIntervals.ForRestore);
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
                ServerPolicies = GetPolicyEntities(),
            };


        private List<TimeIntervalEntity> GetPolicyEntities()
        {
            var policies = new List<TimeIntervalEntity>(1 << 1);

            if (ExpectedUpdateInterval != null)
                policies.Add(ExpectedUpdateInterval.ToEntity());
            if (SensorRestorePolicy != null)
                policies.Add(SensorRestorePolicy.ToEntity());

            return policies;
        }

        private static TimeIntervalViewModel GetPolicy(List<TimeIntervalEntity> entities, int index, List<TimeInterval> predefinedIntervals) =>
            entities.Count > index
                ? new TimeIntervalViewModel(entities[index], predefinedIntervals)
                : GetDefaultPolicy(predefinedIntervals);

        private static TimeIntervalViewModel GetDefaultPolicy(List<TimeInterval> predefinedIntervals) =>
            new(GetDefaultPolicyEntity(), predefinedIntervals);

        private static TimeIntervalEntity GetDefaultPolicyEntity() => new((byte)Core.Model.TimeInterval.Custom, 0L);
    }
}
