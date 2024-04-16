using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.Controls;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;

namespace HSMServer.Model.Folders
{
    public class FolderModel : BaseNodeViewModel, IServerModel<FolderEntity, FolderUpdate>, IChangesEntity
    {
        public Dictionary<Guid, ProductNodeViewModel> Products { get; } = new();

        public Dictionary<User, ProductRoleEnum> UserRoles { get; } = new();

        public DateTime CreationDate { get; }

        public Guid AuthorId { get; }


        public HashSet<Guid> TelegramChats { get; private set; } = new();

        public Color Color { get; private set; }

        public string Author { get; set; }


        public event Action<JournalRecordModel> ChangesHandler;

        public event Func<Guid, string> GetChatName;


        public FolderModel(FolderEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            Name = entity.DisplayName;
            Description = entity.Description;
            Color = Color.FromArgb(entity.Color);
            AuthorId = Guid.Parse(entity.AuthorId);
            CreationDate = new DateTime(entity.CreationDate);

            DefaultChats = LoadDefaultChats(entity.DefaultChatsSettings);

            KeepHistory = LoadKeepHistory(entity.Settings.GetValueOrDefault(nameof(KeepHistory)));
            SelfDestroy = LoadSelfDestroy(entity.Settings.GetValueOrDefault(nameof(SelfDestroy)));
            TTL = LoadTTL(entity.Settings.GetValueOrDefault(nameof(TTL)));

            if (entity.TelegramChats is not null)
                TelegramChats = new HashSet<Guid>(entity.TelegramChats.Select(c => new Guid(c)));
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

            DefaultChats = LoadDefaultChats();

            KeepHistory = LoadKeepHistory();
            SelfDestroy = LoadSelfDestroy();
            TTL = LoadTTL();
        }


        public void Update(FolderUpdate update)
        {
            Description = UpdateProperty(Description, update.Description, update.Initiator);
            Color = update.Color ?? Color;
            Name = update.Name ?? Name;

            if (update.TTL != null)
                TTL = UpdateSetting(TTL, new TimeIntervalViewModel(update.TTL, PredefinedIntervals.ForFolderTimeout), update.Initiator);

            if (update.KeepHistory != null)
                KeepHistory = UpdateSetting(KeepHistory, new TimeIntervalViewModel(update.KeepHistory, PredefinedIntervals.ForKeepHistory), update.Initiator, "Keep sensor history", NoneValues.Forever);

            if (update.SelfDestroy != null)
                SelfDestroy = UpdateSetting(SelfDestroy, new TimeIntervalViewModel(update.SelfDestroy, PredefinedIntervals.ForSelfDestory), update.Initiator, "Remove sensor after inactivity");

            if (update.TelegramChats is not null)
                TelegramChats = UpdateChats(TelegramChats, update.TelegramChats, update.Initiator);

            if (update.DefaultChats != null)
                DefaultChats = UpdateSetting(DefaultChats, update.DefaultChats, update.Initiator);
        }

        private TimeIntervalViewModel UpdateSetting(TimeIntervalViewModel currentValue, TimeIntervalViewModel newValue, InitiatorInfo initiator, [CallerArgumentExpression(nameof(currentValue))] string propName = "", NoneValues none = NoneValues.Never)
        {
            var oldModel = currentValue.ToModel(currentValue);
            var newModel = newValue.ToModel(newValue);

            if (newModel is not null && oldModel.ToString() != newModel.ToString())
            {
                ChangesHandler?.Invoke(new JournalRecordModel(Id, initiator)
                {
                    Enviroment = "Folder settings update",
                    OldValue = oldModel.IsNone ? $"{none}" : $"{oldModel}",
                    NewValue = newModel.IsNone ? $"{none}" : $"{newModel}",

                    PropertyName = propName,
                    Path = Name,
                });
            }

            return newValue;
        }

        private DefaultChatViewModel UpdateSetting(DefaultChatViewModel currentValue, DefaultChatViewModel newValue, InitiatorInfo initiator)
        {
            const string PropertyName = "Default telegram chat";

            var oldChat = currentValue.SelectedChat;
            var newChat = newValue.SelectedChat;

            if (oldChat != newChat)
            {
                ChangesHandler?.Invoke(new JournalRecordModel(Id, initiator)
                {
                    Enviroment = "Folder settings update",
                    OldValue = oldChat.HasValue && oldChat != currentValue.NotInitializedId ? GetChatName(oldChat.Value) : DefaultChatViewModel.NotInitialized,
                    NewValue = newChat.HasValue && newChat != currentValue.NotInitializedId ? GetChatName(newChat.Value) : DefaultChatViewModel.NotInitialized,

                    PropertyName = PropertyName,
                    Path = Name,
                });
            }

            return newValue;
        }

        private T UpdateProperty<T>(T oldValue, T newValue, InitiatorInfo initiator, [CallerArgumentExpression(nameof(oldValue))] string propName = "")
        {
            if (newValue is not null && !newValue.Equals(oldValue ?? newValue))
                ChangesHandler?.Invoke(new JournalRecordModel(Id, initiator)
                {
                    Enviroment = "Folder general info update",
                    OldValue = $"{oldValue}",
                    NewValue = $"{newValue}",

                    PropertyName = propName,
                    Path = Name,
                });

            return newValue ?? oldValue;
        }

        private HashSet<Guid> UpdateChats(HashSet<Guid> oldValue, HashSet<Guid> newValue, InitiatorInfo initiator, [CallerArgumentExpression(nameof(oldValue))] string propName = "")
        {
            var oldChats = oldValue.Select(id => GetChatName(id)).OrderBy(n => n).ToList();
            var newChats = newValue.Select(id => GetChatName(id)).OrderBy(n => n).ToList();

            if (newValue is not null && !newChats.SequenceEqual(oldChats))
            {
                ChangesHandler?.Invoke(new JournalRecordModel(Id, initiator)
                {
                    Enviroment = "Folder chats update",
                    OldValue = string.Join(", ", oldChats),
                    NewValue = string.Join(", ", newChats),

                    PropertyName = propName,
                    Path = Name,
                });
            }

            return newValue;
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
                TelegramChats = TelegramChats.Select(c => c.ToByteArray()).ToList(),

                DefaultChatsSettings = DefaultChats.ToEntity(TelegramChats.ToDictionary(k => k, v => GetChatName(v))),
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


        private static DefaultChatViewModel LoadDefaultChats(PolicyDestinationSettingsEntity entity = null)
        {
            var model = entity is null ? new PolicyDestinationSettings() : new PolicyDestinationSettings(entity);

            return new DefaultChatViewModel().FromModel(model);
        }


        private static TimeIntervalViewModel LoadTTL(TimeIntervalEntity entity = null) => LoadSetting(entity, PredefinedIntervals.ForFolderTimeout, Core.Model.TimeInterval.None);

        private static TimeIntervalViewModel LoadKeepHistory(TimeIntervalEntity entity = null) => LoadSetting(entity, PredefinedIntervals.ForKeepHistory, Core.Model.TimeInterval.Month);

        private static TimeIntervalViewModel LoadSelfDestroy(TimeIntervalEntity entity = null) => LoadSetting(entity, PredefinedIntervals.ForSelfDestory, Core.Model.TimeInterval.Month);


        private static TimeIntervalViewModel LoadSetting(TimeIntervalEntity entity, HashSet<TimeInterval> predefinedIntervals, Core.Model.TimeInterval defaultInterval)
        {
            entity ??= new TimeIntervalEntity((long)defaultInterval, 0L);

            return new TimeIntervalViewModel(entity, predefinedIntervals);
        }


        public void Dispose() { }
    }
}