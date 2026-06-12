using System.Diagnostics.Tracing;

namespace HSMDataCollector.CrashTests.Host
{
    /// <summary>
    /// Self-describing EventSource named "System.Runtime" — the name ProcessEventListener subscribes
    /// to. Writes "EventCounters" events with the same nested-payload shape the real EventCounter
    /// infrastructure uses (a wrapper type with a single <c>Payload</c> property), so the listener
    /// receives <c>eventData.Payload[i]</c> as <c>IDictionary&lt;string, object&gt;</c> through the
    /// genuine EventSource -> EventListener dispatch path.
    /// </summary>
    // The explicit Guid avoids colliding with the real RuntimeEventSource: EventSource GUIDs are
    // derived from the name, and registering a second source with the runtime's GUID fails with
    // "An instance of EventSource with Guid ... already exists" (the source comes up disabled).
    [EventSource(Name = "System.Runtime", Guid = "5fbacf8c-d0d2-4f49-b466-fb1568bb7eab")]
    internal sealed class FakeRuntimeEventSource : EventSource
    {
        private static readonly EventSourceOptions CounterOptions = new EventSourceOptions { Level = EventLevel.LogAlways };

        public FakeRuntimeEventSource()
            : base(EventSourceSettings.EtwSelfDescribingEventFormat)
        {
        }

        public void WriteTimeInGc(double mean) =>
            Write("EventCounters", CounterOptions, new GoodPayloadWrapper { Payload = new GoodPayload { Name = "time-in-gc", Mean = mean } });

        /// <summary>Counter payload without the "Name" key — the documented KeyNotFound vector.</summary>
        public void WriteCountersWithoutName() =>
            Write("EventCounters", CounterOptions, new NoNamePayloadWrapper { Payload = new NoNamePayload { DisplayName = "no name key" } });

        /// <summary>"time-in-gc" payload without the "Mean" key — the second KeyNotFound vector.</summary>
        public void WriteCountersWithoutMean() =>
            Write("EventCounters", CounterOptions, new NoMeanPayloadWrapper { Payload = new NoMeanPayload { Name = "time-in-gc" } });

        /// <summary>"Mean" present but not a double — the InvalidCast vector.</summary>
        public void WriteCountersWithWrongMeanType() =>
            Write("EventCounters", CounterOptions, new WrongMeanPayloadWrapper { Payload = new WrongMeanPayload { Name = "time-in-gc", Mean = "not-a-double" } });

        [EventData]
        private sealed class GoodPayloadWrapper
        {
            public GoodPayload Payload { get; set; }
        }

        [EventData]
        private sealed class GoodPayload
        {
            public string Name { get; set; }

            public double Mean { get; set; }
        }

        [EventData]
        private sealed class NoNamePayloadWrapper
        {
            public NoNamePayload Payload { get; set; }
        }

        [EventData]
        private sealed class NoNamePayload
        {
            public string DisplayName { get; set; }
        }

        [EventData]
        private sealed class NoMeanPayloadWrapper
        {
            public NoMeanPayload Payload { get; set; }
        }

        [EventData]
        private sealed class NoMeanPayload
        {
            public string Name { get; set; }
        }

        [EventData]
        private sealed class WrongMeanPayloadWrapper
        {
            public WrongMeanPayload Payload { get; set; }
        }

        [EventData]
        private sealed class WrongMeanPayload
        {
            public string Name { get; set; }

            public string Mean { get; set; }
        }
    }
}
