using HSMCommon.Extensions;

namespace HSMServer.Core.Model.Policies
{
    public abstract class Policy<T> : Policy where T : BaseValue
    {
        internal abstract bool Validate(T value, BaseSensorModel sensor);
    }
}