using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Managers
{
    public class AlertMessage
    {
        public List<AlertResult> Alerts { get; }

        public Guid FolderId { get; private set; }


        public AlertMessage ApplyFolder(ProductModel product)
        {
            FolderId = product.FolderId.Value;

            return this;
        }
    }


    public class ScheduleAlertMessage : AlertMessage
    {
        public DateTime MessageDate { get; }


        public ScheduleAlertMessage() { }

        internal ScheduleAlertMessage(DateTime messageDate) 
        {
            MessageDate = messageDate;
        }
    }
}