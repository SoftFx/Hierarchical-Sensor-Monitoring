using HSMServer.Core.Extensions;

namespace HSMServer.Core.Model
{
    public sealed class DoubleBarValuesStorage : BarValuesStorage<DoubleBarValue>
    {
        protected override DoubleBarValue Merge(DoubleBarValue value) => (DoubleBarValue)LocalLastValue.Merge(value);
    }
}
