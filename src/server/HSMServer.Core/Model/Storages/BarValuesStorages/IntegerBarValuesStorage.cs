using HSMServer.Core.Extensions;

namespace HSMServer.Core.Model
{
    public sealed class IntegerBarValuesStorage : BarValuesStorage<IntegerBarValue>
    {
        protected override IntegerBarValue Merge(IntegerBarValue value) => (IntegerBarValue)LocalLastValue.Merge(value);
    }
}
