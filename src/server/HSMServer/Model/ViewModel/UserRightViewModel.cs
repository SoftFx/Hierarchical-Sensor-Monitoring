using System;

namespace HSMServer.Model.ViewModel
{
    public class UserRightViewModel
    {
        public Guid ProductKey { get; set; }
        public int ProductRole { get; set; }
        public Guid UserId { get;set; }
        
        public UserRightViewModel() { }
    }
}
