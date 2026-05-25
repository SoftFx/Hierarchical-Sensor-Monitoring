using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using Xunit;
using Xunit.Abstractions;

namespace HSMDataCollector.Tests
{
    internal sealed class SuiteSoakResourceSnapshot
    {
        private SuiteSoakResourceSnapshot(
            long managedAfterFullGc,
            long privateBytes,
            long workingSet,
            int handleCount,
            int threadCount,
            int tcpEstablished,
            int tcpTimeWait,
            int tcpTotal)
        {
            ManagedAfterFullGc = managedAfterFullGc;
            PrivateBytes = privateBytes;
            WorkingSet = workingSet;
            HandleCount = handleCount;
            ThreadCount = threadCount;
            TcpEstablished = tcpEstablished;
            TcpTimeWait = tcpTimeWait;
            TcpTotal = tcpTotal;
        }

        public long ManagedAfterFullGc { get; }

        public long PrivateBytes { get; }

        public long WorkingSet { get; }

        public int HandleCount { get; }

        public int ThreadCount { get; }

        public int TcpEstablished { get; }

        public int TcpTimeWait { get; }

        public int TcpTotal { get; }

        public static SuiteSoakResourceSnapshot Capture(IReadOnlyCollection<int> ports = null)
        {
            ForceFullGc();

            using (var process = Process.GetCurrentProcess())
            {
                process.Refresh();
                var tcp = CaptureTcp(ports);

                return new SuiteSoakResourceSnapshot(
                    GC.GetTotalMemory(forceFullCollection: false),
                    process.PrivateMemorySize64,
                    process.WorkingSet64,
                    GetHandleCount(process),
                    process.Threads.Count,
                    tcp.Established,
                    tcp.TimeWait,
                    tcp.Total);
            }
        }

        public static void WriteDelta(ITestOutputHelper output, string suite, SuiteSoakResourceSnapshot before, SuiteSoakResourceSnapshot after)
        {
            output.WriteLine(
                "{0}Resources; handles={1}->{2}; threads={3}->{4}; managedGc={5}->{6}; private={7}->{8}; workingSet={9}->{10}; tcpEstablished={11}->{12}; tcpTimeWait={13}->{14}; tcpTotal={15}->{16}",
                suite,
                before.HandleCount,
                after.HandleCount,
                before.ThreadCount,
                after.ThreadCount,
                before.ManagedAfterFullGc,
                after.ManagedAfterFullGc,
                before.PrivateBytes,
                after.PrivateBytes,
                before.WorkingSet,
                after.WorkingSet,
                before.TcpEstablished,
                after.TcpEstablished,
                before.TcpTimeWait,
                after.TcpTimeWait,
                before.TcpTotal,
                after.TcpTotal);
        }

        public static void AssertNoCriticalGrowth(SuiteSoakResourceSnapshot before, SuiteSoakResourceSnapshot after)
        {
            Assert.True(after.ThreadCount - before.ThreadCount < 80, "Thread count should stay bounded across the suite soak.");
            Assert.True(after.ManagedAfterFullGc - before.ManagedAfterFullGc < 128L * 1024 * 1024, "Managed memory after full GC should stay bounded across the suite soak.");
            Assert.True(after.PrivateBytes - before.PrivateBytes < 256L * 1024 * 1024, "Private bytes should stay bounded across the suite soak.");
            Assert.True(after.WorkingSet - before.WorkingSet < 256L * 1024 * 1024, "Working set should stay bounded across the suite soak.");

            if (before.HandleCount >= 0 && after.HandleCount >= 0)
                Assert.True(after.HandleCount - before.HandleCount < 500, "Process handle count should stay bounded across the suite soak.");
        }

        public static void AssertNoEstablishedConnections(SuiteSoakResourceSnapshot after)
        {
            Assert.True(after.TcpEstablished == 0, "No ESTABLISHED TCP connections to test ports should remain after the suite soak.");
            Assert.True(after.TcpTimeWait < 2000, "TIME_WAIT TCP connections to test ports should stay bounded after the suite soak.");
        }

        private static void ForceFullGc()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static int GetHandleCount(Process process)
        {
            try
            {
                return process.HandleCount;
            }
            catch
            {
                return -1;
            }
        }

        private static TcpCounts CaptureTcp(IReadOnlyCollection<int> ports)
        {
            if (ports == null || ports.Count == 0)
                return new TcpCounts(0, 0, 0);

            try
            {
                var connections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
                    .Where(c => ports.Contains(c.LocalEndPoint.Port) || ports.Contains(c.RemoteEndPoint.Port))
                    .ToArray();

                return new TcpCounts(
                    connections.Count(c => c.State == TcpState.Established),
                    connections.Count(c => c.State == TcpState.TimeWait),
                    connections.Length);
            }
            catch
            {
                return new TcpCounts(0, 0, 0);
            }
        }

        private sealed class TcpCounts
        {
            public TcpCounts(int established, int timeWait, int total)
            {
                Established = established;
                TimeWait = timeWait;
                Total = total;
            }

            public int Established { get; }

            public int TimeWait { get; }

            public int Total { get; }
        }
    }
}
