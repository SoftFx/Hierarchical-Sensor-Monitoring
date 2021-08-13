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
            MigrateTicketsToNewDatabase();
        }

        /// <summary>
        /// This method MUST be called when update from 2.1.4 or lower to 2.1.5 or higher
        /// </summary>
        private void MigrateTicketsToNewDatabase()
        {
            var currentTickets = _databaseAdapter.GetAllTicketsOld();
            foreach (var ticket in currentTickets)
            {
                _databaseAdapter.WriteRegistrationTicket(ticket);
            }
        }

        public RegistrationTicket GetTicket(Guid id)
        {
            //return _databaseAdapter.ReadRegistrationTicketOld(id);
            return _databaseAdapter.ReadRegistrationTicket(id);
        }

        public void AddTicket(RegistrationTicket ticket)
        {
            //_databaseAdapter.WriteRegistrationTicketOld(ticket);
            _databaseAdapter.WriteRegistrationTicket(ticket);
        }

        public void RemoveTicket(Guid id)
        {
            //_databaseAdapter.RemoveRegistrationTicketOld(id);
            _databaseAdapter.RemoveRegistrationTicket(id);
        }
    }
}
