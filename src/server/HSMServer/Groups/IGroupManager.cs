using HSMServer.Model.Groups;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Groups
{
    public interface IGroupManager
    {
        GroupModel this[Guid id] { get; }


        Task<bool> TryAdd(GroupModel group);

        List<GroupModel> GetGroups();

        Task Initialize();
    }
}
