using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Notifications.Chats
{
    /// <summary>
    /// The name rule of a chat write (#1829) — ONE rule behind the web form and the
    /// management API, where each used to have its own half: the name is required, at
    /// most 100 characters, and unique per server ignoring case, a chat excluded from the
    /// check against itself. Alert import resolves chats by name, and the read DTO always
    /// promised the uniqueness that nothing enforced.
    /// </summary>
    internal static class ChatNameValidator
    {
        public const int NameMaxLength = 100;

        // The web form's messages (ChatViewModel's DataAnnotations), shared so the two
        // surfaces cannot drift apart.
        public const string NameRequiredMessage = "Name is required";
        public const string NameLengthMessage = "Name length should be less than 100.";
        public const string NameDuplicateMessage = "Chat with the same name already exists.";


        /// <summary>
        /// The message of the first rule the name breaks, or null when the name is valid.
        /// The name arrives trimmed (the write engine trims it).
        /// </summary>
        public static string FindError(string name, Guid chatId, IEnumerable<Chat> existing)
        {
            if (string.IsNullOrEmpty(name))
                return NameRequiredMessage;

            if (name.Length > NameMaxLength)
                return NameLengthMessage;

            return existing.Any(chat => chat.Id != chatId && string.Equals(chat.Name, name, StringComparison.OrdinalIgnoreCase))
                ? NameDuplicateMessage
                : null;
        }
    }
}
