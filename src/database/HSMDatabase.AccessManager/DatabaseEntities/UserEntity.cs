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
        
        public List<KeyValuePair<string, byte>> ProductsRoles { get; set; }

        public UserNotificationSettingsEntity NotificationSettings { get; set; }

        public object TreeFilter { get; set; }
    }
}