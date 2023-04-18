using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class UserEntity
    {
        public List<KeyValuePair<string, byte>> ProductsRoles { get; set; } = new();

        public Dictionary<string, byte> FolderRoles { get; set; } = new();


        public NotificationSettingsEntity NotificationSettings { get; set; }

        public object TreeFilter { get; set; }


        public Guid Id { get; set; }

        public bool IsAdmin { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }
    }
}