using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Model.Groups;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Groups
{
    public sealed class GroupManager : ConcurrentStorage<GroupModel, GroupEntity, GroupUpdate>, IGroupManager
    {
        private readonly IDatabaseCore _databaseCore;
        private readonly IUserManager _userManager;
        private readonly ITreeValuesCache _cache;
        private readonly ILogger<GroupManager> _logger;


        protected override Action<GroupEntity> AddToDb => _databaseCore.AddGroup;

        protected override Action<GroupEntity> UpdateInDb => _databaseCore.UpdateGroup;

        protected override Action<GroupModel> RemoveFromDb => group => _databaseCore.RemoveGroup(group.Id.ToString());

        protected override Func<List<GroupEntity>> GetFromDb => _databaseCore.GetAllGroups;


        public GroupManager(IDatabaseCore databaseCore, IUserManager userManager, ITreeValuesCache cache, ILogger<GroupManager> logger)
        {
            _databaseCore = databaseCore;
            _userManager = userManager;
            _cache = cache;
            _logger = logger;
        }


        public List<GroupModel> GetGroups() => Values.ToList();

        public override async Task Initialize()
        {
            await base.Initialize();

            foreach (var (_, group) in this)
                if (_userManager.TryGetValue(group.AuthorId, out var author))
                    group.Author = author.Name;

            foreach (var product in _cache.GetProducts())
                if (product.GroupId.HasValue && TryGetValue(product.GroupId.Value, out var group))
                    group.Products.Add(product);
        }

        protected override GroupModel FromEntity(GroupEntity entity) => new(entity);
    }
}
