using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace HSMServer.Model.Controls
{
    public sealed record DefaultSlackDestinationViewModel : SensorSettingControlBase<DefaultSlackDestinationViewModel>
    {
        public HashSet<Guid> SelectedChats { get; set; } = [];

        public DefaultChatMode ChatMode { get; set; }


        public bool IsModify { get; }

        public bool IsCustom => ChatMode is DefaultChatMode.Custom;

        public bool IsFromParent => ChatMode is DefaultChatMode.FromParent;

        public bool IsNotInitialized => ChatMode is DefaultChatMode.NotInitialized;

        public bool IsEmpty => ChatMode is DefaultChatMode.Empty;


        // public constructor without parameters for post actions
        public DefaultSlackDestinationViewModel() : base() { }

        internal DefaultSlackDestinationViewModel(ParentRequest parentRequest) : base(parentRequest) { }

        public DefaultSlackDestinationViewModel(BaseNodeViewModel node, bool isModify = true) : this(node.DefaultSlackDestinations._parentRequest)
        {
            IsModify = isModify;
            ChatMode = node.DefaultSlackDestinations.ChatMode;
            SelectedChats = new(node.DefaultSlackDestinations.SelectedChats);
        }


        public bool IsSelectedDestination(SlackDestination destination) =>
            ChatMode is DefaultChatMode.Custom or DefaultChatMode.FromParent && SelectedChats.Contains(destination.Id);

        public bool IsSelectedMode(DefaultChatMode mode) => ChatMode == mode;

        public (HashSet<Guid> ids, DefaultChatMode mode) GetCurrentDestinations()
            => IsFromParent ? GetUsedValue(Parent) : (SelectedChats, ChatMode);


        public int GetDestinationsCount()
        {
            var response = SelectedChats.Count;

            if (IsFromParent)
                response += Parent?.GetDestinationsCount() ?? 0;

            return response;
        }

        public DefaultChatMode GetChatMode()
        {
            if (ChatMode is not DefaultChatMode.FromParent)
                return ChatMode;

            return Parent?.GetChatMode() ?? ChatMode;
        }

        public (HashSet<Guid> parentIds, HashSet<Guid> selected, DefaultChatMode mode, DefaultChatMode? parentMode) GetDestinations()
        {
            var parentIds = new HashSet<Guid>();
            var selected = SelectedChats;
            var parent = Parent;
            var parentMode = parent?.ChatMode;

            if (IsFromParent && parent is not null)
            {
                var (a, b) = GetParentDestinations();
                parentIds.UnionWith(a);

                if (b is not null)
                    parentMode = b;
            }

            return (parentIds, selected, ChatMode, parentMode);
        }

        public (HashSet<Guid> parentIds, DefaultChatMode? lastMode) GetParentDestinations()
        {
            var parent = Parent;
            var lastMode = parent?.ChatMode;

            var parentIds = new HashSet<Guid>();
            parentIds.UnionWith(parent?.SelectedChats ?? []);

            if (parent?.IsFromParent ?? false)
            {
                var (a, b) = parent.GetParentDestinations();
                parentIds.UnionWith(a);

                if (b is not null)
                    lastMode = b;
            }

            return (parentIds, lastMode);
        }

        public (string displayDestinations, string parentDestinations) GetDisplayDestinationName(List<SlackDestination> destinationList, out List<SlackDestination> allDestinations)
        {
            var destinations = ToAvailableDestinations(destinationList);
            allDestinations = [..destinations.Values];
            var parentDestinations = GetParentDisplayValue(destinations);

            string destinationsName;
            switch (ChatMode)
            {
                case DefaultChatMode.NotInitialized:
                    destinationsName = DefaultChatMode.NotInitialized.GetDisplayName();
                    break;
                case DefaultChatMode.Empty:
                    destinationsName = DefaultChatMode.Empty.GetDisplayName();
                    break;
                case DefaultChatMode.FromParent when SelectedChats.Count == 0:
                    destinationsName = parentDestinations;
                    break;
                case DefaultChatMode.FromParent when SelectedChats.Count != 0:
                    destinationsName = $"{parentDestinations}, {SelectedChats.ToNames(destinations)}";
                    break;
                case DefaultChatMode.Custom:
                    destinationsName = SelectedChats.ToNames(destinations);
                    break;
                default:
                    destinationsName = ChatMode.GetDisplayName();
                    break;
            }

            return (destinationsName, parentDestinations);
        }

        public string GetParentDisplayValue(Dictionary<Guid, SlackDestination> destinations)
        {
            if (Parent is null)
                return AsFromParent("parent is not initialized");

            var (parentIds, selectedId, mode, parentMode) = Parent.GetDestinations();
            parentIds.UnionWith(selectedId);
            string destinationsName;
            switch (mode)
            {
                case DefaultChatMode.Empty:
                    destinationsName = DefaultChatMode.Empty.GetDisplayName();
                    break;
                case DefaultChatMode.NotInitialized:
                    destinationsName = DefaultChatMode.NotInitialized.GetDisplayName();
                    break;
                case DefaultChatMode.FromParent when parentIds.Count == 0:
                    destinationsName = parentMode.GetDisplayName();
                    break;
                default:
                    destinationsName = parentIds.ToNames(destinations);
                    break;
            }

            return AsFromParent(destinationsName);
        }


        internal DefaultSlackDestinationViewModel FromModel(PolicyDestinationSettings model)
        {
            SelectedChats.Clear();
            foreach (var (id, _) in model.Chats)
                SelectedChats.Add(id);

            ChatMode = model.Mode switch
            {
                DefaultChatsMode.FromParent or DefaultChatsMode.FromFolder => DefaultChatMode.FromParent,
                DefaultChatsMode.Custom => DefaultChatMode.Custom,
                DefaultChatsMode.Empty => DefaultChatMode.Empty,
                _ => DefaultChatMode.NotInitialized,
            };

            return this;
        }

        internal PolicyDestinationSettings ToModel(Dictionary<Guid, string> availableDestinations) => new(ToEntity(availableDestinations));

        internal PolicyDestinationSettingsEntity ToEntity(Dictionary<Guid, string> availableDestinations)
        {
            var chats = new Dictionary<string, string>(1);

            if (IsFromParent || IsCustom)
                foreach (var destination in SelectedChats)
                    if (availableDestinations.TryGetValue(destination, out var destinationName))
                        chats.Add($"{destination}", destinationName);

            return new()
            {
                Chats = chats,
                Mode = (byte)(ChatMode switch
                {
                    DefaultChatMode.FromParent => DefaultChatsMode.FromParent,
                    DefaultChatMode.Custom => DefaultChatsMode.Custom,
                    DefaultChatMode.Empty => DefaultChatsMode.Empty,
                    _ => DefaultChatsMode.NotInitialized
                }),
            };
        }

        internal PolicyDestinationSettings ToUpdate(ProductNodeViewModel product, ISlackDestinationsManager destinationsManager)
        {
            return ToModel(product.GetAvailableSlackDestinations(destinationsManager));
        }


        private Dictionary<Guid, SlackDestination> ToAvailableDestinations(List<SlackDestination> destinations) =>
            destinations.ToDictionary(k => k.Id, v => v);

        private static (HashSet<Guid> ids, DefaultChatMode mode) GetUsedValue(DefaultSlackDestinationViewModel model)
        {
            if (model is not null && model.IsFromParent && model.HasParent)
                return GetUsedValue(model.Parent);

            return (model?.SelectedChats ?? [], model?.ChatMode ?? DefaultChatMode.NotInitialized);
        }

        private static string AsFromParent(string destinationName) => $"{DefaultChatMode.FromParent.GetDisplayName()} ({destinationName})";
    }
}
