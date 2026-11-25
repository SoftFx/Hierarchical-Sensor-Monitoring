using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Model.Configuration;
using HSMServer.ServerConfiguration;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public sealed class TelegramSettingsChangesFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TelegramSettingsChangesTests);
    }

    // #1824 / #1828: what one save of the Telegram bot settings changes. The same list of
    // changes feeds the log line of the save and the `serverSettings` journal records. The
    // bot token is a secret: both say THAT it changed, never the old or the new value.
    [Collection("Database collection")]
    public class TelegramSettingsChangesTests : MonitoringCoreTestsBase<TelegramSettingsChangesFixture>
    {
        private const string OldToken = "110201543:AAE-old-token-value-xxxxxxxxxxxxxxx";
        private const string NewToken = "110201543:AAE-new-token-value-yyyyyyyyyyyyyyy";

        private readonly JournalAppender _journal;


        public TelegramSettingsChangesTests(TelegramSettingsChangesFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _journal = new JournalAppender(_journalService, TimeProvider.System);
        }


        [Fact]
        public void TokenChanged_DescriptionSaysSoWithoutTheValues()
        {
            var changes = TelegramSettingsChanges.Between(
                new TelegramConfig { BotName = "hsm_alerts_bot", BotToken = OldToken, IsRunning = true },
                new TelegramSettingsViewModel { BotName = "hsm_alerts_bot", BotToken = NewToken, IsEnabled = true });

            var description = TelegramSettingsChanges.Describe(changes, "admin");

            Assert.Contains("BotToken: changed", description);
            Assert.DoesNotContain(OldToken, description);
            Assert.DoesNotContain(NewToken, description);
        }

        [Fact]
        public void NameAndSwitchChanged_DescriptionKeepsTheirValues_AndNamesNoToken()
        {
            var changes = TelegramSettingsChanges.Between(
                new TelegramConfig { BotName = "hsm_alerts_bot", BotToken = OldToken, IsRunning = true },
                new TelegramSettingsViewModel { BotName = "hsm_oncall_bot", BotToken = OldToken, IsEnabled = false });

            var description = TelegramSettingsChanges.Describe(changes, "admin");

            Assert.Equal(
                $"admin is changing Telegram config:{Environment.NewLine}IsRunning: True -> False{Environment.NewLine}BotName: hsm_alerts_bot -> hsm_oncall_bot{Environment.NewLine}",
                description);
        }

        [Fact]
        public void NothingChanged_HasNoChanges_AndNoDescription()
        {
            var changes = TelegramSettingsChanges.Between(
                new TelegramConfig { BotName = "hsm_alerts_bot", BotToken = OldToken, IsRunning = true },
                new TelegramSettingsViewModel { BotName = "hsm_alerts_bot", BotToken = OldToken, IsEnabled = true });

            Assert.Empty(changes);
            Assert.Null(TelegramSettingsChanges.Describe(changes, "admin"));
        }

        // The save as the settings page runs it: the posted form is applied and persisted,
        // then journaled — the journal never precedes the write it records (ADR-0010).
        [Fact]
        public void Save_AppliesAndPersistsThePostedSettings_ThenJournalsTheChanges()
        {
            var mark = Records().Count;
            var telegram = new TelegramConfig { BotName = "hsm_alerts_bot", BotToken = OldToken, IsRunning = true };
            var persisted = false;
            var journaledTooEarly = false;

            // A journal write that comes before the save persisted is refused — the appender
            // logs and swallows it, so the records would go missing and the test with them.
            var db = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailJournalWrite = _ => journaledTooEarly |= !persisted,
            };

            var config = Save(telegram, new TelegramSettingsViewModel { BotName = "hsm_oncall_bot", BotToken = NewToken, IsEnabled = false },
                new JournalAppender(new JournalService(db), TimeProvider.System), onPersisted: () => persisted = true);

            Assert.False(journaledTooEarly);
            Assert.False(telegram.IsRunning);
            Assert.Equal(NewToken, telegram.BotToken);
            Assert.Equal("hsm_oncall_bot", telegram.BotName);
            config.Verify(c => c.ResaveSettings(), Times.Once);

            var records = Records().Skip(mark).ToList();
            Assert.Equal(
                [
                    ("Enable messages", "True", "False"),
                    ("Bot token", "set", "changed"),
                    ("Bot name", "hsm_alerts_bot", "hsm_oncall_bot"),
                ],
                records.Select(r => (r.PropertyName, r.OldValue, r.NewValue)).ToList());
            Assert.All(records, r =>
            {
                Assert.Equal(JournalKinds.ServerSettings, r.Kind);
                Assert.Equal("User (admin)", r.Initiator);
                Assert.Equal("Telegram bot", r.Path);
                Assert.Equal(JournalOperations.Update, JournalOperations.Of(r));
                Assert.Null(r.ChangeSetId);
            });
        }

        // The journal is appended after the save landed: a storage failure of the journal
        // must not fail or undo the save.
        [Fact]
        public void Save_WhenTheJournalWriteFails_StillAppliesAndPersistsTheSettings()
        {
            var telegram = new TelegramConfig { BotName = "hsm_alerts_bot", BotToken = OldToken, IsRunning = true };
            var db = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false) { ShouldFailJournalWrite = _ => true };

            var config = Save(telegram, new TelegramSettingsViewModel { BotToken = NewToken },
                new JournalAppender(new JournalService(db), TimeProvider.System));

            Assert.Equal(NewToken, telegram.BotToken);
            config.Verify(c => c.ResaveSettings(), Times.Once);
        }

        [Theory]
        [InlineData(null, NewToken, "not set", "set")]
        [InlineData(OldToken, NewToken, "set", "changed")]
        [InlineData(OldToken, "", "set", "removed")]
        public void Save_TokenChanged_RecordNamesTheStateOfTheSetting_NeverTheToken(
            string currentToken, string postedToken, string oldValue, string newValue)
        {
            var mark = Records().Count;

            Save(new TelegramConfig { BotToken = currentToken }, new TelegramSettingsViewModel { BotToken = postedToken });

            var record = Assert.Single(Records().Skip(mark));
            Assert.Equal(("Bot token", oldValue, newValue), (record.PropertyName, record.OldValue, record.NewValue));

            // Every text cell a record can carry.
            var text = string.Join("\n", record.Enviroment, record.Initiator, record.PropertyName, record.OldValue, record.NewValue, record.Path);
            if (currentToken is not null)
                Assert.DoesNotContain(currentToken, text);
            Assert.DoesNotContain(NewToken, text);
        }

        [Fact]
        public void Save_NothingChanged_PersistsNothing_AndLeavesNoRecord()
        {
            var current = new TelegramConfig { BotName = "hsm_alerts_bot", BotToken = OldToken, IsRunning = true };
            var mark = Records().Count;

            var config = Save(current, new TelegramSettingsViewModel { BotName = current.BotName, BotToken = current.BotToken, IsEnabled = current.IsRunning });

            config.Verify(c => c.ResaveSettings(), Times.Never);
            Assert.Equal(mark, Records().Count);
        }

        // A server that was never configured holds nulls, and the form posts an empty field
        // as an empty string: filling in the bot name is one change, not two.
        [Fact]
        public void Save_FirstSetupWithAnEmptyToken_LeavesABotNameRecordOnly()
        {
            var mark = Records().Count;

            Save(new TelegramConfig(), new TelegramSettingsViewModel { BotName = "hsm_alerts_bot", BotToken = "", IsEnabled = false });

            var records = Records().Skip(mark).ToList();
            Assert.Equal([("Bot name", "", "hsm_alerts_bot")], records.Select(r => (r.PropertyName, r.OldValue, r.NewValue)).ToList());
        }


        // The save as ConfigurationController.SaveTelegramSettings runs it: the posted form
        // against the current configuration, journaled under the signed-in user.
        private Mock<IServerConfig> Save(TelegramConfig current, TelegramSettingsViewModel posted,
            JournalAppender journal = null, Action onPersisted = null)
        {
            var config = new Mock<IServerConfig>();
            config.SetupGet(c => c.Telegram).Returns(current);
            config.Setup(c => c.ResaveSettings()).Callback(() => onPersisted?.Invoke());

            TelegramSettingsChanges.Save(config.Object, posted, "admin", journal ?? _journal);

            return config;
        }

        // The journal's own read (the one behind the global Journal page).
        private List<JournalRecordModel> Records() =>
            _journalService.GetRecords(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMinutes(1))
                .Where(r => r.Key.Id == TelegramSettingsChanges.JournalEntityId)
                .OrderBy(r => r.Key.Time)
                .ToList();
    }
}
