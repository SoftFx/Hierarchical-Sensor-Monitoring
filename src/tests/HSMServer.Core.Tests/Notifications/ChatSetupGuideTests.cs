using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Encodings.Web;
using HSMServer.Extensions;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Html;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // #1827: the chat setup instructions left the help modals' Razor markup for one source in code. These
    // tests pin the copy as the web modal prints it — every step, in its order — so that moving it changed
    // its home and nothing else; the side of each step, which tells the agent-facing guide (#1831) what it
    // may answer; and the links, which must be absolute http(s).
    public class ChatSetupGuideTests
    {
        private const string BotName = "hsm_alerts_bot";
        private const string InvitationHref = "/Notifications/OpenInvitationLink?folderId=00000000-0000-0000-0000-000000000001";

        // The links of a modal whose bot runs: nothing greyed out, no tooltip. The link builder writes
        // a tag's attributes by name, class before href.
        private static readonly ChatSetupInvitationLinks Live = new(Title: string.Empty, Class: string.Empty,
            GroupCommandHref: "javascript:getGroupStartCommand()", InvitationHref: InvitationHref);


        [Fact]
        public void Slack_KeepsTheSeventeenStepsOfTheModalInOrder()
        {
            Assert.Equal(new[]
            {
                "You need Slack workspace admin permissions, or permission to install apps.",
                "Open <code>api.slack.com/apps</code>.",
                "Click <code>Create New App</code>.",
                "Choose <code>From scratch</code>.",
                "Enter an app name, for example <code>my space</code>.",
                "Select the Slack workspace where alerts should be sent.",
                "Click <code>Create App</code>.",
                "In the left menu, open <code>Incoming Webhooks</code> under <code>Features</code>.",
                "Turn on <code>Activate Incoming Webhooks</code>. The switch should change from Off to On.",
                "Click <code>Add New Webhook to Workspace</code> at the bottom of the page.",
                "Slack will ask which channel should receive messages. Select the channel. The channel must already exist in Slack before this step.",
                "Click <code>Allow</code>.",
                "Slack will generate a URL like <code>https://hooks.slack.com/services/...</code>.",
                "Copy this URL.",
                "Paste it into the <code>Slack webhook URL</code> field in HSM.",
                "Save the chat.",
                "In HSM, use <code>Send test Slack message</code> to confirm that Slack receives the message.",
            }, Render(ChatSetupGuide.Slack));
        }

        [Fact]
        public void Mattermost_KeepsTheTwelveStepsOfTheModalInOrder()
        {
            Assert.Equal(new[]
            {
                "You need a Mattermost account with permission to create integrations, or ask a System Admin for help.",
                "Open your Mattermost site in the browser.",
                "Open the main menu (top-left) → <code>Integrations</code> → <code>Incoming Webhooks</code>.",
                "If the page says incoming webhooks are disabled, ask a System Admin to enable them: <code>System Console</code> → <code>Integrations</code> → <code>Custom Integrations</code> → turn on <code>Enable Incoming Webhooks</code>.",
                "Click <code>Add Incoming Webhook</code>.",
                "Select the channel that should receive alert messages. The channel must already exist.",
                "Optionally set a display name (e.g. <code>HSM alerts</code>) and description.",
                "Click <code>Save</code>.",
                "Mattermost will display a URL like <code>https://your-mattermost.example/hooks/xxxx</code>.",
                "Copy this URL.",
                "Paste it into the <code>Mattermost webhook URL</code> field in HSM.",
                "Save the chat.",
            }, Render(ChatSetupGuide.Mattermost));
        }

        // The bot is named without the @ of a mention — what the modal has always shown (Razor took
        // the @ of "(@BotName" for the start of the expression).
        [Fact]
        public void Telegram_KeepsTheFourGroupStepsAndTheDirectStepOfTheModalInOrder()
        {
            Assert.Equal(new[]
            {
                "<a href=\"https://telegram.org/faq#q-how-do-i-create-a-group\" target=\"_blank\">Create</a> group chat in Telegram.",
                "<a href=\"https://telegram.org/faq#q-how-do-i-add-more-members-what-39s-an-invite-link\" target=\"_blank\">Add</a> Telegram Bot (hsm_alerts_bot) to group chat.",
                "<a href=\"https://telegram.org/faq#q-can-i-assign-administrators\" target=\"_blank\">Give Admin right</a> to Telegram Bot (hsm_alerts_bot).",
                "Send <span title=\"\"><a class=\"\" href=\"javascript:getGroupStartCommand()\">this message</a></span> from clipboard to group chat.",
            }, Render(ChatSetupGuide.TelegramGroup, Live));

            Assert.Equal(new[]
            {
                "Click <span title=\"\"><a class=\"\" href=\"" + InvitationHref + "\" target=\"_blank\">invitation link</a></span>"
                    + " and press <b>Start</b> button at opened direct chat with Telegram Bot (hsm_alerts_bot).",
            }, Render(ChatSetupGuide.TelegramDirect, Live));
        }


        [Fact]
        public void Slack_LastThreeStepsAreInHsm_TheOtherFourteenInTheMessenger() =>
            Assert.Equal(Sides(14, 3), ChatSetupGuide.Slack.Select(step => step.Side));

        [Fact]
        public void Mattermost_LastTwoStepsAreInHsm_TheOtherTenInTheMessenger() =>
            Assert.Equal(Sides(10, 2), ChatSetupGuide.Mattermost.Select(step => step.Side));

        // A person carries every Telegram step out inside the messenger. The command of a group and the
        // link of a direct chat are HSM's to issue, but the step says what to do with them in Telegram —
        // the same rule as for Slack, where a step is HSM's only when the person acts in HSM.
        [Fact]
        public void Telegram_EveryStepIsInTheMessenger() =>
            Assert.Equal(Sides(ChatSetupGuide.TelegramGroup.Count + ChatSetupGuide.TelegramDirect.Count, 0),
                TelegramSteps().Select(step => step.Side));

        [Fact]
        public void EveryStep_SaysSomething() =>
            Assert.All(AllSteps(), step => Assert.NotEmpty(step.Parts));

        [Fact]
        public void EveryLink_IsAbsoluteHttp()
        {
            var links = AllSteps().SelectMany(step => step.Parts).OfType<ChatSetupPart.Link>()
                .Select(link => new Uri(link.Address, UriKind.Absolute)).ToList();

            Assert.NotEmpty(links);
            Assert.All(links, link => Assert.Contains(link.Scheme, new[] { Uri.UriSchemeHttp, Uri.UriSchemeHttps }));
        }

        // The three FAQ links used to carry `?setln=uz`, which opened the page in Uzbek for everyone.
        [Fact]
        public void TelegramFaqLinks_LeaveTheLanguageToTheReader()
        {
            var addresses = TelegramSteps().SelectMany(step => step.Parts).OfType<ChatSetupPart.Link>()
                .Select(link => link.Address).ToList();

            Assert.Equal(new[]
            {
                "https://telegram.org/faq#q-how-do-i-create-a-group",
                "https://telegram.org/faq#q-how-do-i-add-more-members-what-39s-an-invite-link",
                "https://telegram.org/faq#q-can-i-assign-administrators",
            }, addresses);
        }


        private static IEnumerable<ChatSetupSide> Sides(int messenger, int hsm) =>
            Enumerable.Repeat(ChatSetupSide.Messenger, messenger).Concat(Enumerable.Repeat(ChatSetupSide.Hsm, hsm));

        private static IEnumerable<ChatSetupStep> TelegramSteps() => ChatSetupGuide.TelegramGroup.Concat(ChatSetupGuide.TelegramDirect);

        private static IEnumerable<ChatSetupStep> AllSteps() =>
            ChatSetupGuide.Slack.Concat(ChatSetupGuide.Mattermost).Concat(TelegramSteps());

        // A step as text, its markup kept and its characters read back as the browser shows them.
        private static string[] Render(IReadOnlyList<ChatSetupStep> steps, ChatSetupInvitationLinks invitationLinks = null) =>
            steps.Select(step => WebUtility.HtmlDecode(Html(step.ToHtml(BotName, invitationLinks)))).ToArray();

        private static string Html(IHtmlContent content)
        {
            using var writer = new StringWriter();

            content.WriteTo(writer, HtmlEncoder.Default);

            return writer.ToString();
        }
    }
}
