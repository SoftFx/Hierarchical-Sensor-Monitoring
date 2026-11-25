using HSMServer.Core.Journal;
using HSMServer.Notifications;
using System;
using System.Collections.Generic;

namespace HSMServer.Notifications.Chats
{
    /// <summary>
    /// The journal's view of a chat (#1828): the state of its journaled settings at one
    /// moment, and the records one write leaves between two such states. A record carries
    /// a setting's old and new value — except where the value is a secret: a webhook reads
    /// as the state of its channel, and the Telegram binding as not bound, bound or removed.
    /// </summary>
    internal static class ChatJournal
    {
        internal const string CreateEnviroment = "Create chat";
        internal const string UpdateEnviroment = "Chat update";
        internal const string RemoveEnviroment = "Remove chat";

        // A plain struct on purpose: a record would print the chat's secrets through its
        // ToString.
        internal struct State
        {
            internal string Name;
            internal string Description;
            internal bool SendMessages;
            internal int MessagesAggregationTimeSec;
            internal string SlackWebhookUrl;
            internal string MattermostWebhookUrl;
            internal long? TelegramChatId;
            internal ConnectedChatType? TelegramType;
        }


        internal static State StateOf(Chat chat) => new()
        {
            Name = chat.Name,
            // An absent and an empty description are the same setting.
            Description = chat.Description ?? string.Empty,
            SendMessages = chat.SendMessages,
            MessagesAggregationTimeSec = chat.MessagesAggregationTimeSec,
            SlackWebhookUrl = chat.SlackWebhookUrl,
            MattermostWebhookUrl = chat.MattermostWebhookUrl,
            TelegramChatId = chat.TelegramChatId?.Identifier,
            TelegramType = chat.TelegramType,
        };

        // The chat itself, then every channel it is created with. The settings a new chat
        // starts from (messages on, the default delay) are no one's change and stay out.
        internal static List<JournalAppender.Change> CreationChanges(in State state)
        {
            var changes = new List<JournalAppender.Change> { new("chat", "", state.Name) };

            AddWebhookChange(changes, "Slack webhook", null, state.SlackWebhookUrl);
            AddWebhookChange(changes, "Mattermost webhook", null, state.MattermostWebhookUrl);

            if (state.TelegramChatId is not null)
                changes.Add(new("Telegram binding", "not bound", DescribeBinding(state)));

            return changes;
        }

        // The settings in the order of the chat form. The Telegram chat id is compared only:
        // for a direct chat it is a person's Telegram user id, and it never reaches a record.
        internal static List<JournalAppender.Change> ChangesBetween(in State before, in State after)
        {
            var changes = new List<JournalAppender.Change>();

            if (after.Name != before.Name)
                changes.Add(new("Name", before.Name, after.Name));

            if (after.Description != before.Description)
                changes.Add(new("Description", before.Description, after.Description));

            if (after.SendMessages != before.SendMessages)
                changes.Add(new("Enable messages", $"{before.SendMessages}", $"{after.SendMessages}"));

            if (after.MessagesAggregationTimeSec != before.MessagesAggregationTimeSec)
                changes.Add(new("Messages delay", $"{before.MessagesAggregationTimeSec} sec", $"{after.MessagesAggregationTimeSec} sec"));

            AddWebhookChange(changes, "Slack webhook", before.SlackWebhookUrl, after.SlackWebhookUrl);
            AddWebhookChange(changes, "Mattermost webhook", before.MattermostWebhookUrl, after.MattermostWebhookUrl);

            // A bound chat that moved to another Telegram id (a group becoming a supergroup)
            // is still bound: only a binding that appears or disappears is a change.
            var wasBound = before.TelegramChatId is not null;
            var isBound = after.TelegramChatId is not null;

            if (wasBound != isBound)
                changes.Add(new("Telegram binding",
                    wasBound ? DescribeBinding(before) : "not bound",
                    isBound ? DescribeBinding(after) : "removed"));

            return changes;
        }

        // A webhook is a secret: the record names the state of its channel, never the URL.
        private static void AddWebhookChange(List<JournalAppender.Change> changes, string channel, string before, string after)
        {
            var wasSet = IsSet(before);
            var isSet = IsSet(after);

            if (wasSet == isSet && (!isSet || before == after))
                return;

            changes.Add(new(channel,
                wasSet ? "set" : "not set",
                !isSet ? "removed" : wasSet ? "changed" : "set"));
        }

        private static bool IsSet(string webhookUrl) => !string.IsNullOrEmpty(webhookUrl);

        private static string DescribeBinding(in State state) => state.TelegramType switch
        {
            ConnectedChatType.TelegramPrivate => "bound (direct)",
            ConnectedChatType.TelegramGroup => "bound (group)",
            _ => "bound",
        };
    }
}
