using HSMServer.DataLayer;
using Microsoft.Extensions.Logging;
using System;

namespace HSMServer.Registration
{
    public class RegistrationTicketManager : IRegistrationTicketManager
    {
        private readonly IDatabaseWorker _databaseWorker;
        private readonly ILogger<RegistrationTicketManager> _logger;
        public RegistrationTicketManager(IDatabaseWorker databaseWorker, 
            ILogger<RegistrationTicketManager> logger)
        {
            _databaseWorker = databaseWorker;
            _logger = logger;
        }

        public RegistrationTicket GetTicket(Guid id)
        {
            return _databaseWorker.ReadRegistrationTicket(id);
        }

        public void AddTicket(RegistrationTicket ticket)
        {
            _databaseWorker.WriteRegistrationTicket(ticket);
        }

        public void RemoveTicket(Guid id)
        {
            _databaseWorker.RemoveRegistrationTicket(id);
        }
    }
}
