using System;
using HSMServer.Core.Model;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record ApplyTemplateRequest(Guid SensorId, AlertTemplateModel Template) : IUpdateRequest
    {
        public AlertTemplateModel AlertTemplateModel { get; } = Template;

        // Set by the queue handler when the per-sensor apply failed (#1394):
        // the queue's TaskResult carries only THROWN exceptions, and
        // TryUpdateSensor catches the DB failure into a string — without this
        // channel the template save reported success while the alert existed
        // only in memory until the next restart. Read by AddAlertTemplateAsync
        // after its await, which establishes the happens-before edge.
        public string Error { get; set; }
    }
}
