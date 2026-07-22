using System;

namespace HSMServer.Model.Notifications
{
    // Discriminates the two Telegram invitation modes used by _NewChatHelpModal:
    //   - ChatId set:    EditChat flow — bind Telegram to an existing Chat record (in-place).
    //   - FolderId set:  folder-scoped flow — create a brand-new chat and bind it to the folder.
    // Exactly one of the two should be set per invocation.
    public sealed record NewChatHelpViewModel(Guid? ChatId = null, Guid? FolderId = null);
}
