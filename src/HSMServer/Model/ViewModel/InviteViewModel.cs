using HSMServer.Authentication;
using System;

namespace HSMServer.Model.ViewModel
{
    public class InviteViewModel
    {
        public string ProductKey { get; set; }
        public string Email { get; set; }
        public string ExpirationDate { get; set; }
        public string Role { get; set; } 
    }
}
