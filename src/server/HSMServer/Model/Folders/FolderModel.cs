﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notification.Settings;
using System;
using System.Collections.Generic;
using System.Drawing;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;

namespace HSMServer.Model.Folders
{
    public class FolderModel : BaseNodeViewModel, IServerModel<FolderEntity, FolderUpdate>, IChangesEntity
    {
        public Dictionary<Guid, ProductNodeViewModel> Products { get; } = new();

        public Dictionary<User, ProductRoleEnum> UserRoles { get; } = new();

        public DateTime CreationDate { get; }

        public Guid AuthorId { get; }


        public NotificationSettings Notifications { get; private set; } = new();

        public Color Color { get; private set; }

        public string Author { get; set; }


        public event Action<JournalRecordModel> ChangesHandler;


        public FolderModel(FolderEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            Name = entity.DisplayName;
            Description = entity.Description;
            Color = Color.FromArgb(entity.Color);
            AuthorId = Guid.Parse(entity.AuthorId);
            CreationDate = new DateTime(entity.CreationDate);
            Notifications = new NotificationSettings(entity.Notifications);

            KeepHistory = LoadKeepHistory(entity.Settings.GetValueOrDefault(nameof(KeepHistory)));
            SelfDestroy = LoadSelfDestroy(entity.Settings.GetValueOrDefault(nameof(SelfDestroy)));
            TTL = LoadTTL(entity.Settings.GetValueOrDefault(nameof(TTL)));
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
            TTL = LoadTTL();
        }


        public void Update(FolderUpdate update)
        {
            Notifications = update.Notifications ?? Notifications;
            Description = update.Description ?? Description;
            Color = update.Color ?? Color;
            Name = update.Name ?? Name;

            if (update.TTL != null)
                TTL = new TimeIntervalViewModel(update.TTL, PredefinedIntervals.ForFolderTimeout);

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
                    [nameof(TTL)] = TTL.ToEntity(),
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


        private static TimeIntervalViewModel LoadTTL(TimeIntervalEntity entity = null) => LoadSetting(entity, PredefinedIntervals.ForFolderTimeout, Core.Model.TimeInterval.None);

        private static TimeIntervalViewModel LoadKeepHistory(TimeIntervalEntity entity = null) => LoadSetting(entity, PredefinedIntervals.ForKeepHistory, Core.Model.TimeInterval.Month);

        private static TimeIntervalViewModel LoadSelfDestroy(TimeIntervalEntity entity = null) => LoadSetting(entity, PredefinedIntervals.ForSelfDestory, Core.Model.TimeInterval.Month);


        private static TimeIntervalViewModel LoadSetting(TimeIntervalEntity entity, HashSet<TimeInterval> predefinedIntervals, Core.Model.TimeInterval defaultInterval)
        {
            entity ??= new TimeIntervalEntity((long)defaultInterval, 0L);

            return new TimeIntervalViewModel(entity, predefinedIntervals);
        }
    }
}
