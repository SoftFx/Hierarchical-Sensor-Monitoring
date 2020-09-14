using System;
using System.Collections.ObjectModel;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.Objects;

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

        public MonitoringTreeViewModel(IMonitoringModel model) : base(model as ModelBase)
        {
            _model = model;
        }

        private IMonitoringModel _model;
        private ObservableCollection<MonitoringCounterBaseViewModel> _currentCounters;
        private MonitoringNodeBase _selectedNode;
        private MonitoringCounterBaseViewModel _selectedCounter;

        public MonitoringNodeBase SelectedNode
        {
            get => _selectedNode;
            set
            {
                _selectedNode = value;
                CurrentCounters = _selectedNode.Counters;
            }
        }

        public MonitoringCounterBaseViewModel SelectedCounter
        {
            get => _selectedCounter;
            set { _selectedCounter = value; }
        }

        public ObservableCollection<MonitoringCounterBaseViewModel> CurrentCounters
        {
            get => _currentCounters;
            set
            {
                _currentCounters = value;
                OnPropertyChanged(nameof(CurrentCounters));
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

        public ObservableCollection<MonitoringNodeBase> Nodes
        {
            get { return _model?.Nodes; }
        }

        public ObservableCollection<MonitoringCounterBaseViewModel> Counters { get; set; }
    }
}
