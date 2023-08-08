using HSMDataCollector.SensorsMetainfo;

namespace HSMDataCollector.Prototypes
{
    internal abstract class BaseMonitoringPrototype<MetainfoType, OptionsType> : Prototype<MetainfoType, OptionsType>
        where MetainfoType : SensorMetainfo, new()
    {
        protected const string SystemPath = ".Default";


        protected abstract string SensorName { get; }

        protected virtual string Category { get; }


        protected BaseMonitoringPrototype()
        {
            Path = BuildPath(SystemPath, Category, SensorName);
        }
    }
}