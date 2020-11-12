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

        public ClientDialogModelBase(ISensorHistoryConnector connector, MonitoringSensorBaseViewModel sensor)
        {
            string path = "/" + sensor.Name;
            MonitoringNodeBase currentNode = sensor.Parent;
            while (currentNode != null)
            {
                path = ("/" + currentNode.Name + path);
                currentNode = currentNode.Parent;
            }

            _path = path;
            _connector = connector;
            _name = sensor.Name;
            _product = sensor.Product;
        }
    }
}
