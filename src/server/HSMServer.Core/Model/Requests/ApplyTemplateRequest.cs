using System;
using System.Collections.Generic;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.TableOfChanges;


namespace HSMServer.Core.Model.Requests
{
    // The optional Initiator (#1610): the administration service stamps the
    // request's undo group onto the AlertTemplate-type initiator, so every
    // fan-out policy record the apply writes links back to the template
    // write's rollback group. Null (the default) keeps the canonical
    // unstamped AlertTemplate initiator of the pre-journaled callers.
    //
    // The optional IsDisabledChangedAlertIds (#1077 review): the template
    // alert ids whose IsDisabled differs from the previously stored version
    // of the same template — computed once per save in AddAlertTemplateAsync
    // (null on the sensor-creation path, the only apply that runs without a
    // save to diff; restore and change-set rollback also go through
    // AddAlertTemplateAsync and DO get the diff). Read by
    // ApplyTemplateToSensor to decide whether a
    // preserved operator toggle's divergence carries new information worth
    // reporting on THIS save.
    internal sealed record ApplyTemplateRequest(Guid SensorId, AlertTemplateModel Template,
        InitiatorInfo Initiator = null, HashSet<Guid> IsDisabledChangedAlertIds = null) : IUpdateRequest
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
