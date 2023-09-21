using System;

namespace HSMServer.Model.DataAlerts
{
    public sealed class AlertImportViewModel
    {
        public Guid NodeId { get; set; }

        public string FileContent { get; set; }
    }
}
