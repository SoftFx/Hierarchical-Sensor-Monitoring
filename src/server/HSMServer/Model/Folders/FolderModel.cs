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

            ExpectedUpdateInterval = policies.Count > 0
                ? new TimeIntervalViewModel(policies[0], PredefinedTimeIntervals.ExpectedUpdatePolicy)
                : GetDefaultExpectedUpdatePolicy();
            SensorRestorePolicy = policies.Count > 1
                ? new TimeIntervalViewModel(policies[1], PredefinedTimeIntervals.RestorePolicy)
                : GetDefaultRestorePolicy();
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

            ExpectedUpdateInterval = GetDefaultExpectedUpdatePolicy();
            SensorRestorePolicy = GetDefaultRestorePolicy();
        }


        public void Update(FolderUpdate update)
        {
            Description = update.Description ?? Description;
            Color = update.Color ?? Color;

            if (update.ExpectedUpdateInterval != null)
                ExpectedUpdateInterval = new TimeIntervalViewModel(update.ExpectedUpdateInterval, PredefinedTimeIntervals.ExpectedUpdatePolicy);
            if (update.RestoreInterval != null)
                SensorRestorePolicy = new TimeIntervalViewModel(update.RestoreInterval, PredefinedTimeIntervals.RestorePolicy);
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
            var policies = new List<TimeIntervalEntity>();

            if (ExpectedUpdateInterval != null)
                policies.Add(ExpectedUpdateInterval.ToEntity());
            if (SensorRestorePolicy != null)
                policies.Add(SensorRestorePolicy.ToEntity());

            return policies;
        }

        private static TimeIntervalViewModel GetDefaultExpectedUpdatePolicy() =>
            new(GetDefaultPolicyEntity(), PredefinedTimeIntervals.ExpectedUpdatePolicy);

        private static TimeIntervalViewModel GetDefaultRestorePolicy() =>
            new(GetDefaultPolicyEntity(), PredefinedTimeIntervals.RestorePolicy);

        private static TimeIntervalEntity GetDefaultPolicyEntity() => new((byte)TimeInterval.None, 0L);
    }
}
