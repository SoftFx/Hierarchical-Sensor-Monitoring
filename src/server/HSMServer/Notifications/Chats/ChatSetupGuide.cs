using System.Collections.Generic;

namespace HSMServer.Notifications.Chats
{
    /// <summary>
    /// The steps of setting up a notification chat — ONE source for the web UI's help modals
    /// (the Slack and Mattermost tabs of the chat form, the Telegram setup modal) and for the guide an
    /// agent reads through the management API (#1831). Until #1827 they lived only as Razor markup,
    /// which an agent cannot read, and a second copy would have drifted from the first.
    /// The words are the modals' as they were. The Telegram FAQ links lost their language parameter,
    /// which opened the page in Uzbek for everyone. What surrounds the steps in a modal — its title,
    /// introduction and closing note — stays in the modal.
    /// </summary>
    internal static class ChatSetupGuide
    {
        // Without a language parameter: the FAQ then opens in the reader's own language.
        private const string TelegramFaq = "https://telegram.org/faq";

        /// <summary>
        /// A Slack incoming webhook — a private URL that lets HSM post to one Slack channel. The first
        /// fourteen steps create the URL in Slack; the last three hand it to HSM.
        /// </summary>
        public static readonly IReadOnlyList<ChatSetupStep> Slack = new[]
        {
            Messenger("You need Slack workspace admin permissions, or permission to install apps."),
            Messenger("Open ", Code("api.slack.com/apps"), "."),
            Messenger("Click ", Code("Create New App"), "."),
            Messenger("Choose ", Code("From scratch"), "."),
            Messenger("Enter an app name, for example ", Code("my space"), "."),
            Messenger("Select the Slack workspace where alerts should be sent."),
            Messenger("Click ", Code("Create App"), "."),
            Messenger("In the left menu, open ", Code("Incoming Webhooks"), " under ", Code("Features"), "."),
            Messenger("Turn on ", Code("Activate Incoming Webhooks"), ". The switch should change from Off to On."),
            Messenger("Click ", Code("Add New Webhook to Workspace"), " at the bottom of the page."),
            Messenger("Slack will ask which channel should receive messages. Select the channel. The channel must already exist in Slack before this step."),
            Messenger("Click ", Code("Allow"), "."),
            Messenger("Slack will generate a URL like ", Code("https://hooks.slack.com/services/..."), "."),
            Messenger("Copy this URL."),
            Hsm("Paste it into the ", Code("Slack webhook URL"), " field in HSM."),
            Hsm("Save the chat."),
            Hsm("In HSM, use ", Code("Send test Slack message"), " to confirm that Slack receives the message."),
        };

        /// <summary>
        /// A Mattermost incoming webhook — a private URL that lets HSM post to one Mattermost channel.
        /// The first ten steps create the URL on the Mattermost server; the last two hand it to HSM.
        /// </summary>
        public static readonly IReadOnlyList<ChatSetupStep> Mattermost = new[]
        {
            Messenger("You need a Mattermost account with permission to create integrations, or ask a System Admin for help."),
            Messenger("Open your Mattermost site in the browser."),
            Messenger("Open the main menu (top-left) → ", Code("Integrations"), " → ", Code("Incoming Webhooks"), "."),
            Messenger("If the page says incoming webhooks are disabled, ask a System Admin to enable them: ",
                Code("System Console"), " → ", Code("Integrations"), " → ", Code("Custom Integrations"), " → turn on ", Code("Enable Incoming Webhooks"), "."),
            Messenger("Click ", Code("Add Incoming Webhook"), "."),
            Messenger("Select the channel that should receive alert messages. The channel must already exist."),
            Messenger("Optionally set a display name (e.g. ", Code("HSM alerts"), ") and description."),
            Messenger("Click ", Code("Save"), "."),
            Messenger("Mattermost will display a URL like ", Code("https://your-mattermost.example/hooks/xxxx"), "."),
            Messenger("Copy this URL."),
            Hsm("Paste it into the ", Code("Mattermost webhook URL"), " field in HSM."),
            Hsm("Save the chat."),
        };

        /// <summary>
        /// Binding a Telegram group: the bot joins the group and receives the /start command HSM issues.
        /// Every step is the person's, in Telegram; the command is fetched where its words are clicked.
        /// The bot is named in brackets without the @ of a mention, as the modal always showed it.
        /// </summary>
        public static readonly IReadOnlyList<ChatSetupStep> TelegramGroup = new[]
        {
            Messenger(Link("Create", TelegramFaq + "#q-how-do-i-create-a-group"), " group chat in Telegram."),
            Messenger(Link("Add", TelegramFaq + "#q-how-do-i-add-more-members-what-39s-an-invite-link"), " Telegram Bot (", new ChatSetupPart.BotName(), ") to group chat."),
            Messenger(Link("Give Admin right", TelegramFaq + "#q-can-i-assign-administrators"), " to Telegram Bot (", new ChatSetupPart.BotName(), ")."),
            Messenger("Send ", new ChatSetupPart.StartCommand("this message"), " from clipboard to group chat."),
        };

        /// <summary>
        /// Binding a Telegram direct chat: the person follows the invitation link HSM issues and starts
        /// the bot.
        /// </summary>
        public static readonly IReadOnlyList<ChatSetupStep> TelegramDirect = new[]
        {
            Messenger("Click ", new ChatSetupPart.InvitationLink("invitation link"), " and press ", new ChatSetupPart.Bold("Start"),
                " button at opened direct chat with Telegram Bot (", new ChatSetupPart.BotName(), ")."),
        };


        private static ChatSetupStep Messenger(params ChatSetupPart[] parts) => new(ChatSetupSide.Messenger, parts);

        private static ChatSetupStep Hsm(params ChatSetupPart[] parts) => new(ChatSetupSide.Hsm, parts);

        private static ChatSetupPart Code(string value) => new ChatSetupPart.Code(value);

        private static ChatSetupPart Link(string value, string address) => new ChatSetupPart.Link(value, address);
    }
}
