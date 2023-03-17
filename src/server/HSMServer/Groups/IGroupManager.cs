using HSMServer.Model.Groups;
using System.Threading.Tasks;

namespace HSMServer.Groups
{
    public interface IGroupManager
    {
        Task<bool> TryAdd(GroupModel group);
    }
}
