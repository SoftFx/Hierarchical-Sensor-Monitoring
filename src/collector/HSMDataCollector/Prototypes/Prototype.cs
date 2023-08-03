using HSMDataCollector.SensorsMetainfo;

namespace HSMDataCollector.Options
{
    internal abstract class Prototype<MetainfoType, OptionsType>
        where MetainfoType : SensorMetainfo, new()
    {
        protected const string ProductInfoPath = "Product Info";
        protected const string CollectorPath = ProductInfoPath + "/Collector";

        protected abstract MetainfoType BaseMetainfo { get; }


        protected abstract MetainfoType Get(OptionsType options);
    }
}