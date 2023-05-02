using HSMServer.Core.Registration;
using System;

namespace HSMServer.Registration
{
    public interface IRegistrationTicketManager
    {
        public RegistrationTicket GetTicket(Guid id);
        public void AddTicket(RegistrationTicket ticket);
        public void RemoveTicket(Guid id);
    }
}
