using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class UserEntity
    {
        public Guid Id { get; set; }
        public bool IsAdmin { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string CertificateThumbprint { get; set; }
        public string CertificateFileName { get; set; }
        public List<KeyValuePair<string, byte>> ProductsRoles { get; set; }

        public byte TelegramMessagesMinStatus { get; set; }
        public bool EnableTelegramMessages { get; set; } = true;
        public int TelegramMessagesDelay { get; set; } = 10;
    }
}