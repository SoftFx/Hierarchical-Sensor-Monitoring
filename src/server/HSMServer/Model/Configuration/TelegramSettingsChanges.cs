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
    /// The three bot settings as one value (#1830): what a save compares, applies and
    /// journals. The web form posts all three; the management surface fills the omitted
    /// ones from the stored settings.
    /// </summary>
    internal readonly record struct TelegramBotSettings(string BotName, string BotToken, bool IsEnabled)
    {
        internal static TelegramBotSettings Of(TelegramConfig config) => new(config.BotName, config.BotToken, config.IsRunning);

        internal static TelegramBotSettings Of(TelegramSettingsViewModel posted) => new(posted.BotName, posted.BotToken, posted.IsEnabled);
    }


    /// <summary>
    /// Which of the three bot settings a save changes — the one definition of it, so what is
    /// journaled and what is applied cannot drift apart. An absent and an empty token or name
    /// are the same value: a server that was never configured holds nulls, and the web form
    /// posts an empty field as an empty string.
    /// </summary>
    internal readonly record struct TelegramBotSettingsDiff(bool IsEnabled, bool BotToken, bool BotName)
    {
        internal bool Any => IsEnabled || BotToken || BotName;

        internal static TelegramBotSettingsDiff Between(TelegramBotSettings current, TelegramBotSettings posted) => new(
            IsEnabled: current.IsEnabled != posted.IsEnabled,
            BotToken: JournalAppender.Change.SecretChanged(current.BotToken ?? string.Empty, posted.BotToken ?? string.Empty),
            BotName: (current.BotName ?? string.Empty) != (posted.BotName ?? string.Empty));
    }


    /// <summary>
    /// One save of the Telegram bot settings (#1824, #1828): what it changes, the log line
    /// of the save and its journal records. The bot token is a secret: the log and the
    /// journal say THAT it changed, never the old or the new value. Shared by the settings
    /// page and the management surface (#1830).
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

            Apply(config, TelegramBotSettings.Of(config.Telegram), TelegramBotSettings.Of(posted));

            Write(changes, journal, InitiatorInfo.AsUser(userName));

            return changes;
        }

        /// <summary>
        /// The changed settings of the save, in the order the log always listed them. An
        /// absent and an empty value are the same setting: a server that was never
        /// configured holds nulls, and the form posts an empty field as an empty string.
        /// </summary>
        internal static List<Change> Between(TelegramConfig current, TelegramSettingsViewModel posted) =>
            Between(TelegramBotSettings.Of(current), TelegramBotSettings.Of(posted));

        /// <inheritdoc cref="Between(TelegramConfig, TelegramSettingsViewModel)"/>
        internal static List<Change> Between(TelegramBotSettings current, TelegramBotSettings posted)
        {
            var changes = new List<Change>();
            var diff = TelegramBotSettingsDiff.Between(current, posted);

            if (diff.IsEnabled)
                changes.Add(new(new("Enable messages", $"{current.IsEnabled}", $"{posted.IsEnabled}"),
                    $"IsRunning: {current.IsEnabled} -> {posted.IsEnabled}"));

            if (diff.BotToken)
                changes.Add(new(JournalAppender.Change.OfSecret("Bot token", current.BotToken, posted.BotToken),
                    "BotToken: changed"));

            if (diff.BotName)
            {
                var currentName = current.BotName ?? string.Empty;
                var postedName = posted.BotName ?? string.Empty;

                changes.Add(new(new("Bot name", currentName, postedName), $"BotName: {currentName} -> {postedName}"));
            }

            return changes;
        }

        /// <summary>
        /// Applies a save to the configuration and persists it. Only a setting that changes
        /// is written: one that keeps its value is not saved over, so a writer of another
        /// setting racing this one is not overwritten.
        /// </summary>
        internal static void Apply(IServerConfig config, TelegramBotSettings current, TelegramBotSettings target)
        {
            var diff = TelegramBotSettingsDiff.Between(current, target);

            if (diff.IsEnabled)
                config.Telegram.IsRunning = target.IsEnabled;

            if (diff.BotToken)
                config.Telegram.BotToken = target.BotToken;

            if (diff.BotName)
                config.Telegram.BotName = target.BotName;

            config.ResaveSettings();
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
