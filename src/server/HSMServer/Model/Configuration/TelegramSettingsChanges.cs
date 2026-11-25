using HSMServer.Core.Journal;
using HSMServer.Core.TableOfChanges;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMServer.Model.Configuration
{
    /// <summary>
    /// One save of the Telegram bot settings (#1824, #1828): what it changes, the log line
    /// of the save and its journal records. The bot token is a secret: the log and the
    /// journal say THAT it changed, never the old or the new value.
    /// </summary>
    internal static class TelegramSettingsChanges
    {
        /// <summary>
        /// One changed setting: the journal record of the change and the save's log line
        /// for it.
        /// </summary>
        internal sealed record Change(JournalAppender.Change Record, string LogLine);


        // A server setting is no tree node: the bot settings journal under their own fixed
        // id, so the journal's entity filter can address them.
        internal static readonly Guid JournalEntityId = new("D1D5B7C3-6E2A-4B9F-9A71-3C8E2F6A4D15");

        internal const string JournalEnviroment = "Telegram bot settings update";


        /// <summary>
        /// The save of the settings page: the changes of the posted form, applied to the
        /// configuration and persisted, then journaled — the journal never precedes the
        /// write it records. Nothing is applied and nothing is journaled when nothing
        /// changed.
        /// </summary>
        internal static IReadOnlyList<Change> Save(IServerConfig config, TelegramSettingsViewModel posted, string userName,
            JournalAppender journal)
        {
            var changes = Between(config.Telegram, posted);

            if (changes.Count == 0)
                return changes;

            config.Telegram.IsRunning = posted.IsEnabled;
            config.Telegram.BotToken = posted.BotToken;
            config.Telegram.BotName = posted.BotName;

            config.ResaveSettings();

            Write(changes, journal, InitiatorInfo.AsUser(userName));

            return changes;
        }

        /// <summary>
        /// The changed settings of the save, in the order the log always listed them. An
        /// absent and an empty value are the same setting: a server that was never
        /// configured holds nulls, and the form posts an empty field as an empty string.
        /// </summary>
        internal static List<Change> Between(TelegramConfig current, TelegramSettingsViewModel posted)
        {
            var changes = new List<Change>();
            var currentToken = current.BotToken ?? string.Empty;
            var postedToken = posted.BotToken ?? string.Empty;
            var currentName = current.BotName ?? string.Empty;
            var postedName = posted.BotName ?? string.Empty;

            if (current.IsRunning != posted.IsEnabled)
                changes.Add(new(new("Enable messages", $"{current.IsRunning}", $"{posted.IsEnabled}"),
                    $"IsRunning: {current.IsRunning} -> {posted.IsEnabled}"));

            if (JournalAppender.Change.SecretChanged(currentToken, postedToken))
                changes.Add(new(JournalAppender.Change.OfSecret("Bot token", currentToken, postedToken),
                    "BotToken: changed"));

            if (currentName != postedName)
                changes.Add(new(new("Bot name", currentName, postedName),
                    $"BotName: {currentName} -> {postedName}"));

            return changes;
        }

        // The log line of the save; null when nothing changed.
        internal static string Describe(IReadOnlyList<Change> changes, string userName)
        {
            if (changes.Count == 0)
                return null;

            var sb = new StringBuilder($"{userName} is changing Telegram config:{Environment.NewLine}");

            foreach (var change in changes)
                sb.AppendLine(change.LogLine);

            return sb.ToString();
        }

        /// <summary>Leaves one serverSettings record per change; no changes, no record.</summary>
        internal static void Write(IReadOnlyList<Change> changes, JournalAppender journal, InitiatorInfo initiator) =>
            journal.Write(JournalEntityId, JournalKinds.ServerSettings, JournalEnviroment, "Telegram bot", initiator,
                changes.Select(c => c.Record).ToList());
    }
}
