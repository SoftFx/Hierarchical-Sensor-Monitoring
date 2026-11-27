using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Model.Notifications;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Core.Tests.Views
{
    // #1827: the three help modals of the chat form print their steps from ChatSetupGuide. Rendered with
    // the compiled views, as the MVC pipeline does, these tests pin what the browser receives: every step
    // of its guide in order, and the modal around the steps as it was — how it opens and closes, the
    // group command copied to the clipboard, the invitation links greyed out while the bot is not running.
    // And since #1831, that the management API's guide shows the same steps as each modal.
    public class ChatSetupHelpModalTests
    {
        private const string BotName = "hsm_alerts_bot";
        private const string Markup = "<img src=x onerror=alert(1)>";

        private const string GroupCommandHref = "javascript:getGroupStartCommand()";

        private readonly User _admin = new("admin") { IsAdmin = true };

        // The links of the Telegram modal while its bot is not running. Its invitation link has no
        // address here: the renderer registers no routes, so none can be built for it.
        private readonly ChatSetupInvitationLinks _greyedOut = new(TelegramBot.BotIsNotRunningError, "disabled-link", GroupCommandHref, InvitationHref: null);


        // What a webhook modal is, by channel: its view, its id, the functions that open and close
        // it, its title, and the guide it prints.
        private static readonly Dictionary<string, (string View, string Id, string Show, string Hide, string Title, IReadOnlyList<ChatSetupStep> Guide)> WebhookModals = new()
        {
            ["Slack"] = ("_SlackHelpModal.cshtml", "slackHelp_modal", "showSlackHelpModal", "hideSlackHelpModal", "How to create a Slack incoming webhook", ChatSetupGuide.Slack),
            ["Mattermost"] = ("_MattermostHelpModal.cshtml", "mattermostHelp_modal", "showMattermostHelpModal", "hideMattermostHelpModal", "How to create a Mattermost incoming webhook", ChatSetupGuide.Mattermost),
        };

        [Theory]
        [InlineData("Slack")]
        [InlineData("Mattermost")]
        public async Task WebhookModal_ListsEveryStepOfTheGuideInOrder(string channel)
        {
            var (view, id, show, hide, title, guide) = WebhookModals[channel];

            var html = await Render(view);

            AssertInOrder(html, guide.Select(step => ViewRenderer.Text(step.ToHtml())));
            Assert.Equal(guide.Count, Occurrences(html, "<li>"));
            Assert.Contains(title, html);
            AssertOpensAndCloses(html, id, show, hide);
        }

        [Fact]
        public async Task TelegramModal_ListsTheGroupStepsNumbered_AndTheDirectStep()
        {
            var html = await RenderTelegram(BotName);

            Assert.Contains("<h6 class=\"mt-2\">For group chat</h6>", html);
            AssertInOrder(html, ChatSetupGuide.TelegramGroup.Select((step, index) => $"{index + 1}. {ViewRenderer.Text(step.ToHtml(BotName, _greyedOut))}"));

            // A step to a line, and no empty line after the last one.
            Assert.Equal(ChatSetupGuide.TelegramGroup.Count - 1, Occurrences(html, "<br />"));

            Assert.Contains("<h6 class=\"mt-3\">For direct chat</h6>", html);
            AssertInOrder(html, ChatSetupGuide.TelegramDirect.Select(step => ViewRenderer.Text(step.ToHtml(BotName, _greyedOut))));

            AssertOpensAndCloses(html, "newChatHelp_modal", "showAddChatHelpModal", "hideAddChatHelpModal");
        }

        // The words that stand for an invitation are the modal's links, both greyed out with the reason
        // as the tooltip. The group command is fetched where it is clicked, the invitation link opens a new tab.
        [Fact]
        public async Task TelegramModal_GreysItsInvitationLinksOutWhileTheBotIsNotRunning()
        {
            var html = await RenderTelegram(BotName);

            Assert.Contains("<span title=\"Telegram Bot is not running.\"><a class=\"disabled-link\" href=\"" + GroupCommandHref + "\">this message</a></span>", html);
            Assert.Contains("<span title=\"Telegram Bot is not running.\"><a class=\"disabled-link\" target=\"_blank\">invitation link</a></span>", html);
        }

        [Fact]
        public async Task TelegramModal_CopiesTheGroupCommandToTheClipboard()
        {
            var html = await RenderTelegram(BotName);

            Assert.Contains("function getGroupStartCommand()", html);
            Assert.Contains(".done(function (command) {", html);
            Assert.Contains("copyToClipboard(command);", html);
        }

        // The three FAQ links used to carry `?setln=uz`, which opened the page in Uzbek for everyone.
        [Fact]
        public async Task TelegramModal_LinksTheFaqWithoutALanguage()
        {
            var html = await RenderTelegram(BotName);

            Assert.DoesNotContain("setln=", html);
            Assert.Contains("href=\"https://telegram.org/faq#q-how-do-i-create-a-group\"", html);
            Assert.Contains("href=\"https://telegram.org/faq#q-how-do-i-add-more-members-what-39s-an-invite-link\"", html);
            Assert.Contains("href=\"https://telegram.org/faq#q-can-i-assign-administrators\"", html);
        }

        // The bot's username is an administrator's entry of the Configuration page: text in the modal, never markup.
        [Fact]
        public async Task TelegramModal_ShowsTheBotNameAsText()
        {
            var html = await RenderTelegram(Markup);

            Assert.Contains("Telegram Bot (&lt;img src=x onerror=alert(1)&gt;)", html);
            Assert.DoesNotContain("<img src=x", html);
        }


        // One source, two readers (#1831): a modal prints a step as HTML, the management API's guide —
        // the service behind its tool and its action — shows it as text. Over the same stored bot,
        // every step a person performs carries the same words in both, in the same order; and HSM's
        // steps of a modal, which the guide never tells a person, are what its calls do.
        [Theory]
        [InlineData("Slack", "slack")]
        [InlineData("Mattermost", "mattermost")]
        [InlineData("Telegram", "telegram-chat")]
        public async Task AModal_AndTheSetupGuide_ShowTheSameSteps(string modal, string channel)
        {
            var page = WebUtility.HtmlDecode(modal == "Telegram" ? await RenderTelegram(BotName) : await Render(WebhookModals[modal].View));
            var guide = SetupGuideOf(channel);

            var source = (modal == "Telegram" ? ChatSetupGuide.TelegramGroup.Concat(ChatSetupGuide.TelegramDirect) : WebhookModals[modal].Guide)
                .ToList();
            var personsSteps = source.Where(step => step.Side == ChatSetupSide.Messenger).ToList();

            // As many steps for a person as the source holds, each carrying its words in both readers.
            Assert.Equal(personsSteps.Count, guide.Steps.Count);

            var at = 0;

            for (var index = 0; index < personsSteps.Count; index++)
                foreach (var words in TheWordsOf(personsSteps[index]))
                {
                    var found = page.IndexOf(words, at, StringComparison.Ordinal);

                    Assert.True(found >= 0, $"The {modal} modal does not say \"{words}\" of step {index + 1}, or not in order.");
                    Assert.True(guide.Steps[index].Contains(words), $"The guide's step {index + 1} of {channel} does not say \"{words}\".");

                    at = found + words.Length;
                }

            // HSM's own steps of a modal — paste the URL, save, send a test — are nobody's to do
            // here: the guide tells none of them and names a call for them instead.
            foreach (var words in source.Where(step => step.Side == ChatSetupSide.Hsm).SelectMany(TheWordsOf))
                Assert.DoesNotContain(guide.Steps, step => step.Contains(words));

            Assert.NotEmpty(guide.ToolSteps);
        }

        // The guide of the management API, over the service both its transports call, for the bot the
        // modal names and a caller past the gate.
        private static ChatSetupGuideDto SetupGuideOf(string channel)
        {
            var authorization = new Mock<IApiTokenAuthorizationService>();
            authorization.Setup(a => a.CanSeeAnyBoundary(It.IsAny<ClaimsPrincipal>())).Returns(true);

            var config = new Mock<IServerConfig>();
            config.SetupGet(c => c.Telegram).Returns(new TelegramConfig { BotName = BotName });

            return new ChatSetupGuideService(authorization.Object, config.Object).GetGuide(channel, new ClaimsPrincipal()).Value;
        }

        // What a step says, whatever shows it: its words, the names it shows in monospace, the words
        // of its links and the stressed ones — the bot's username where it stands. Shorter than a
        // word, or true only of the modal, a part is left out.
        private static IEnumerable<string> TheWordsOf(ChatSetupStep step) =>
            step.Parts.Select(part => part switch
            {
                ChatSetupPart.Plain { Value: var words } => words.Trim(),
                ChatSetupPart.Code { Value: var name } => name,
                ChatSetupPart.Bold { Value: var word } => word,
                ChatSetupPart.Link { Value: var words } => words,
                ChatSetupPart.BotName => BotName,
                _ => string.Empty,
            }).Where(words => words.Length > 1);


        private Task<string> Render(string view) => ViewRenderer.RenderAsync($"~/Views/Notifications/{view}", null, _admin);

        // The modal over a notification center whose bot never started — the one state a test reaches
        // without Telegram — for a chat of the chat form.
        private async Task<string> RenderTelegram(string botName)
        {
            var config = new Mock<IServerConfig>();
            config.SetupGet(c => c.Telegram).Returns(new TelegramConfig { BotName = botName });

            var chats = new Mock<IChatsManager>();
            var folders = new Mock<IFolderManager>();
            folders.Setup(f => f.GetValues()).Returns([]);

            using var slack = new HttpClient();
            using var mattermost = new HttpClient();
            await using var notifications = new NotificationsCenter(chats.Object, folders.Object, new Mock<ITreeValuesCache>().Object, config.Object,
                new SlackNotificationChannel(chats.Object, slack), new MattermostNotificationChannel(chats.Object, mattermost));

            return await ViewRenderer.RenderAsync("~/Views/Notifications/_NewChatHelpModal.cshtml", new NewChatHelpViewModel { ChatId = Guid.NewGuid() }, _admin,
                services =>
                {
                    services.AddSingleton(config.Object);
                    services.AddSingleton(notifications);
                });
        }

        private static void AssertInOrder(string html, IEnumerable<string> texts)
        {
            var page = WebUtility.HtmlDecode(html);
            var after = 0;

            foreach (var text in texts)
            {
                var at = page.IndexOf(text, after, StringComparison.Ordinal);

                Assert.True(at >= 0, $"Missing from the modal, or out of order: {text}");
                after = at + text.Length;
            }
        }

        private static int Occurrences(string html, string of)
        {
            var count = 0;

            for (var at = html.IndexOf(of, StringComparison.Ordinal); at >= 0; at = html.IndexOf(of, at + of.Length, StringComparison.Ordinal))
                count++;

            return count;
        }

        // The modal opens over a static backdrop and closes from its button, through the functions the
        // chat form calls.
        private static void AssertOpensAndCloses(string html, string id, string show, string hide)
        {
            Assert.Contains($"id=\"{id}\"", html);
            Assert.Contains($"function {show}() {{", html);
            Assert.Contains("backdrop: 'static'", html);
            Assert.Contains($"function {hide}() {{", html);
            Assert.Contains($"onclick=\"{hide}()\"", html);
        }
    }
}
