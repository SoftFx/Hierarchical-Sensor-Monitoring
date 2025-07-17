using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public abstract class WindowsTimeInGCBase : WindowsSensorBase
    {
        public const string Category = ".NET CLR Memory";
        public const string Counter = "% Time in GC";


        protected override string CategoryName => Category;

        protected override string CounterName => Counter;


        internal WindowsTimeInGCBase(BarSensorOptions options) : base(options) { }
    }
}
