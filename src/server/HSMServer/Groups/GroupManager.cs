using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.DataLayer;
using HSMServer.Model.Groups;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace HSMServer.Groups
{
    public sealed class GroupManager : ConcurrentStorage<GroupModel, GroupEntity, GroupUpdate>, IGroupManager
    {
        private readonly IDatabaseCore _databaseCore;
        private readonly ILogger<GroupManager> _logger;


        protected override Action<GroupEntity> AddToDb => _databaseCore.AddGroup;

        protected override Action<GroupEntity> UpdateInDb => _databaseCore.UpdateGroup;

        protected override Action<GroupModel> RemoveFromDb => group => _databaseCore.RemoveGroup(group.Id.ToString());

        protected override Func<List<GroupEntity>> GetFromDb => _databaseCore.GetAllGroups;


        public GroupManager(IDatabaseCore databaseCore, ILogger<GroupManager> logger)
        {
            _databaseCore = databaseCore;
            _logger = logger;
        }


        protected override GroupModel FromEntity(GroupEntity entity) => new(entity);
    }
}
