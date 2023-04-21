using HSMServer.Model.Authentication;
using System;
using System.Text.Json.Serialization;

namespace HSMServer.Model.ViewModel
{
    public class UserRightViewModel
    {
        public Guid UserId { get; set; }

        public Guid EntityId { get; set; }

        public int ProductRole { get; set; }

        [JsonIgnore]
        public ProductRoleEnum Role => (ProductRoleEnum)ProductRole;


        public UserRightViewModel() { }
    }
}
