using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.Windows.Process;
using HSMDataCollector.Options;
using HSMDataCollector.Threading;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.CrashTests.Host
{
    /// <summary>
    /// Process-isolated crash harness for the #1102 A1/A2 host-crash vectors. Each scenario wires a
    /// DataCollector (or the relevant internal component) with a deliberately hostile callback,
    /// triggers the vector, and prints <see cref="SurvivedSentinel"/> + exits 0 only if the process
    /// is still alive afterwards. While a crash vector is live, the injected exception escapes onto
    /// a ThreadPool / runtime-callback thread and the process dies before reaching the sentinel —
    /// which is exactly what the spawning xUnit test (CollectorCrashIsolationTests) detects.
    /// </summary>
    internal static class Program
    {
        private const string SurvivedSentinel = "HOST_SURVIVED";

        public static async Task<int> Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: HSMDataCollector.CrashTests.Host <scenario>");
                return 64;
            }

            switch (args[0])
            {
                case "a1-throwing-exception-subscriber":
                    await RunThrowingExceptionSubscriberAsync().ConfigureAwait(false);
                    break;

                case "a1-throwing-onerror":
                    await RunThrowingOnErrorAsync().ConfigureAwait(false);
                    break;

                case "a2-etw-malformed-counters":
                    return RunEtwMalformedCounters();

                case "a2-etw-time-in-gc-smoke":
                    return RunEtwTimeInGcSmoke();

                default:
                    Console.Error.WriteLine($"Unknown scenario '{args[0]}'.");
                    return 64;
            }

            Console.WriteLine(SurvivedSentinel);
            return 0;
        }

        /// <summary>
        /// #1102-A1, public API path: a monitoring sensor errors on every tick, and the host's
        /// ExceptionThrowing subscriber throws from the error notification. Unguarded, the second
        /// throw escapes ScheduledTask's onError invocation and the async-void dispatcher
        /// (CollectorScheduler.ExecuteQueuedTask) rethrows it on the ThreadPool -> process death.
        /// </summary>
        private static async Task RunThrowingExceptionSubscriberAsync()
        {
            using (var collector = new DataCollector(CreateOptions()))
            {
                var sensor = collector.CreateFunctionSensor<int>(
                    "crash/a1/exception-subscriber",
                    () => throw new InvalidOperationException("Injected sensor failure."),
                    new FunctionSensorOptions { PostDataPeriod = TimeSpan.FromMilliseconds(50) });

                ((SensorBase<NoDisplayUnit>)sensor).ExceptionThrowing += (path, error) =>
                    throw new InvalidOperationException("Injected host ExceptionThrowing handler failure.");

                await collector.Start().ConfigureAwait(false);

                // Let the send loop tick many times so the vector fires deterministically.
                await Task.Delay(TimeSpan.FromMilliseconds(1500)).ConfigureAwait(false);

                await collector.Stop().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// #1102-A1, dispatch-layer path: a scheduled action throws and the registered onError
        /// callback throws too. Internal API (InternalsVisibleTo) — this is the exact seam every
        /// monitoring sensor passes HandleException into.
        /// </summary>
        private static async Task RunThrowingOnErrorAsync()
        {
            var scheduler = new CollectorScheduler();
            try
            {
                scheduler.Schedule(
                    () => throw new InvalidOperationException("Injected scheduled action failure."),
                    TimeSpan.FromMilliseconds(10),
                    TimeSpan.FromMilliseconds(50),
                    onError: _ => throw new InvalidOperationException("Injected onError failure."));

                await Task.Delay(TimeSpan.FromMilliseconds(1000)).ConfigureAwait(false);
            }
            finally
            {
                scheduler.Dispose();
            }
        }

        /// <summary>
        /// #1102-A2: malformed EventCounters payloads delivered through a real EventSource ->
        /// EventListener dispatch, on a dedicated non-test thread (stand-in for the runtime-owned
        /// callback thread). Unguarded, the missing "Name"/"Mean" keys and the bad "Mean" type throw
        /// inside ProcessEventListener.OnEventWritten and kill the process. After the malformed
        /// events, a well-formed payload must still reach OnTimeInGC — the listener has to survive,
        /// not just swallow.
        /// </summary>
        private static int RunEtwMalformedCounters()
        {
            using (var listener = new ProcessEventListener())
            using (var source = new FakeRuntimeEventSource())
            using (var received = new ManualResetEventSlim(false))
            {
                listener.OnTimeInGC += _ => received.Set();

                var thread = new Thread(() =>
                {
                    source.WriteCountersWithoutName();
                    source.WriteCountersWithoutMean();
                    source.WriteCountersWithWrongMeanType();
                    source.WriteTimeInGc(7.5);
                });

                thread.IsBackground = false;
                thread.Start();
                thread.Join();

                if (!received.Wait(TimeSpan.FromSeconds(2)))
                {
                    Console.Error.WriteLine("Listener stopped delivering time-in-gc after malformed events.");
                    return 1;
                }
            }

            Console.WriteLine(SurvivedSentinel);
            return 0;
        }

        /// <summary>
        /// True ETW-path smoke: a well-formed time-in-gc counter payload travels through the real
        /// EventSource -> EventListener -> OnEventWritten pipeline and reaches the OnTimeInGC event.
        /// Guards the A2 refactor against breaking the happy path.
        /// </summary>
        private static int RunEtwTimeInGcSmoke()
        {
            using (var listener = new ProcessEventListener())
            using (var source = new FakeRuntimeEventSource())
            using (var received = new ManualResetEventSlim(false))
            {
                double value = double.NaN;
                listener.OnTimeInGC += v =>
                {
                    value = v;
                    received.Set();
                };

                source.WriteTimeInGc(12.5);

                if (!received.Wait(TimeSpan.FromSeconds(2)) || Math.Abs(value - 12.5) > double.Epsilon)
                {
                    Console.Error.WriteLine($"time-in-gc payload was not delivered (received: {value}).");
                    return 1;
                }
            }

            Console.WriteLine(SurvivedSentinel);
            return 0;
        }

        private static CollectorOptions CreateOptions() => new CollectorOptions
        {
            AccessKey = "crash-host-key",
            ClientName = "crash-host-client",
            ComputerName = "crash-host",
            Module = "crash-host-module",
            DataSender = new NoopDataSender(),
            PackageCollectPeriod = TimeSpan.FromMilliseconds(50),
            RequestTimeout = TimeSpan.FromSeconds(1)
        };
    }
}
