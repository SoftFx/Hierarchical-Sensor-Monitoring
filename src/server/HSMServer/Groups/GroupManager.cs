using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using HSMServer.Model.Groups;
using HSMServer.Model.TreeViewModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Groups
{
    public sealed class GroupManager : ConcurrentStorage<GroupModel, GroupEntity, GroupUpdate>, IGroupManager
    {
        private readonly IUserManager _userManager;
        private readonly IDatabaseCore _databaseCore;
        private readonly TreeViewModel _treeViewModel;
        private readonly ILogger<GroupManager> _logger;


        protected override Action<GroupEntity> AddToDb => _databaseCore.AddGroup;

        protected override Action<GroupEntity> UpdateInDb => _databaseCore.UpdateGroup;

        protected override Action<GroupModel> RemoveFromDb => group => _databaseCore.RemoveGroup(group.Id.ToString());

        protected override Func<List<GroupEntity>> GetFromDb => _databaseCore.GetAllGroups;


        public GroupManager(IDatabaseCore databaseCore, IUserManager userManager,
            TreeViewModel treeViewModel, ILogger<GroupManager> logger)
        {
            _databaseCore = databaseCore;
            _userManager = userManager;
            _treeViewModel = treeViewModel;
            _logger = logger;
        }


        public List<GroupModel> GetGroups() => Values.ToList();

        public override async Task Initialize()
        {
            await base.Initialize();

            foreach (var (_, group) in this)
                if (_userManager.TryGetValue(group.AuthorId, out var author))
                    group.Author = author.Name;

            foreach (var (_, node) in _treeViewModel.Nodes)
                if (node.Parent is null && node.GroupId.HasValue && TryGetValue(node.GroupId.Value, out var group))
                    group.Products.Add(node);
        }

        protected override GroupModel FromEntity(GroupEntity entity) => new(entity);
    }
}
