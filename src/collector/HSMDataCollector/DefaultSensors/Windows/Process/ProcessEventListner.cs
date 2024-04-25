using System.Collections.Generic;
using System.Diagnostics.Tracing;

namespace HSMDataCollector.DefaultSensors.Windows.Process
{
    internal sealed class ProcessEventListener : EventListener
    {
        public double TimeInGC { get; private set; }


        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name.Equals("System.Runtime"))
            {
                EnableEvents(source, EventLevel.Critical, EventKeywords.All, null);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName.Equals("EventCounters"))
            {
                for (int i = 0; i < eventData.Payload.Count; i++)
                {
                    if (eventData.Payload[i] is IDictionary<string, object> eventPayload)
                    {
                        UpdateTimeInGc(eventPayload);
                    }
                }
            }
        }


        private void UpdateTimeInGc(IDictionary<string, object> eventPayload)
        {
            if (eventPayload["Name"].ToString() != "time-in-gc")
                return;

            TimeInGC = (double)eventPayload["Mean"];
        }
    }
}
