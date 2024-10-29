using System.Globalization;
using HSMServer.Core.Model;
using HSMServer.Dashboards;

namespace HSMServer.Model.History
{
    public class ChartValuesViewModel
    {
        private readonly BaseSensorModel _sensor;
        private readonly PanelDatasource _source;
        
        
        public ChartValuesViewModel(BaseSensorModel sensor)
        {
            _sensor = sensor;
            _source = new PanelDatasource(sensor);
        }
    }
}
