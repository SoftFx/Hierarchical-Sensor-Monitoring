namespace HSMServer.Model.Notifications
{
    // The two links the Telegram setup modal (_NewChatHelpModal) puts behind the words of a setup
    // step that stand for an invitation. HSM issues the invitation at the moment of use: the group
    // command is fetched by the link's own request, the invitation link is the route of the action
    // that opens the bot. Both carry the tooltip and the class as given — while the bot is not
    // running, the reason and the greyed-out look.
    internal sealed record ChatSetupInvitationLinks(string Title, string Class, string GroupCommandHref, string InvitationHref);
}
