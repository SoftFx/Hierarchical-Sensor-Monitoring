using HSMServer.Controllers;
using HSMServer.Model.Configuration;
using HSMServer.ServerConfiguration;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // #1824: the change description that SaveTelegramSettings logs. The bot token is a secret —
    // the description says THAT it changed, never the old or the new value; the bot name and
    // the on/off switch keep their "old -> new" lines.
    public sealed class ConfigurationControllerTelegramSettingsChangesTests
    {
        private const string OldToken = "110201543:AAE-old-token-value-xxxxxxxxxxxxxxx";
        private const string NewToken = "110201543:AAE-new-token-value-yyyyyyyyyyyyyyy";


        [Fact]
        public void BuildTelegramChangesText_TokenChanged_SaysSoWithoutTheValues()
        {
            var current = new TelegramConfig { BotName = "hsm_alerts_bot", BotToken = OldToken, IsRunning = true };
            var posted = new TelegramSettingsViewModel { BotName = "hsm_alerts_bot", BotToken = NewToken, IsEnabled = true };

            var changes = ConfigurationController.BuildTelegramChangesText(current, posted, "admin");

            Assert.NotNull(changes);
            Assert.Contains("BotToken", changes);
            Assert.DoesNotContain(OldToken, changes);
            Assert.DoesNotContain(NewToken, changes);
        }


        [Fact]
        public void BuildTelegramChangesText_NameAndSwitchChanged_KeepsTheirValues()
        {
            var current = new TelegramConfig { BotName = "hsm_alerts_bot", BotToken = OldToken, IsRunning = true };
            var posted = new TelegramSettingsViewModel { BotName = "hsm_oncall_bot", BotToken = OldToken, IsEnabled = false };

            var changes = ConfigurationController.BuildTelegramChangesText(current, posted, "admin");

            Assert.Contains("IsRunning: True -> False", changes);
            Assert.Contains("BotName: hsm_alerts_bot -> hsm_oncall_bot", changes);
            Assert.DoesNotContain("BotToken", changes);
        }


        [Fact]
        public void BuildTelegramChangesText_NothingChanged_AnswersNull()
        {
            var current = new TelegramConfig { BotName = "hsm_alerts_bot", BotToken = OldToken, IsRunning = true };
            var posted = new TelegramSettingsViewModel { BotName = "hsm_alerts_bot", BotToken = OldToken, IsEnabled = true };

            Assert.Null(ConfigurationController.BuildTelegramChangesText(current, posted, "admin"));
        }
    }
}
