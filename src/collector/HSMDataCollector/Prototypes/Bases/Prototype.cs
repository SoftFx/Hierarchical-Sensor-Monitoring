using HSMDataCollector.SensorsMetainfo;
using System.Linq;

namespace HSMDataCollector.Prototypes
{
    internal abstract class Prototype<MetainfoType, OptionsType> : SensorMetainfo
        where MetainfoType : SensorMetainfo, new()
    {
        private const string PathSeparator = "/";

        //protected const string ProductInfoPath = "Product Info";
        //protected const string CollectorPath = ProductInfoPath + "/Collector";


        protected abstract MetainfoType Apply(MetainfoType info, OptionsType options);

        internal MetainfoType Get(OptionsType options)
        {
            var info = new MetainfoType()
            {
                OriginalUnit = OriginalUnit,
                Description = Description,
                Path = Path,

                Settings = new SensorMetainfoSettings()
                {
                    KeepHistory = Settings.KeepHistory,
                    SelfDestroy = Settings.SelfDestroy,
                    TTL = Settings.TTL,
                },

                Enables = new SensorMetainfoEnables()
                {
                    ForGrafana = Enables.ForGrafana,
                },

                AggregateData = AggregateData,
            };

            return Apply(info, options);
        }

        protected string BuildPath(params string[] parts) => string.Join(PathSeparator, parts.Select(u => !string.IsNullOrEmpty(u)));
    }
}