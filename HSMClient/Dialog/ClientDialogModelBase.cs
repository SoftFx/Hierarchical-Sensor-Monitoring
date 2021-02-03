using HSMClientWPFControls.Bases;
using HSMClientWPFControls.ConnectorInterface;
using HSMClientWPFControls.Model.SensorDialog;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.ViewModel;

namespace HSMClient.Dialog
{
    public class ClientDialogModelBase : ModelBase, ISensorDialogModel
    {
        protected ISensorHistoryConnector _connector;
        protected string _path;
        protected string _name;
        protected string _product;

        public ClientDialogModelBase(ISensorHistoryConnector connector, MonitoringSensorViewModel sensor)
        {
            _path = sensor.Path;
            _connector = connector;
            _name = sensor.Name;
            _product = sensor.Product;
        }
    }
}
