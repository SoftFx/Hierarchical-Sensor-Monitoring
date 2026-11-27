using HSMCommon.Extensions;
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
        public Dictionary<Guid, ProductNodeViewModel> Products { get; } = [];

        public Dictionary<User, ProductRoleEnum> UserRoles { get; } = [];

        public DateTime CreationDate { get; }

        public Guid AuthorId { get; }


        public HashSet<Guid> Chats { get; private set; } = [];

        public Color Color { get; private set; }

        public string Author { get; set; }


        public event Action<JournalRecordModel> ChangesHandler;

        public event Func<Guid, string> GetChatName;

        internal Func<IEnumerable<Guid>> GetGlobalChatIds;


        public FolderModel(FolderEntity entity)
        {
            Id = Guid.Parse(entity.Id);
            Name = entity.DisplayName;
            Description = entity.Description;
            Color = Color.FromArgb(entity.Color);
            AuthorId = Guid.Parse(entity.AuthorId);
            CreationDate = new DateTime(entity.CreationDate);

            DefaultChats = LoadDefaultChats(entity.DefaultChatsSettings);

            // A stored folder without a period keeps reading it as the month it
            // always read: the new default is for new folders only.
            KeepHistory = LoadKeepHistory(entity.Settings.GetValueOrDefault(nameof(KeepHistory)), StoredWithoutPeriod);
            SelfDestroy = LoadSelfDestroy(entity.Settings.GetValueOrDefault(nameof(SelfDestroy)), StoredWithoutPeriod);
            TTL = LoadTTL(entity.Settings.GetValueOrDefault(nameof(TTL)));

            Chats = LoadChats(entity);
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

            KeepHistory = LoadKeepHistory(null, Core.Cache.TreeValuesCache.DefaultRetention);
            SelfDestroy = LoadSelfDestroy(null, Core.Cache.TreeValuesCache.DefaultRetention);
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

            var chatName = update.ChatNameResolver ?? GetChatName;

            if (update.Chats is not null)
                Chats = UpdateChats(Chats, update.Chats, update.Initiator, chatName);

            if (update.DefaultChats != null)
                DefaultChats = UpdateSetting(DefaultChats, update.DefaultChats, update.Initiator, chatName);
        }

        private TimeIntervalViewModel UpdateSetting(TimeIntervalViewModel currentValue, TimeIntervalViewModel newValue, InitiatorInfo initiator, [CallerArgumentExpression(nameof(currentValue))] string propName = "", NoneValues none = NoneValues.Never)
        {
            // A setting stored as FromParent is shown as the item that never expires; posted back unchanged, it stays as
            // stored: no change, no journal record (#1945).
            if (currentValue.KeepsStoredFromParent(newValue))
                return currentValue;

            var oldModel = currentValue.ToModel(currentValue);
            var newModel = newValue.ToModel(newValue);

            if (newModel is not null && oldModel.ToString() != newModel.ToString())
            {
                ChangesHandler?.Invoke(new JournalRecordModel(Id, initiator)
                {
                    Enviroment = "Folder settings update",
                    // A folder has no parent: FromParent on it never expires, and its form showed it as such.
                    OldValue = oldModel.IsNone || oldModel.IsFromParent ? $"{none}" : $"{oldModel}",
                    NewValue = newModel.IsNone ? $"{none}" : $"{newModel}",

                    PropertyName = propName,
                    Path = Name,
                    Kind = HSMServer.Core.Journal.JournalKinds.Folder,
                });
            }

            return newValue;
        }

        private DefaultChatViewModel UpdateSetting(DefaultChatViewModel currentValue, DefaultChatViewModel newValue, InitiatorInfo initiator,
                                                   Func<Guid, string> chatName)
        {
            // The chats a mode holds are named, so the record tells which chat came or left —
            // in From parent too, whose selected chats add to the inherited ones.
            string GetJournalValue((HashSet<Guid> ids, DefaultChatMode mode) value) => value.mode switch
            {
                DefaultChatMode.Custom => NamesOf(value.ids),
                DefaultChatMode.FromParent when value.ids.Count != 0 => $"{value.mode.GetDisplayName()}, {NamesOf(value.ids)}",
                _ => value.mode.GetDisplayName(),
            };

            string NamesOf(HashSet<Guid> ids) => string.Join(", ", ids.Select(chatName));

            var oldChat = (currentValue.SelectedChats, currentValue.ChatMode);
            var newChat = (newValue.SelectedChats, newValue.ChatMode);

            if (oldChat != newChat)
            {
                ChangesHandler?.Invoke(new JournalRecordModel(Id, initiator)
                {
                    Enviroment = "Folder settings update",
                    OldValue = GetJournalValue(oldChat),
                    NewValue = GetJournalValue(newChat),

                    PropertyName = "Default chats",
                    Path = Name,
                    Kind = HSMServer.Core.Journal.JournalKinds.Folder,
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
                    Kind = HSMServer.Core.Journal.JournalKinds.Folder,
                });

            return newValue ?? oldValue;
        }

        private HashSet<Guid> UpdateChats(HashSet<Guid> oldValue, HashSet<Guid> newValue, InitiatorInfo initiator, Func<Guid, string> nameResolver, [CallerArgumentExpression(nameof(oldValue))] string propName = "")
        {
            List<string> GetFilteredValues(HashSet<Guid> hash) => [.. hash.Select(nameResolver).OrderBy(n => n)];

            var oldChats = GetFilteredValues(oldValue);
            var newChats = GetFilteredValues(newValue);

            if (newValue is not null && !newChats.SequenceEqual(oldChats))
            {
                ChangesHandler?.Invoke(new JournalRecordModel(Id, initiator)
                {
                    Enviroment = "Folder chats update",
                    OldValue = string.Join(", ", oldChats),
                    NewValue = string.Join(", ", newChats),

                    PropertyName = propName,
                    Path = Name,
                    Kind = HSMServer.Core.Journal.JournalKinds.Folder,
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
                Chats = Chats.Select(c => c.ToByteArray()).ToList(),

                DefaultChatsSettings = DefaultChats.ToEntity(GetAvailableChats()),

                Settings = new Dictionary<string, TimeIntervalEntity>
                {
                    [nameof(TTL)] = TTL.ToEntity(),
                    [nameof(KeepHistory)] = KeepHistory.ToEntity(),
                    [nameof(SelfDestroy)] = SelfDestroy.ToEntity(),
                }
            };

        internal Dictionary<Guid, string> GetAvailableChats()
        {
            var result = Chats.ToDictionary(k => k, v => GetChatName?.Invoke(v));

            if (GetGlobalChatIds is not null)
                foreach (var id in GetGlobalChatIds())
                    if (!result.ContainsKey(id))
                        result[id] = GetChatName?.Invoke(id);

            return result;
        }


        internal FolderModel RecalculateState()
        {
            UpdateTime = Products.Values.MaxOrDefault(x => x.UpdateTime);

            RecalculateAlerts(Products.Values);

            return this;
        }


        private static DefaultChatViewModel LoadDefaultChats(PolicyDestinationSettingsEntity entity = null)
        {
            var model = new PolicyDestinationSettings(entity ?? new PolicyDestinationSettingsEntity());

            return new DefaultChatViewModel().FromModel(model);
        }

        private static HashSet<Guid> LoadChats(FolderEntity entity)
        {
            var chats = new HashSet<Guid>();

            if (entity.Chats is not null)
                foreach (var id in entity.Chats)
                    chats.Add(new Guid(id));

            if (chats.Count == 0)
            {
                if (entity.LegacyTelegramChats is not null)
                    foreach (var id in entity.LegacyTelegramChats)
                        chats.Add(new Guid(id));

                if (entity.LegacySlackDestinations is not null)
                    foreach (var id in entity.LegacySlackDestinations)
                        chats.Add(new Guid(id));
            }

            return chats;
        }


        private static TimeIntervalViewModel LoadTTL(TimeIntervalEntity entity = null) => LoadSetting(entity, PredefinedIntervals.ForFolderTimeout, Core.Model.TimeInterval.None);

        private const Core.Model.TimeInterval StoredWithoutPeriod = Core.Model.TimeInterval.Month;

        private static TimeIntervalViewModel LoadKeepHistory(TimeIntervalEntity entity, Core.Model.TimeInterval defaultInterval) => LoadSetting(entity, PredefinedIntervals.ForKeepHistory, defaultInterval);

        private static TimeIntervalViewModel LoadSelfDestroy(TimeIntervalEntity entity, Core.Model.TimeInterval defaultInterval) => LoadSetting(entity, PredefinedIntervals.ForSelfDestory, defaultInterval);


        private static TimeIntervalViewModel LoadSetting(TimeIntervalEntity entity, HashSet<TimeInterval> predefinedIntervals, Core.Model.TimeInterval defaultInterval)
        {
            entity ??= new TimeIntervalEntity((long)defaultInterval, 0L);

            return new TimeIntervalViewModel(entity, predefinedIntervals);
        }


        public void Dispose() { }
    }
}
