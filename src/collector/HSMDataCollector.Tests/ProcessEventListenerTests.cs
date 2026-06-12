using System;
using System.Collections.Generic;
using HSMDataCollector.DefaultSensors.Windows.Process;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// Adversarial matrix for the #1102-A2 vector: ProcessEventListener's callback body must accept
    /// every malformed shape EventSource can deliver (null EventName, payload dictionaries with
    /// missing keys or unexpected value types) without throwing — EventListener callbacks can run on
    /// runtime-owned threads where an escaping exception kills the host process. The true
    /// EventSource -> EventListener dispatch path is covered by the process-isolated
    /// a2-etw-malformed-counters / a2-etw-time-in-gc-smoke scenarios (CollectorCrashIsolationTests).
    /// </summary>
    public sealed class ProcessEventListenerTests
    {
        [Fact]
        public void Null_event_name_does_not_throw()
        {
            using (var listener = new ProcessEventListener())
            {
                var exception = Record.Exception(() => listener.ProcessEventCounters(null, new object[0]));

                Assert.Null(exception);
            }
        }

        [Fact]
        public void Null_payload_does_not_throw()
        {
            using (var listener = new ProcessEventListener())
            {
                var exception = Record.Exception(() => listener.ProcessEventCounters("EventCounters", null));

                Assert.Null(exception);
            }
        }

        [Fact]
        public void Payload_without_name_key_does_not_throw()
        {
            using (var listener = new ProcessEventListener())
            {
                var payload = new Dictionary<string, object> { ["DisplayName"] = "no name key" };

                var exception = Record.Exception(() => listener.ProcessEventCounters("EventCounters", new object[] { payload }));

                Assert.Null(exception);
            }
        }

        [Fact]
        public void Payload_with_null_name_value_does_not_throw()
        {
            using (var listener = new ProcessEventListener())
            {
                var payload = new Dictionary<string, object> { ["Name"] = null };

                var exception = Record.Exception(() => listener.ProcessEventCounters("EventCounters", new object[] { payload }));

                Assert.Null(exception);
            }
        }

        [Fact]
        public void Time_in_gc_payload_without_mean_key_does_not_throw()
        {
            using (var listener = new ProcessEventListener())
            {
                var payload = new Dictionary<string, object> { ["Name"] = "time-in-gc" };

                var exception = Record.Exception(() => listener.ProcessEventCounters("EventCounters", new object[] { payload }));

                Assert.Null(exception);
            }
        }

        [Fact]
        public void Time_in_gc_payload_with_non_double_mean_does_not_throw_and_is_skipped()
        {
            using (var listener = new ProcessEventListener())
            {
                var received = new List<double>();
                listener.OnTimeInGC += received.Add;

                var payload = new Dictionary<string, object> { ["Name"] = "time-in-gc", ["Mean"] = "not-a-double" };

                var exception = Record.Exception(() => listener.ProcessEventCounters("EventCounters", new object[] { payload }));

                Assert.Null(exception);
                Assert.Empty(received);
            }
        }

        [Fact]
        public void Non_dictionary_payload_items_are_ignored()
        {
            using (var listener = new ProcessEventListener())
            {
                var exception = Record.Exception(() =>
                    listener.ProcessEventCounters("EventCounters", new object[] { null, "text", 42 }));

                Assert.Null(exception);
            }
        }

        [Fact]
        public void Malformed_payload_does_not_break_subsequent_delivery()
        {
            using (var listener = new ProcessEventListener())
            {
                var received = new List<double>();
                listener.OnTimeInGC += received.Add;

                listener.ProcessEventCounters("EventCounters", new object[] { new Dictionary<string, object> { ["DisplayName"] = "no name" } });
                listener.ProcessEventCounters("EventCounters", new object[] { new Dictionary<string, object> { ["Name"] = "time-in-gc", ["Mean"] = 12.5 } });

                Assert.Equal(new[] { 12.5 }, received);
            }
        }

        [Fact]
        public void Well_formed_time_in_gc_payload_raises_event()
        {
            using (var listener = new ProcessEventListener())
            {
                var received = new List<double>();
                listener.OnTimeInGC += received.Add;

                listener.ProcessEventCounters("EventCounters", new object[] { new Dictionary<string, object> { ["Name"] = "time-in-gc", ["Mean"] = 42.25 } });

                Assert.Equal(new[] { 42.25 }, received);
            }
        }

        [Fact]
        public void Other_counter_names_are_ignored()
        {
            using (var listener = new ProcessEventListener())
            {
                var received = new List<double>();
                listener.OnTimeInGC += received.Add;

                listener.ProcessEventCounters("EventCounters", new object[] { new Dictionary<string, object> { ["Name"] = "cpu-usage", ["Mean"] = 99.0 } });

                Assert.Empty(received);
            }
        }

        [Fact]
        public void Other_event_names_are_ignored()
        {
            using (var listener = new ProcessEventListener())
            {
                var received = new List<double>();
                listener.OnTimeInGC += received.Add;

                listener.ProcessEventCounters("EventSourceMessage", new object[] { new Dictionary<string, object> { ["Name"] = "time-in-gc", ["Mean"] = 1.0 } });

                Assert.Empty(received);
            }
        }
    }
}
