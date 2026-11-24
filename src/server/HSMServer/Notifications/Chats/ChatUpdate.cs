using HSMServer.ConcurrentStorage;
using HSMServer.Core.TableOfChanges;

namespace HSMServer.Notifications.Chats
{
    public record ChatUpdate : BaseUpdateRequest
    {
        // Who performs this write (#1825) — stamped by the write engine, consumed by the chat
        // journal subscription (#1828); the storage itself ignores it.
        public InitiatorInfo Initiator { get; init; }
        // Common
        public bool? SendMessages { get; init; }

        public int? MessagesAggregationTimeSec { get; init; }


        // Telegram chat id (optional; TelegramType / AuthorizationTime are init-only on Chat)
        public long? TelegramChatId { get; init; }


        // Telegram title/description — written by TelegramBot.ChatNamesSynchronization on every
        // bot start. Admin-owned Name/Description on the same chat are never touched by sync.
        public string TelegramChatTitle { get; init; }

        public string TelegramChatDescription { get; init; }


        // Slack (optional)
        public string SlackWebhookUrl { get; init; }


        // Mattermost (optional)
        public string MattermostWebhookUrl { get; init; }


        // Negative-clear flags. Chat.ApplyUpdate uses `?? current` for SlackWebhookUrl /
        // MattermostWebhookUrl, so a null update value means "don't change" and submitting an
        // empty string through EditChat cannot clear a saved webhook. Telegram binding is even
        // stricter — TelegramType / AuthorizationTime are internal-set and only mutable here.
        // These flags route around both issues by explicitly signalling "set this channel to null".
        public bool? ClearTelegramBinding { get; init; }

        public bool? ClearSlackWebhook { get; init; }

        public bool? ClearMattermostWebhook { get; init; }
    }
}
