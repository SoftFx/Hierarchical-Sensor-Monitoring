using System;
using HSMServer.Extensions;
using HSMServer.Notifications.Chats;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // #1831: a step of the guide as text — the twin of the HTML the web modals print (ChatSetupGuideTests
    // pin that one): the same parts, shown another way. One step per kind of part, then the two ways an
    // invitation reads — the call that issues it in the guide, what was issued in the invitation's answer.
    public class ChatSetupStepTextTests
    {
        private const string BotName = "hsm_alerts_bot";

        private static readonly ChatSetupInvitationText ByTheTool =
            ChatSetupInvitationText.NotIssued(kind => $"issue(kind: \"{kind}\")");

        private const string Command = "/start@hsm_alerts_bot abc";
        private const string Link = "https://t.me/hsm_alerts_bot?start=abc";


        [Fact]
        public void Words_ANameAndAStressedWord_ReadAsWritten_TheNameInBackticks()
        {
            var step = new ChatSetupStep(ChatSetupSide.Messenger, "Open the main menu → ", new ChatSetupPart.Code("Integrations"),
                " and press ", new ChatSetupPart.Bold("Start"), ".");

            Assert.Equal("Open the main menu → `Integrations` and press Start.", step.ToText(BotName, ByTheTool));
        }

        // A link keeps its words and shows where they lead: an agent can open the page, and a person
        // reading a relayed step gets a link to click.
        [Fact]
        public void ALink_KeepsItsWords_AndShowsItsAddress()
        {
            var step = new ChatSetupStep(ChatSetupSide.Messenger, new ChatSetupPart.Link("Create", "https://telegram.org/faq#q-how-do-i-create-a-group"),
                " group chat in Telegram.");

            Assert.Equal("[Create](https://telegram.org/faq#q-how-do-i-create-a-group) group chat in Telegram.", step.ToText(BotName, ByTheTool));
        }

        [Fact]
        public void TheBot_IsNamedAsStored()
        {
            var step = new ChatSetupStep(ChatSetupSide.Messenger, "Add Telegram Bot (", new ChatSetupPart.BotName(), ").");

            Assert.Equal("Add Telegram Bot (hsm_alerts_bot).", step.ToText(BotName, ByTheTool));
        }

        // No bot is configured: the step names the field that says so — never an empty pair of brackets,
        // which reads as a bot without a name. (The web modal names an empty one, as it always did.)
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void NoBot_IsNamedByTheFieldThatSaysSo(string noBotName)
        {
            var step = new ChatSetupStep(ChatSetupSide.Messenger, "Add Telegram Bot (", new ChatSetupPart.BotName(), ").");

            Assert.Equal("Add Telegram Bot (<botName>).", step.ToText(noBotName, ByTheTool));
        }

        // "From clipboard" is how the web modal hands the group command over — by a link that copies it.
        // An agent is handed the command by the call the part before names.
        [Fact]
        public void TheWordsOfTheModalAlone_AreLeftOut()
        {
            var step = new ChatSetupStep(ChatSetupSide.Messenger, "Send ", new ChatSetupPart.StartCommand("this message"),
                new ChatSetupPart.InTheModal(" from clipboard"), " to group chat.");

            Assert.Equal("Send the `value` of `issue(kind: \"group\")` to group chat.", step.ToText(BotName, ByTheTool));
        }

        [Fact]
        public void AnInvitation_NothingIssuedYet_NamesTheCallAndItsValueField()
        {
            var group = new ChatSetupStep(ChatSetupSide.Messenger, "Send ", new ChatSetupPart.StartCommand("this message"), " to group chat.");
            var direct = new ChatSetupStep(ChatSetupSide.Messenger, "Click ", new ChatSetupPart.InvitationLink("invitation link"), " and press Start.");

            Assert.Equal("Send the `value` of `issue(kind: \"group\")` to group chat.", group.ToText(BotName, ByTheTool));
            Assert.Equal("Click the `value` of `issue(kind: \"direct\")` and press Start.", direct.ToText(BotName, ByTheTool));
        }

        // The invitation's own answer: the link it issued under the step's own words, the command it
        // issued as what the person posts.
        [Fact]
        public void AnIssuedInvitation_IsReadInThePlaceOfItsWords()
        {
            var direct = new ChatSetupStep(ChatSetupSide.Messenger, "Click ", new ChatSetupPart.InvitationLink("invitation link"), " and press Start.");
            var group = new ChatSetupStep(ChatSetupSide.Messenger, "Send ", new ChatSetupPart.StartCommand("this message"), " to group chat.");

            Assert.Equal($"Click [invitation link]({Link}) and press Start.", direct.ToText(BotName, ChatSetupInvitationText.IssuedLink(Link)));
            Assert.Equal($"Send `{Command}` to group chat.", group.ToText(BotName, ChatSetupInvitationText.IssuedCommand(Command)));
        }

        // An invitation of the other kind is refused aloud: a group's step cannot stand on a direct
        // chat's link, nor a direct chat's step on a group's command.
        [Fact]
        public void AnInvitationOfTheOtherKind_IsRefused()
        {
            var group = new ChatSetupStep(ChatSetupSide.Messenger, "Send ", new ChatSetupPart.StartCommand("this message"), " to group chat.");
            var direct = new ChatSetupStep(ChatSetupSide.Messenger, "Click ", new ChatSetupPart.InvitationLink("invitation link"), " and press Start.");

            Assert.Throws<InvalidOperationException>(() => group.ToText(BotName, ChatSetupInvitationText.IssuedLink(Link)));
            Assert.Throws<InvalidOperationException>(() => direct.ToText(BotName, ChatSetupInvitationText.IssuedCommand(Command)));
        }
    }
}
