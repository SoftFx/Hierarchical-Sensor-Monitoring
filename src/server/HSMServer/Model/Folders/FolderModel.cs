using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notification.Settings;
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


        public NotificationSettings Notifications { get; private set; } = new();

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
            Notifications = new NotificationSettings(entity.Notifications);

            var policies = entity.ServerPolicies;

            ExpectedUpdateInterval = GetPolicy(policies, 0, PredefinedIntervals.ForTimeout, GetDefaultPolicy);
            SensorRestorePolicy = GetPolicy(policies, 1, PredefinedIntervals.ForRestore, GetDefaultPolicy);
            SavedHistoryPeriod = GetPolicy(policies, 2, PredefinedIntervals.ForCleanup, GetDefaultCleanup);
            SelfDestroyPeriod = GetPolicy(policies, 3, PredefinedIntervals.ForCleanup, GetDefaultCleanup);
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
            SavedHistoryPeriod = GetDefaultCleanup(PredefinedIntervals.ForCleanup);
            SelfDestroyPeriod = GetDefaultCleanup(PredefinedIntervals.ForCleanup);
        }


        public void Update(FolderUpdate update)
        {
            Notifications = update.Notifications ?? Notifications;
            Description = update.Description ?? Description;
            Color = update.Color ?? Color;
            Name = update.Name ?? Name;

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
                Notifications = Notifications.ToEntity(),
            };

        internal FolderModel RecalculateState()
        {
            UpdateTime = Products.Values.MaxOrDefault(x => x.UpdateTime);

            return this;
        }


        private List<TimeIntervalEntity> GetPolicyEntities()
        {
            var policies = new List<TimeIntervalEntity>(1 << 1);

            if (ExpectedUpdateInterval != null)
                policies.Add(ExpectedUpdateInterval.ToEntity());
            if (SensorRestorePolicy != null)
                policies.Add(SensorRestorePolicy.ToEntity());
            if (SavedHistoryPeriod != null)
                policies.Add(SavedHistoryPeriod.ToEntity());
            if (SelfDestroyPeriod != null)
                policies.Add(SelfDestroyPeriod.ToEntity());

            return policies;
        }

        private static TimeIntervalViewModel GetPolicy(List<TimeIntervalEntity> entities, int index,
            List<TimeInterval> predefinedIntervals, Func<List<TimeInterval>, TimeIntervalViewModel> getDefault) =>
            entities.Count > index
                ? new TimeIntervalViewModel(entities[index], predefinedIntervals)
                : getDefault(predefinedIntervals);

        private TimeIntervalViewModel GetDefaultPolicy(List<TimeInterval> predefinedIntervals) =>
            new(GetDefaultPolicyEntity(), predefinedIntervals);

        private TimeIntervalViewModel GetDefaultCleanup(List<TimeInterval> predefinedIntervals) =>
            new(GetDefaultCleanupEntity(), predefinedIntervals);

        private static TimeIntervalEntity GetDefaultPolicyEntity() => new((byte)Core.Model.TimeInterval.Custom, 0L);

        private static TimeIntervalEntity GetDefaultCleanupEntity() => new((byte)Core.Model.TimeInterval.Month, 0L);
    }
}
