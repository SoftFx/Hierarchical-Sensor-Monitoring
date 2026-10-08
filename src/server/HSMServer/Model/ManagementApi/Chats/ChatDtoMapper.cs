using System;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;

namespace HSMServer.Model.ManagementApi.Chats
{
    // Entity -> DTO for the chat read surface. Credential-free: the visibility
    // decision lives in ChatsReadService, not here, so the mapper stays usable
    // from any context (the AlertSchedules mapper follows the same split).
    public static class ChatDtoMapper
    {
        public static ChatDto ToDto(Chat chat) => new()
        {
            Id = chat.Id,
            Name = chat.Name,
            Description = chat.Description,
            CreationDate = chat.CreationDate,
            Author = chat.Author,
            SendMessages = chat.SendMessages,
            MessagesAggregationTimeSec = chat.MessagesAggregationTimeSec,
            TelegramChatId = chat.TelegramChatId?.Identifier,
            TelegramType = TelegramTypeName(chat.TelegramType),
            TelegramAuthorizationTime = chat.AuthorizationTime,
            TelegramChatTitle = chat.TelegramChatTitle,
            TelegramChatDescription = chat.TelegramChatDescription,
            HasSlackWebhook = !string.IsNullOrEmpty(chat.SlackWebhookUrl),
            HasMattermostWebhook = !string.IsNullOrEmpty(chat.MattermostWebhookUrl),
            Folders = [.. chat.Folders],
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
