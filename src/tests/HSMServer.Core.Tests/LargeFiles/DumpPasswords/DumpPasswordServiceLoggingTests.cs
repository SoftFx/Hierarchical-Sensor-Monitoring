using HSMServer.Core.Cache;
using HSMServer.Core.Journal;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.DumpPasswords
{
    // Swaps the process-global NLog configuration, so it runs in the exclusive collection (see AlertScheduleNLogIsolatedCollection).
    [Collection("NLog isolated")]
    public sealed class DumpPasswordServiceLoggingTests : IDisposable
    {
        private const string Typed = "typed-secret-value-0001";
        private const string Rejected = "rejected-secret-value-0002";

        private readonly MemoryTarget _memory = new() { Layout = "${level} ${logger} ${message} ${exception:format=tostring}" };
        private readonly LoggingConfiguration _previous = LogManager.Configuration;
        private readonly DumpPasswordTestContext _context = new();


        public DumpPasswordServiceLoggingTests()
        {
            LogManager.Setup().LoadConfiguration(cfg => cfg.ForLogger().WriteTo(_memory));
        }

        public void Dispose()
        {
            _context.Dispose();
            LogManager.Configuration = _previous;
        }


        [Fact]
        public void No_Log_Line_And_No_Exception_Carries_A_Password()
        {
            var exceptions = new List<Exception>();
            var service = _context.CreateService(TimeSpan.Zero);
            var id = _context.Product.Id;
            var generated = service.Generate();
            var passwords = new[] { Typed, Rejected, generated };

            service.PasswordChanged += (_, _) => throw new InvalidOperationException("subscriber failed");

            service.SetPassword(id, Typed, generated: false, InitiatorInfo.AsUser("manager"));

            // A journal that throws: its failure is logged.
            _context.Journal.Setup(j => j.AddRecord(It.IsAny<JournalRecordModel>())).Throws(new InvalidOperationException("journal down"));
            service.SetPassword(id, generated, generated: true, InitiatorInfo.System);

            // Writes that fail: logged and thrown.
            _context.Database.ShouldFailDumpPasswordOp = _ => true;

            Capture(exceptions, () => service.SetPassword(id, Rejected, generated: false, InitiatorInfo.AsUser("manager")));
            Capture(exceptions, () => service.MarkRetired(id, 1, _context.Now));
            Capture(exceptions, () => service.RemoveProduct(id));

            _context.Cache.Raise(c => c.ChangeProductEvent += null, _context.Product, ActionType.Delete);

            _context.Database.ShouldFailDumpPasswordOp = null;
            service.MarkRetired(id, 1, _context.Now);

            _context.Database.ShouldFailDumpPasswordOp = _ => true;
            Capture(exceptions, () => service.RemoveExpired(_context.Now.AddDays(1), new HashSet<(Guid, int)>()));

            _context.Database.ShouldFailDumpPasswordOp = null;
            service.RemoveExpired(_context.Now.AddDays(1), new HashSet<(Guid, int)> { (id, 1) }); // kept: an archive carries it
            service.RemoveExpired(_context.Now.AddDays(1), new HashSet<(Guid, int)>());
            service.RemoveProduct(id);


            // A reload logs too.
            _context.CreateService();

            var logs = _memory.Logs.ToArray();

            Assert.Equal(4, exceptions.Count);
            Assert.Contains(logs, l => l.Contains("could not be stored"));
            Assert.Contains(logs, l => l.Contains("could not be removed"));
            Assert.Contains(logs, l => l.Contains("journal record"));
            Assert.Contains(logs, l => l.Contains("handler"));
            Assert.Contains(logs, l => l.Contains("version 1") && l.Contains("an archive still carries it"));

            foreach (var password in passwords)
            {
                Assert.DoesNotContain(logs, l => l.Contains(password));
                Assert.DoesNotContain(exceptions, e => e.ToString().Contains(password));
            }
        }


        private static void Capture(List<Exception> exceptions, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        }
    }
}
