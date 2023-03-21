using HSMServer.Model.Groups;
using System;
using System.Threading.Tasks;

namespace HSMServer.Groups
{
    public interface IGroupManager
    {
        GroupModel this[Guid id] { get; }


        Task<bool> TryAdd(GroupModel group);

        Task Initialize();
    }
}
