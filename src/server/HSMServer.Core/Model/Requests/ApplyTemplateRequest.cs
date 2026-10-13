using System;
using System.Collections.Generic;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.SensorsUpdatesQueue;


namespace HSMServer.Core.Model.Requests
{
    internal sealed record ApplyTemplateRequest(Guid SensorId, AlertTemplateModel Template) : IUpdateRequest
    {
        public AlertTemplateModel AlertTemplateModel { get; } = Template;

        // Set by the queue handler when the per-sensor apply failed (#1394):
        // the queue's TaskResult carries only THROWN exceptions, while
        // TryUpdateSensor catches the DB failure into a string. Read by
        // AddAlertTemplateAsync after its await, which establishes the
        // happens-before edge.
        public string Error { get; set; }

        // #1401: per-policy VALIDATION rejections of an apply that PERSISTED
        // (TryUpdateSensor returned true with a non-empty error) — one entry
        // per rejected policy, populated on the queue thread and read by
        // AddAlertTemplateAsync after its await (same happens-before edge as
        // Error). Per-request by construction: never shared mutable state.
        public List<TemplateApplyWarning> Warnings { get; } = [];
    }
}
