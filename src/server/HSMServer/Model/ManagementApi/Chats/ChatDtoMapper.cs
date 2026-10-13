using System;
using System.Linq;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;

namespace HSMServer.Model.ManagementApi.Chats
{
    // Entity -> DTO for the chat read surface. Credential-free: the
    // visibility and detail decisions live in ChatsReadService, not here —
    // the service passes the folder-sight predicate and the detail flag in,
    // so the mapper stays usable from any context (the AlertSchedules mapper
    // follows the same split).
    public static class ChatDtoMapper
    {
        public static ChatDto ToDto(Chat chat, Func<Guid, bool> isFolderVisible, bool includeDetail) => new()
        {
            Id = chat.Id,
            Name = chat.Name,
            TelegramType = TelegramTypeName(chat.TelegramType),
            Folders = [.. StableCopy.Of(() => chat.Folders).Where(isFolderVisible)],
            HasSlackWebhook = !string.IsNullOrEmpty(chat.SlackWebhookUrl),
            HasMattermostWebhook = !string.IsNullOrEmpty(chat.MattermostWebhookUrl),

            // Detail fields — null unless the owner is an admin or a manager
            // of a bound folder (the web UI's EditChat gate); the fields every
            // gated caller gets are the id-discovery half the write endpoints
            // validate against.
            Description = includeDetail ? chat.Description : null,
            CreationDate = includeDetail ? chat.CreationDate : null,
            Author = includeDetail ? chat.Author : null,
            SendMessages = includeDetail ? chat.SendMessages : null,
            MessagesAggregationTimeSec = includeDetail ? chat.MessagesAggregationTimeSec : null,
            TelegramChatId = includeDetail ? chat.TelegramChatId?.Identifier : null,
            TelegramAuthorizationTime = includeDetail ? chat.AuthorizationTime : null,
            TelegramChatTitle = includeDetail ? chat.TelegramChatTitle : null,
            TelegramChatDescription = includeDetail ? chat.TelegramChatDescription : null,
        };


        // Mirrors ConnectedChatType's [Display] names without reflection —
        // the wire contract ("direct"/"group") is load-bearing for clients.
        private static string TelegramTypeName(ConnectedChatType? type) => type switch
        {
            ConnectedChatType.TelegramPrivate => "direct",
            ConnectedChatType.TelegramGroup => "group",
            _ => null,
        };
    }
}
