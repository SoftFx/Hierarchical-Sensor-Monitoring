using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Managers
{
    public class AlertMessage
    {
        public List<AlertResult> Alerts { get; } = [];

        public Guid SensorId { get; }


        public Guid FolderId { get; private set; }


        public bool IsEmpty => Alerts.Count == 0;


        internal AlertMessage(Guid sensorId)
        {
            SensorId = sensorId;
        }

        internal AlertMessage(Guid sensorId, List<AlertResult> alerts) : this(sensorId)
        {
            Alerts = alerts;
        }


        public AlertMessage ApplyFolder(ProductModel product)
        {
            FolderId = product.FolderId.Value;

            return this;
        }
    }


    public sealed class ScheduleAlertMessage : AlertMessage
    {
        public ScheduleAlertMessage() : base(Guid.Empty) { }

        internal ScheduleAlertMessage(Guid sensorId) : base(sensorId) { }
    }
}