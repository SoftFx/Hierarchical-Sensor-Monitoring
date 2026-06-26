using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace HSMDataCollector.DefaultSensors.Windows.Process
{
    internal sealed class ProcessEventListener : EventListener
    {
        private const string RuntimeSourceName = "System.Runtime";
        private const string CountersEventName = "EventCounters";
        private const string TimeInGcCounterName = "time-in-gc";

        public event Action<double> OnTimeInGC;

        protected override void OnEventSourceCreated(EventSource source)
        {
            // Runs while another component's EventSource is being constructed, possibly on a
            // runtime-owned thread — a throw here must not escape (#1102-A2).
            try
            {
                if (RuntimeSourceName.Equals(source?.Name, StringComparison.Ordinal))
                    EnableEvents(source, EventLevel.Critical, EventKeywords.All, null);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"{nameof(ProcessEventListener)} failed to enable events for source '{source?.Name}': {ex}");
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // EventListener callbacks run on whatever thread writes the event, including
            // runtime-owned threads — nothing may escape (#1102-A2).
            try
            {
                ProcessEventCounters(eventData?.EventName, eventData?.Payload);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"{nameof(ProcessEventListener)} failed to process event '{eventData?.EventName}': {ex}");
            }
        }

        // Callback body extracted from OnEventWritten for direct adversarial testing (#1103):
        // EventName can be null (a known EventSource case), counter payload dictionaries can miss
        // keys or carry unexpected value types.
        internal void ProcessEventCounters(string eventName, IEnumerable<object> payload)
        {
            if (!CountersEventName.Equals(eventName, StringComparison.Ordinal) || payload == null)
                return;

            foreach (var item in payload)
            {
                if (item is IDictionary<string, object> eventPayload)
                {
                    UpdateTimeInGc(eventPayload);
                }
            }
        }


        private void UpdateTimeInGc(IDictionary<string, object> eventPayload)
        {
            if (!eventPayload.TryGetValue("Name", out var name) || !TimeInGcCounterName.Equals(name as string, StringComparison.Ordinal))
                return;

            if (eventPayload.TryGetValue("Mean", out var mean) && mean is double value)
                OnTimeInGC?.Invoke(value);
        }
    }
}
