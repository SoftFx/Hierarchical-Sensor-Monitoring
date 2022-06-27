using System;
using HSMServer.Core.Model;

namespace HSMServer.Core.Registration
{
    public interface IRegistrationTicketManager
    {
        public RegistrationTicket GetTicket(Guid id);
        public void AddTicket(RegistrationTicket ticket);
        public void RemoveTicket(Guid id);
    }
}
