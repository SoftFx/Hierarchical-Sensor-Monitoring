using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Extensions;
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

            if (entity.Settings.Count == 0) //TODO: Remove after migrations
            {
                for (int i = 0; i < entity.ServerPolicies.Count; ++i)
                {
                    var oldInterval = entity.ServerPolicies[i];
                    var newEntity = Core.Migrators.ToNewInterval(oldInterval).ToEntity();

                    var name = i switch
                    {
                        0 => nameof(TimeToLive),
                        2 => nameof(KeepHistory),
                        3 => nameof(SelfDestroy),
                        _ => null,
                    };

                    if (name != null)
                        entity.Settings.Add(name, newEntity);
                }
            }

            KeepHistory = LoadKeepHistory(entity.Settings.GetValueOrDefault(nameof(KeepHistory)));
            SelfDestroy = LoadSelfDestroy(entity.Settings.GetValueOrDefault(nameof(SelfDestroy)));
            TimeToLive = LoadTTL(entity.Settings.GetValueOrDefault(nameof(TimeToLive)));
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

            KeepHistory = LoadKeepHistory();
            SelfDestroy = LoadSelfDestroy();
            TimeToLive = LoadTTL();
        }


        public void Update(FolderUpdate update)
        {
            Notifications = update.Notifications ?? Notifications;
            Description = update.Description ?? Description;
            Color = update.Color ?? Color;
            Name = update.Name ?? Name;

            if (update.TTL != null)
                TimeToLive = new TimeIntervalViewModel(update.TTL, PredefinedIntervals.ForTimeout);

            if (update.KeepHistory != null)
                KeepHistory = new TimeIntervalViewModel(update.KeepHistory, PredefinedIntervals.ForKeepHistory);

            if (update.SelfDestroy != null)
                SelfDestroy = new TimeIntervalViewModel(update.SelfDestroy, PredefinedIntervals.ForSelfDestory);
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
                Notifications = Notifications.ToEntity(),
                Settings = new Dictionary<string, TimeIntervalEntity>
                {
                    [nameof(TimeToLive)] = TimeToLive.ToEntity(),
                    [nameof(KeepHistory)] = KeepHistory.ToEntity(),
                    [nameof(SelfDestroy)] = SelfDestroy.ToEntity(),
                }
            };

        internal FolderModel RecalculateState()
        {
            UpdateTime = Products.Values.MaxOrDefault(x => x.UpdateTime);

            RecalculateAlerts(Products.Values);

            return this;
        }


        private static TimeIntervalViewModel LoadTTL(TimeIntervalEntity entity = null) => LoadSetting(entity, PredefinedIntervals.ForTimeout, Core.Model.TimeInterval.None);

        private static TimeIntervalViewModel LoadKeepHistory(TimeIntervalEntity entity = null) => LoadSetting(entity, PredefinedIntervals.ForKeepHistory, Core.Model.TimeInterval.Month);

        private static TimeIntervalViewModel LoadSelfDestroy(TimeIntervalEntity entity = null) => LoadSetting(entity, PredefinedIntervals.ForSelfDestory, Core.Model.TimeInterval.Month);


        private static TimeIntervalViewModel LoadSetting(TimeIntervalEntity entity, List<TimeInterval> predefinedIntervals, Core.Model.TimeInterval defaultInterval)
        {
            entity ??= new TimeIntervalEntity((long)defaultInterval, 0L);

            return new TimeIntervalViewModel(entity, predefinedIntervals);
        }
    }
}
