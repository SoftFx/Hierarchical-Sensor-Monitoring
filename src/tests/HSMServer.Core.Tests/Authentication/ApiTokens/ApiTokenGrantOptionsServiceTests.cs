using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using Moq;
using Xunit;
using ProductModel = HSMServer.Core.Model.ProductModel;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Issuance-side owner filter (initiative step 4): what the create form may offer and
    // what the lifecycle endpoints accept. The rules mirror the evaluator's
    // OwnerCanSee/OwnerCanPerform conjunction: reads need any role, writes the Manager
    // role, the global boundary is admin-only, product rights come from the
    // materialised ProductsRoles with no folder fallback, and stale roles anchor nothing.
    public class ApiTokenGrantOptionsServiceTests
    {
        private static readonly Guid ProductA = Guid.NewGuid();
        private static readonly Guid ProductB = Guid.NewGuid();
        private static readonly Guid DeadProduct = Guid.NewGuid();
        private static readonly Guid FolderF = Guid.NewGuid();
        private static readonly Guid DeadFolder = Guid.NewGuid();

        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly User _user = new("user") { Id = Guid.NewGuid() };

        private readonly ProductModel _productA =
            new(EntitiesFactory.BuildProductEntity(name: "Product A") with { Id = ProductA.ToString() });

        private readonly ProductModel _productB =
            new(EntitiesFactory.BuildProductEntity(name: "Product B") with { Id = ProductB.ToString() });

        private readonly FolderModel _folderF =
            new(EntitiesFactory.BuildFolderEntity() with { Id = FolderF.ToString() });


        public ApiTokenGrantOptionsServiceTests()
        {
            SetupProductLookup(ProductA, _productA);
            SetupProductLookup(ProductB, _productB);
            SetupProductName(ProductA, "Product A");
            SetupProductName(ProductB, "Product B");

            SetupFolderLookup(FolderF, _folderF);
        }


        private ApiTokenGrantOptionsService CreateService() => new(_cache.Object, _folders.Object);


        [Fact]
        public void Admin_GetsGlobalPlusEverything()
        {
            _user.IsAdmin = true;
            _cache.Setup(c => c.GetProducts()).Returns(new List<ProductModel> { _productA, _productB });
            _folders.Setup(f => f.GetValues()).Returns(new List<FolderModel> { _folderF });

            var options = CreateService().GetBoundaryOptions(_user);

            var global = Assert.Single(options, o => o.Kind == "global");
            Assert.Equal(string.Empty, global.Id);
            // The global boundary carries every catalog operation for an admin,
            // including the global-only system-health:read and all writes.
            Assert.True(global.Operations.Contains(ApiTokenOperations.SystemHealthRead));
            Assert.True(global.Operations.Contains(ApiTokenOperations.ProductsWrite));

            Assert.True(options.Any(o => o.Kind == "product" && o.Id == ProductA.ToString()));
            Assert.True(options.Any(o => o.Kind == "folder" && o.Id == FolderF.ToString()));

            foreach (var boundary in options.Where(o => o.Kind != "global"))
            {
                Assert.True(boundary.Operations.Contains(ApiTokenOperations.AlertsWrite));
                Assert.False(boundary.Operations.Contains(ApiTokenOperations.SystemHealthRead));
            }
        }


        [Fact]
        public void ProductViewer_GetsReadsOnly_NoGlobalBoundary()
        {
            _user.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductViewer));

            var options = CreateService().GetBoundaryOptions(_user);

            var boundary = Assert.Single(options);
            Assert.Equal(ProductA.ToString(), boundary.Id);
            Assert.False(boundary.Operations.Any(ApiTokenOperations.IsWrite), "viewer must not be offered writes");
            Assert.True(boundary.Operations.Contains(ApiTokenOperations.ProductsRead));
            // The global boundary (and system-health:read with it) is admin-only.
            Assert.False(options.Any(o => o.Kind == "global"));
        }


        [Fact]
        public void ProductManager_GetsWritesOnOwnProductOnly()
        {
            _user.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _user.ProductsRoles.Add((ProductB, ProductRoleEnum.ProductViewer));

            var options = CreateService().GetBoundaryOptions(_user);

            var a = Assert.Single(options, o => o.Id == ProductA.ToString());
            Assert.True(a.Operations.Contains(ApiTokenOperations.AlertsWrite));

            var b = Assert.Single(options, o => o.Id == ProductB.ToString());
            Assert.False(b.Operations.Any(ApiTokenOperations.IsWrite), "viewer must not be offered writes");
        }


        [Fact]
        public void FolderRoles_FoldIntoBoundaryOptions()
        {
            _user.FoldersRoles.Add(FolderF, ProductRoleEnum.ProductManager);

            var options = CreateService().GetBoundaryOptions(_user);

            var folder = Assert.Single(options, o => o.Kind == "folder");
            Assert.Equal(FolderF.ToString(), folder.Id);
            Assert.True(folder.Operations.Contains(ApiTokenOperations.SensorsWrite));
        }


        [Fact]
        public void StaleRoles_AreSkipped()
        {
            // Roles for products/folders that no longer exist anchor nothing and must
            // not appear in the picker.
            _user.ProductsRoles.Add((DeadProduct, ProductRoleEnum.ProductManager));
            _user.FoldersRoles.Add(DeadFolder, ProductRoleEnum.ProductManager);

            var options = CreateService().GetBoundaryOptions(_user);

            Assert.Empty(options);
        }


        [Fact]
        public void IsGrantableByOwner_Matrix()
        {
            _user.ProductsRoles.Add((ProductA, ProductRoleEnum.ProductManager));
            _user.FoldersRoles.Add(FolderF, ProductRoleEnum.ProductViewer);
            var service = CreateService();

            Assert.True(service.IsGrantableByOwner(_user, ApiTokenOperations.AlertsWrite,
                ApiTokenBoundaryKind.Product, ProductA.ToString()));
            Assert.True(service.IsGrantableByOwner(_user, ApiTokenOperations.AlertsRead,
                ApiTokenBoundaryKind.Product, ProductA.ToString()));

            // Writes on a viewer-only boundary are refused.
            Assert.False(service.IsGrantableByOwner(_user, ApiTokenOperations.AlertsWrite,
                ApiTokenBoundaryKind.Folder, FolderF.ToString()));
            Assert.True(service.IsGrantableByOwner(_user, ApiTokenOperations.AlertsRead,
                ApiTokenBoundaryKind.Folder, FolderF.ToString()));

            // Foreign or dead boundaries anchor nothing.
            Assert.False(service.IsGrantableByOwner(_user, ApiTokenOperations.AlertsRead,
                ApiTokenBoundaryKind.Product, ProductB.ToString()));
            Assert.False(service.IsGrantableByOwner(_user, ApiTokenOperations.AlertsRead,
                ApiTokenBoundaryKind.Product, DeadProduct.ToString()));

            // The global boundary and the global-only operation are admin-only.
            Assert.False(service.IsGrantableByOwner(_user, ApiTokenOperations.ProductsRead,
                ApiTokenBoundaryKind.Global, string.Empty));
            Assert.False(service.IsGrantableByOwner(_user, ApiTokenOperations.SystemHealthRead,
                ApiTokenBoundaryKind.Product, ProductA.ToString()));

            // Unknown catalog operations fail closed.
            Assert.False(service.IsGrantableByOwner(_user, "users:read",
                ApiTokenBoundaryKind.Product, ProductA.ToString()));
        }


        [Fact]
        public void Admin_IsGrantableByOwner_GlobalAndSystemHealth()
        {
            _user.IsAdmin = true;
            var service = CreateService();

            Assert.True(service.IsGrantableByOwner(_user, ApiTokenOperations.SystemHealthRead,
                ApiTokenBoundaryKind.Global, string.Empty));
            Assert.True(service.IsGrantableByOwner(_user, ApiTokenOperations.ProductsWrite,
                ApiTokenBoundaryKind.Product, ProductA.ToString()));
            // A global pair carrying an id is malformed and refused even for an admin.
            Assert.False(service.IsGrantableByOwner(_user, ApiTokenOperations.SystemHealthRead,
                ApiTokenBoundaryKind.Global, ProductA.ToString()));
        }


        private void SetupProductLookup(Guid id, ProductModel product) =>
            _cache.Setup(c => c.TryGetProduct(id, out It.Ref<ProductModel>.IsAny))
                .Callback(new OutProductCallback((Guid _, out ProductModel p) => p = product))
                .Returns(true);

        private void SetupProductName(Guid id, string name) =>
            _cache.Setup(c => c.TryGetProductNameById(id, out It.Ref<string>.IsAny))
                .Callback(new OutStringCallback((Guid _, out string n) => n = name))
                .Returns(true);

        private void SetupFolderLookup(Guid id, FolderModel folder) =>
            _folders.Setup(f => f.TryGetValue(id, out It.Ref<FolderModel>.IsAny))
                .Callback(new OutFolderCallback((Guid _, out FolderModel f) => f = folder))
                .Returns(true);


        private delegate void OutProductCallback(Guid id, out ProductModel product);
        private delegate void OutFolderCallback(Guid id, out FolderModel folder);
        private delegate void OutStringCallback(Guid id, out string name);
    }
}
