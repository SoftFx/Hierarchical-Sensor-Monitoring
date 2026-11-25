using HSMServer.Core.Cache;
using HSMServer.Core.Journal;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.DumpPasswords
{
    // A real configuration database in a temp folder, behind FailingDatabaseCore for the write failures.
    internal sealed class DumpPasswordTestContext : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hsm-dump-passwords-{Guid.NewGuid():N}");
        private readonly List<DumpPasswordService> _services = [];

        internal DatabaseCoreManager Manager { get; }

        internal FailingDatabaseCore Database { get; }

        internal Mock<ITreeValuesCache> Cache { get; } = new();

        internal Mock<IJournalService> Journal { get; } = new();

        internal ConcurrentQueue<JournalRecordModel> JournalRecords { get; } = new();

        internal ProductModel Product { get; } = new("Dumps product");

        internal DateTime Now { get; set; } = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);


        internal DumpPasswordTestContext()
        {
            Manager = new DatabaseCoreManager(_folder);
            Database = new FailingDatabaseCore(Manager.DatabaseCore, _ => false);

            Cache.Setup(c => c.GetProduct(Product.Id)).Returns(Product);
            Journal.Setup(j => j.AddRecord(It.IsAny<JournalRecordModel>())).Callback<JournalRecordModel>(JournalRecords.Enqueue);
        }


        internal DumpPasswordService CreateService(TimeSpan? retention = null)
        {
            var service = new DumpPasswordService(Database, Journal.Object, Cache.Object, retention, () => Now);

            _services.Add(service);

            return service;
        }

        public void Dispose()
        {
            foreach (var service in _services)
                service.Dispose();

            Manager.DatabaseCore.Dispose();

            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }


    public sealed class DumpPasswordServiceTests : IDisposable
    {
        private const string TypedPassword = "correct-horse-battery-staple";

        private static readonly InitiatorInfo Manager = InitiatorInfo.AsUser("manager");

        private readonly DumpPasswordTestContext _context = new();


        public void Dispose() => _context.Dispose();


        [Fact]
        public void SetPassword_Stores_A_Version_And_Makes_It_Current()
        {
            var service = _context.CreateService();

            var result = service.SetPassword(_context.Product.Id, TypedPassword, generated: false, Manager);

            Assert.True(result.IsOk);
            Assert.Equal(1, result.Version);
            Assert.True(service.HasPassword(_context.Product.Id));

            var current = service.GetCurrent(_context.Product.Id);

            Assert.Equal(1, current.Version);
            Assert.Equal(TypedPassword, current.Password);
            Assert.Equal(_context.Now, current.SetUtc);
            Assert.Equal(Manager.ToString(), current.SetBy);
            Assert.Null(current.RetiredUtc);
        }

        [Fact]
        public void Parallel_SetPassword_Allocates_Consecutive_Durable_Versions()
        {
            const int count = 20;

            var service = _context.CreateService();
            var changed = new ConcurrentBag<int>();

            service.PasswordChanged += (_, version) => changed.Add(version);

            var results = new DumpPasswordSetResult[count];

            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = count }, i =>
                results[i] = service.SetPassword(_context.Product.Id, service.Generate(), generated: true, Manager));

            Assert.All(results, r => Assert.True(r.IsOk));
            Assert.Equal(Enumerable.Range(1, count), results.Select(r => r.Version).Order());
            Assert.Equal(Enumerable.Range(1, count), changed.Order());
            Assert.Equal(count, _context.JournalRecords.Count);

            var versions = service.GetVersions(_context.Product.Id);

            Assert.Equal(Enumerable.Range(1, count).Reverse(), versions.Select(v => v.Version));
            Assert.Equal(count, service.GetCurrent(_context.Product.Id).Version);

            // Every version landed in the database, not only the last one written.
            var reloaded = _context.CreateService().GetVersions(_context.Product.Id);

            Assert.Equal(versions, reloaded);
        }

        [Fact]
        public void Reload_From_The_Database_Gives_The_Same_Versions()
        {
            var service = _context.CreateService();

            service.SetPassword(_context.Product.Id, TypedPassword, generated: false, Manager);
            _context.Now = _context.Now.AddHours(1);
            service.SetPassword(_context.Product.Id, service.Generate(), generated: true, InitiatorInfo.System);
            service.MarkRetired(_context.Product.Id, 1, _context.Now.AddMinutes(5));

            var reloaded = _context.CreateService();

            Assert.Equal(service.GetVersions(_context.Product.Id), reloaded.GetVersions(_context.Product.Id));
            Assert.Equal(_context.Now.AddMinutes(5), reloaded.GetVersion(_context.Product.Id, 1).RetiredUtc);
            Assert.Equal(DateTimeKind.Utc, reloaded.GetVersion(_context.Product.Id, 1).SetUtc.Kind);
        }

        [Fact]
        public void A_Failed_Write_Changes_Nothing_Journals_Nothing_And_Raises_Nothing()
        {
            var service = _context.CreateService();
            var events = 0;

            service.SetPassword(_context.Product.Id, TypedPassword, generated: false, Manager);
            service.PasswordChanged += (_, _) => events++;

            var journaled = _context.JournalRecords.Count;
            _context.Database.ShouldFailDumpPasswordOp = op => op == nameof(FailingDatabaseCore.PutDumpPasswords);

            Assert.Throws<InvalidOperationException>(() =>
                service.SetPassword(_context.Product.Id, "another-password-of-16+", generated: false, Manager));

            Assert.Equal(0, events);
            Assert.Equal(journaled, _context.JournalRecords.Count);
            Assert.Equal([1], service.GetVersions(_context.Product.Id).Select(v => v.Version));
            Assert.Equal(TypedPassword, service.GetCurrent(_context.Product.Id).Password);

            // The next change after the failure takes the version the failed one did not.
            _context.Database.ShouldFailDumpPasswordOp = null;

            Assert.Equal(2, service.SetPassword(_context.Product.Id, "another-password-of-16+", generated: false, Manager).Version);
            Assert.Equal([2, 1], _context.CreateService().GetVersions(_context.Product.Id).Select(v => v.Version));
        }

        [Fact]
        public void A_Failed_Write_On_The_First_Password_Leaves_The_Product_Without_One()
        {
            var service = _context.CreateService();

            _context.Database.ShouldFailDumpPasswordOp = _ => true;

            Assert.Throws<InvalidOperationException>(() => service.SetPassword(_context.Product.Id, TypedPassword, generated: false, Manager));

            Assert.False(service.HasPassword(_context.Product.Id));
            Assert.Null(service.GetCurrent(_context.Product.Id));
            Assert.Empty(service.GetVersions(_context.Product.Id));
            Assert.Empty(_context.JournalRecords);
        }

        [Fact]
        public void A_Journal_Failure_Does_Not_Fail_Or_Undo_The_Change()
        {
            var service = _context.CreateService();
            var events = 0;

            service.PasswordChanged += (_, _) => events++;
            _context.Journal.Setup(j => j.AddRecord(It.IsAny<JournalRecordModel>())).Throws(new InvalidOperationException("journal down"));

            var result = service.SetPassword(_context.Product.Id, TypedPassword, generated: false, Manager);

            Assert.True(result.IsOk);
            Assert.Equal(1, events);
            Assert.Equal(1, _context.CreateService().GetCurrent(_context.Product.Id).Version);
        }

        [Fact]
        public void A_Throwing_Subscriber_Does_Not_Fail_The_Change_Or_Silence_The_Others()
        {
            var service = _context.CreateService();
            var heard = 0;

            service.PasswordChanged += (_, _) => throw new InvalidOperationException("subscriber");
            service.PasswordChanged += (_, _) => heard++;

            Assert.True(service.SetPassword(_context.Product.Id, TypedPassword, generated: false, Manager).IsOk);
            Assert.Equal(1, heard);
        }

        [Fact]
        public void Unknown_Product_Is_Refused_Without_A_Write()
        {
            var service = _context.CreateService();
            var unknown = Guid.NewGuid();

            var result = service.SetPassword(unknown, TypedPassword, generated: false, Manager);

            Assert.Equal(DumpPasswordSetOutcome.ProductUnknown, result.Outcome);
            Assert.False(service.HasPassword(unknown));
            Assert.Empty(_context.JournalRecords);
            Assert.Empty(_context.Database.GetAllDumpPasswords());
        }

        [Theory]
        [InlineData(null, false, DumpPasswordSetOutcome.TooShort)]
        [InlineData("", false, DumpPasswordSetOutcome.TooShort)]
        [InlineData(null, true, DumpPasswordSetOutcome.TooShort)]
        [InlineData("", true, DumpPasswordSetOutcome.TooShort)]
        [InlineData("fifteen-chars-x", false, DumpPasswordSetOutcome.TooShort)]
        [InlineData("sixteen-chars-xx", false, DumpPasswordSetOutcome.Set)]
        [InlineData(" ~!\"#$%&'()*+,-./0123456789:;<=>?@AZ[\\]^_`az{|}", false, DumpPasswordSetOutcome.Set)]
        [InlineData("password-with-я-letter", false, DumpPasswordSetOutcome.NotPrintableAscii)]
        [InlineData("password-with-\t-tab", false, DumpPasswordSetOutcome.NotPrintableAscii)]
        [InlineData("password-with-\x7F-del", false, DumpPasswordSetOutcome.NotPrintableAscii)]
        [InlineData("password-with-é-accent", false, DumpPasswordSetOutcome.NotPrintableAscii)]
        [InlineData("short", true, DumpPasswordSetOutcome.Set)]
        public void SetPassword_Validates_Typed_Passwords(string password, bool generated, DumpPasswordSetOutcome expected)
        {
            var service = _context.CreateService();

            var result = service.SetPassword(_context.Product.Id, password, generated, Manager);

            Assert.Equal(expected, result.Outcome);
            Assert.Equal(expected == DumpPasswordSetOutcome.Set, service.HasPassword(_context.Product.Id));

            if (expected != DumpPasswordSetOutcome.Set)
            {
                Assert.Equal(0, result.Version);
                Assert.Empty(_context.JournalRecords);
            }
        }

        [Fact]
        public void A_Typed_Password_Of_MaxLength_Is_Accepted_And_A_Longer_One_Is_Not()
        {
            var service = _context.CreateService();

            Assert.True(service.SetPassword(_context.Product.Id, new string('a', DumpPasswordRules.MaxLength), false, Manager).IsOk);

            var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
                service.SetPassword(_context.Product.Id, new string('b', DumpPasswordRules.MaxLength + 1), false, Manager));

            Assert.DoesNotContain(new string('b', 32), ex.ToString());
            Assert.Equal(1, service.GetCurrent(_context.Product.Id).Version);
        }

        [Fact]
        public void Generate_Gives_Passwords_Of_The_Length_And_Alphabet()
        {
            var service = _context.CreateService();
            var passwords = Enumerable.Range(0, 200).Select(_ => service.Generate()).ToList();

            Assert.All(passwords, p =>
            {
                Assert.Equal(DumpPasswordRules.GeneratedLength, p.Length);
                Assert.All(p, c => Assert.Contains(c, DumpPasswordRules.GeneratedAlphabet));
            });

            Assert.Equal(passwords.Count, passwords.Distinct().Count());
            Assert.True(service.SetPassword(_context.Product.Id, passwords[0], generated: true, Manager).IsOk);
        }

        [Fact]
        public void MarkRetired_Sets_The_Time_Once_And_Never_On_The_Current_Version()
        {
            var service = _context.CreateService();

            service.SetPassword(_context.Product.Id, TypedPassword, false, Manager);
            service.SetPassword(_context.Product.Id, service.Generate(), true, Manager);

            var first = _context.Now.AddDays(1);

            service.MarkRetired(_context.Product.Id, 1, first);
            service.MarkRetired(_context.Product.Id, 1, first.AddDays(1));
            service.MarkRetired(_context.Product.Id, 2, first);
            service.MarkRetired(_context.Product.Id, 99, first);
            service.MarkRetired(Guid.NewGuid(), 1, first);

            Assert.Equal(first, service.GetVersion(_context.Product.Id, 1).RetiredUtc);
            Assert.True(service.GetVersion(_context.Product.Id, 1).IsRetired);
            Assert.Null(service.GetCurrent(_context.Product.Id).RetiredUtc);
            Assert.Equal(first, _context.CreateService().GetVersion(_context.Product.Id, 1).RetiredUtc);
        }

        [Fact]
        public void MarkRetired_That_Cannot_Be_Written_Throws_And_Changes_Nothing()
        {
            var service = _context.CreateService();

            service.SetPassword(_context.Product.Id, TypedPassword, false, Manager);
            service.SetPassword(_context.Product.Id, service.Generate(), true, Manager);
            _context.Database.ShouldFailDumpPasswordOp = _ => true;

            Assert.Throws<InvalidOperationException>(() => service.MarkRetired(_context.Product.Id, 1, _context.Now));

            Assert.False(service.GetVersion(_context.Product.Id, 1).IsRetired);
        }

        [Fact]
        public void RemoveExpired_Removes_Old_Retired_Versions_Only()
        {
            var retention = TimeSpan.FromDays(30);
            var service = _context.CreateService(retention);
            var id = _context.Product.Id;

            for (var i = 0; i < 4; i++)
                service.SetPassword(id, service.Generate(), true, Manager);

            var now = _context.Now.AddDays(100);

            service.MarkRetired(id, 1, now - retention - TimeSpan.FromSeconds(1)); // expired
            service.MarkRetired(id, 2, now - retention + TimeSpan.FromSeconds(1)); // recent
            // Version 3 is replaced and not retired: an archive may still carry it.

            Assert.Equal(1, service.RemoveExpired(now));

            Assert.Equal([4, 3, 2], service.GetVersions(id).Select(v => v.Version));
            Assert.Null(service.GetVersion(id, 1));
            Assert.Equal([4, 3, 2], _context.CreateService(retention).GetVersions(id).Select(v => v.Version));

            Assert.Equal(0, service.RemoveExpired(now));
        }

        [Fact]
        public void RemoveExpired_Never_Removes_The_Current_Version()
        {
            var service = _context.CreateService(TimeSpan.Zero);
            var id = _context.Product.Id;

            service.SetPassword(id, TypedPassword, false, Manager);

            Assert.Equal(0, service.RemoveExpired(_context.Now.AddYears(10)));
            Assert.Equal(1, service.GetCurrent(id).Version);
        }

        [Fact]
        public void RemoveExpired_That_Cannot_Be_Written_Keeps_The_Versions_And_Throws()
        {
            var service = _context.CreateService(TimeSpan.FromDays(1));
            var id = _context.Product.Id;

            service.SetPassword(id, TypedPassword, false, Manager);
            service.SetPassword(id, service.Generate(), true, Manager);
            service.MarkRetired(id, 1, _context.Now);
            _context.Database.ShouldFailDumpPasswordOp = _ => true;

            Assert.Throws<AggregateException>(() => service.RemoveExpired(_context.Now.AddDays(10)));
            Assert.NotNull(service.GetVersion(id, 1));

            _context.Database.ShouldFailDumpPasswordOp = null;

            Assert.Equal(1, service.RemoveExpired(_context.Now.AddDays(10)));
        }

        [Fact]
        public void RemoveProduct_Removes_Every_Version_From_Memory_And_The_Database()
        {
            var service = _context.CreateService();
            var other = new ProductModel("Other product");

            _context.Cache.Setup(c => c.GetProduct(other.Id)).Returns(other);

            service.SetPassword(_context.Product.Id, TypedPassword, false, Manager);
            service.SetPassword(_context.Product.Id, service.Generate(), true, Manager);
            service.SetPassword(other.Id, TypedPassword, false, Manager);

            service.RemoveProduct(_context.Product.Id);

            Assert.False(service.HasPassword(_context.Product.Id));
            Assert.Empty(service.GetVersions(_context.Product.Id));
            Assert.True(service.HasPassword(other.Id));

            var reloaded = _context.CreateService();

            Assert.False(reloaded.HasPassword(_context.Product.Id));
            Assert.True(reloaded.HasPassword(other.Id));
        }

        [Fact]
        public void RemoveProduct_That_Cannot_Be_Written_Throws_And_Keeps_The_Versions()
        {
            var service = _context.CreateService();

            service.SetPassword(_context.Product.Id, TypedPassword, false, Manager);
            _context.Database.ShouldFailDumpPasswordOp = op => op == nameof(FailingDatabaseCore.RemoveDumpPasswords);

            Assert.Throws<InvalidOperationException>(() => service.RemoveProduct(_context.Product.Id));
            Assert.True(service.HasPassword(_context.Product.Id));
        }

        [Fact]
        public void The_Removal_Of_A_Product_In_The_Tree_Removes_Its_Passwords()
        {
            var service = _context.CreateService();

            service.SetPassword(_context.Product.Id, TypedPassword, false, Manager);

            _context.Cache.Raise(c => c.ChangeProductEvent += null, _context.Product, ActionType.Update);
            Assert.True(service.HasPassword(_context.Product.Id));

            _context.Cache.Raise(c => c.ChangeProductEvent += null, _context.Product, ActionType.Delete);
            Assert.False(service.HasPassword(_context.Product.Id));
            Assert.False(_context.CreateService().HasPassword(_context.Product.Id));
        }

        [Fact]
        public void A_Failed_Removal_From_The_Tree_Event_Is_Not_Thrown_Into_The_Cache()
        {
            var service = _context.CreateService();

            service.SetPassword(_context.Product.Id, TypedPassword, false, Manager);
            _context.Database.ShouldFailDumpPasswordOp = _ => true;

            _context.Cache.Raise(c => c.ChangeProductEvent += null, _context.Product, ActionType.Delete);
            _context.Cache.Raise(c => c.ChangeProductEvent += null, null, ActionType.Delete);

            Assert.True(service.HasPassword(_context.Product.Id));
        }

        [Fact]
        public void Dispose_Stops_Listening_To_The_Tree()
        {
            var service = _context.CreateService();

            service.SetPassword(_context.Product.Id, TypedPassword, false, Manager);
            service.Dispose();

            _context.Cache.Raise(c => c.ChangeProductEvent += null, _context.Product, ActionType.Delete);

            Assert.True(service.HasPassword(_context.Product.Id));
        }

        [Fact]
        public void The_Journal_Record_Names_The_Change_And_Carries_No_Password()
        {
            var service = _context.CreateService();

            service.SetPassword(_context.Product.Id, TypedPassword, false, Manager);
            service.SetPassword(_context.Product.Id, "second-typed-password", false, Manager);

            Assert.Equal(2, _context.JournalRecords.Count);

            var records = _context.JournalRecords.ToArray();
            var last = records[^1];

            Assert.Equal(_context.Product.Id, last.Key.Id);
            Assert.Equal(JournalKinds.NodeSettings, last.Kind);
            Assert.Equal(_context.Product.Id.ToString(), last.ProductId);
            Assert.Equal("Dump password", last.PropertyName);
            Assert.Equal("changed (version 2)", last.NewValue);
            Assert.True(string.IsNullOrEmpty(last.OldValue));
            Assert.Equal(Manager.ToString(), last.Initiator);
            Assert.Equal(JournalOperations.Update, JournalOperations.Of(last));

            foreach (var record in records)
            {
                AssertNoPassword(record, TypedPassword, "second-typed-password");
                AssertNoPassword(record.ToJournalEntity(), TypedPassword, "second-typed-password");
            }
        }

        [Fact]
        public void Versions_Print_No_Password()
        {
            var service = _context.CreateService();

            service.SetPassword(_context.Product.Id, TypedPassword, false, Manager);

            Assert.DoesNotContain(TypedPassword, service.GetCurrent(_context.Product.Id).ToString());

            var entity = _context.Database.GetAllDumpPasswords().Single();

            Assert.DoesNotContain(TypedPassword, entity.ToString());
            Assert.DoesNotContain(TypedPassword, entity.Versions[0].ToString());
            Assert.Equal(TypedPassword, entity.Versions[0].Password);
        }


        internal static void AssertNoPassword(object record, params string[] passwords)
        {
            foreach (var property in record.GetType().GetProperties().Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0))
            {
                var value = (string)property.GetValue(record);

                foreach (var password in passwords)
                    Assert.False(value?.Contains(password) == true, $"{record.GetType().Name}.{property.Name} carries a password");
            }
        }
    }
}
