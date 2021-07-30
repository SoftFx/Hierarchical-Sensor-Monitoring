using HSMServer.DataLayer;
using Microsoft.Extensions.Logging;
using System;

namespace HSMServer.Registration
{
    public class RegistrationTicketManager : IRegistrationTicketManager
    {
        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly ILogger<RegistrationTicketManager> _logger;
        public RegistrationTicketManager(IDatabaseAdapter databaseAdapter, 
            ILogger<RegistrationTicketManager> logger)
        {
            _databaseAdapter = databaseAdapter;
            _logger = logger;
        }

        public RegistrationTicket GetTicket(Guid id)
        {
            return _databaseAdapter.ReadRegistrationTicket(id);
        }

        public void AddTicket(RegistrationTicket ticket)
        {
            _databaseAdapter.WriteRegistrationTicket(ticket);
        }

        public void RemoveTicket(Guid id)
        {
            _databaseAdapter.RemoveRegistrationTicket(id);
        }
    }
}
