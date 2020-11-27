using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Model;
using HSMClientWPFControls.Objects;
using HSMClientWPFControls.SensorExpandingService;

namespace HSMClientWPFControls.ViewModel
{
    public class MonitoringTreeViewModel : ViewModelBase, IDisposable
    {
        #region IDisposable implementation

        // Disposed flag.
        private bool _disposed;

        // Implement IDisposable.
        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected new virtual void Dispose(bool disposingManagedResources)
        {
            // The idea here is that Dispose(Boolean) knows whether it is 
            // being called to do explicit cleanup (the Boolean is true) 
            // versus being called due to a garbage collection (the Boolean 
            // is false). This distinction is useful because, when being 
            // disposed explicitly, the Dispose(Boolean) method can safely 
            // execute code using reference type fields that refer to other 
            // objects knowing for sure that these other objects have not been 
            // finalized or disposed of yet. When the Boolean is false, 
            // the Dispose(Boolean) method should not execute code that 
            // refer to reference type fields because those objects may 
            // have already been finalized."

            if (!_disposed)
            {
                if (disposingManagedResources)
                {
                    //Dispose managed resources here...
                    foreach (var node in Nodes)
                    {
                        if (node.SubNodes != null)
                        {
                            foreach (var childNode in node.SubNodes)
                            {
                                childNode.Dispose();
                            }
                        }

                        node.Dispose();
                    }
                }
                _model.Dispose();
                // Dispose unmanaged resources here...

                // Set large fields to null here...

                // Mark as disposed.
                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~MonitoringTreeViewModel()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion

        private ISensorExpandingService _expandingService;
        public MonitoringTreeViewModel(IMonitoringModel model) : base(model as ModelBase)
        {
            _model = model;

            ShowProductsCommand = new MultipleDelegateCommand(ShowProducts, CanShowProducts);
            SensorDoubleClickCommand = new SingleDelegateCommand(ExpandSensor);

        }

        private IMonitoringModel _model;
        private ObservableCollection<MonitoringSensorBaseViewModel> _currentSensors;
        private MonitoringNodeBase _selectedNode;
        private MonitoringSensorBaseViewModel _selectedSensor;

        public ICommand ShowProductsCommand { get; private set; }
        public ICommand SensorDoubleClickCommand { get; private set; }

        public bool IsDefaultCertificateWarning
        {
            get => _model.IsClientCertificateDefault;
        }


        public MonitoringNodeBase SelectedNode
        {
            get => _selectedNode;
            set
            {
                _selectedNode = value;
                CurrentSensors = _selectedNode.Sensors;
            }
        }

        public MonitoringSensorBaseViewModel SelectedSensor
        {
            get => _selectedSensor;
            set { _selectedSensor = value; }
        }

        public ObservableCollection<MonitoringSensorBaseViewModel> CurrentSensors
        {
            get => _currentSensors;
            set
            {
                _currentSensors = value;
                OnPropertyChanged(nameof(CurrentSensors));
            }
        }

        public IMonitoringModel Model
        {
            get => _model;
            set
            {
                _model = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Nodes));
            }
        }

        public ISensorExpandingService SensorExpandingService
        {
            get => _expandingService;
            set => _expandingService = value;
        }

        public ObservableCollection<MonitoringNodeBase> Nodes
        {
            get { return _model?.Nodes; }
        }

        public ObservableCollection<MonitoringSensorBaseViewModel> Sensors { get; set; }

        private void ShowProducts()
        {
            _model.ShowProducts();
        }

        private bool CanShowProducts()
        {
            return true;
        }

        private bool ExpandSensor(object o, bool isCheckOnly)
        {
            if(isCheckOnly)
                return true;

            _expandingService?.Expand(SelectedSensor);
            return true;
        }
    }
}
