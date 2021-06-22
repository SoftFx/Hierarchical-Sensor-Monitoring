using HSMServer.Authentication;
using System;
using System.Collections.Generic;

namespace HSMServer.DataLayer.Model
{
    public class Product
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public DateTime DateAdded { get; set; }
        //public Guid ManagerId { get; set; }
        public List<KeyValuePair<Guid, UserRoleEnum>> UsersRights { get; set; }
        public List<ExtraProductKey> ExtraKeys { get; set; }
    }
}
