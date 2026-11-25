using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
using HSMServer.Core.LargeFiles.Archives;
using HSMServer.Core.LargeFiles.DumpPasswords;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.ServerConfiguration;
using HSMServer.ServiceExtensions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Downloads
{
    // The registration of the archive side (#1885): the dump password service allocates a version past the highest one an
    // archive of the product carries, read from the store (the builder depends on the service, never the other way round).
    public sealed class LargeFileArchivesRegistrationTests : IDisposable
    {
        private readonly FakeLargeFileArchiveStore _store = new();
        private readonly ProductModel _product = new("Dumps product");
        private readonly List<ProductDumpPasswordsEntity> _rows = [];


        public void Dispose() => _store.Dispose();


        [Fact]
        public void TheRegisteredPasswordService_AllocatesPastTheVersionsThePackedArchivesCarry()
        {
            _store.AddPacked(_product.Id, Guid.NewGuid(), [1, 2, 3], passwordVersion: 5);
            _store.AddPacked(_product.Id, Guid.NewGuid(), [4, 5, 6], passwordVersion: 3);
            _store.AddPacked(Guid.NewGuid(), Guid.NewGuid(), [7, 8, 9], passwordVersion: 40);

            using var provider = BuildProvider();

            // Resolving both proves the registrations do not depend on each other in a cycle.
            Assert.NotNull(provider.GetRequiredService<IArchiveBuilder>());
            var passwords = provider.GetRequiredService<IDumpPasswordService>();

            var result = passwords.SetPassword(_product.Id, "correct-horse-battery-staple", generated: false, InitiatorInfo.System);

            Assert.True(result.IsOk);
            Assert.Equal(6, result.Version);
            Assert.Equal(6, Assert.Single(_rows).Versions[0].Version);
        }

        [Fact]
        public void AProductWithoutArchives_StartsAtVersionOne()
        {
            _store.AddPacked(Guid.NewGuid(), Guid.NewGuid(), [7, 8, 9], passwordVersion: 40);

            using var provider = BuildProvider();

            Assert.Equal(1, provider.GetRequiredService<IDumpPasswordService>()
                .SetPassword(_product.Id, "correct-horse-battery-staple", generated: false, InitiatorInfo.System).Version);
        }


        private ServiceProvider BuildProvider()
        {
            var database = new Mock<IDatabaseCore>();
            database.Setup(d => d.GetAllDumpPasswords()).Returns([]);
            database.Setup(d => d.PutDumpPasswords(It.IsAny<ProductDumpPasswordsEntity>())).Callback<ProductDumpPasswordsEntity>(_rows.Add);

            var cache = new Mock<ITreeValuesCache>();
            cache.Setup(c => c.GetProduct(_product.Id)).Returns(_product);

            var config = new Mock<IServerConfig>();
            config.Setup(c => c.LargeFileArchives).Returns(new LargeFileArchivesConfig());

            var services = new ServiceCollection();
            services.AddSingleton(database.Object);
            services.AddSingleton(new Mock<IJournalService>().Object);
            services.AddSingleton(cache.Object);
            services.AddLargeFileArchives(config.Object);

            // The test store in place of the placeholder the registration adds.
            services.AddSingleton<ILargeFileArchiveStore>(_store);

            return services.BuildServiceProvider();
        }
    }
}
