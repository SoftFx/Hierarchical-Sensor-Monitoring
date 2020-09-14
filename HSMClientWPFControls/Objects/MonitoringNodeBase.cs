using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using HSMClient.Common;
using HSMClientWPFControls.Bases;
using HSMClientWPFControls.UpdateObjects;
using HSMClientWPFControls.ViewModel;

namespace HSMClientWPFControls.Objects
{
    public class MonitoringNodeBase : NotifyingBase, IDisposable
    {
        #region IDisposable implementation

        // Disposed flag.
        private bool _disposed;

        // Implement IDisposable.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposingManagedResources)
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
                    // Dispose managed resources here...
                    //foreach (var counter in Counters)
                    //  counter.Dispose();
                    foreach (var subNode in SubNodes)
                        subNode.Dispose();
                }

                // Dispose unmanaged resources here...

                // Set large fields to null here...

                // Mark as disposed.
                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~MonitoringNodeBase()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion

        private MonitoringNodeBase _parent;
        private string _name;
        private string _status;
        private string _internalStatus = TextConstants.Unknown;
        private DateTime _lastStatusUpdate;
        private Dictionary<string, MonitoringCounterBaseViewModel> _nameToCounter;
        private Dictionary<string, MonitoringNodeBase> _nameToNode;
        public MonitoringNodeBase(MonitoringNodeBase parent = null)
        {
            _parent = parent;
            _status = TextConstants.Unknown;
            _lastStatusUpdate = DateTime.Now;
            SubNodes = new ObservableCollection<MonitoringNodeBase>();
            Counters = new ObservableCollection<MonitoringCounterBaseViewModel>();
            _nameToCounter = new Dictionary<string, MonitoringCounterBaseViewModel>();
            _nameToNode = new Dictionary<string, MonitoringNodeBase>();
            SubNodes.CollectionChanged += Content_CollectionChanged;
            Counters.CollectionChanged += Content_CollectionChanged;
        }

        private void Content_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged();
        }

        public MonitoringNodeBase(string name, MonitoringNodeBase parent = null) : this(parent)
        {
            _name = name;
        }

        public MonitoringNodeBase(MonitoringNodeUpdate update, MonitoringNodeBase parent = null) : this(parent)
        {
            foreach (var counter in update.Counters)
            {
                if (_nameToCounter.ContainsKey(counter.Name))
                {
                    var counterViewModel = new MonitoringCounterBaseViewModel(counter, this);
                    Counters.Add(counterViewModel);
                    _nameToCounter[counter.Name] = counterViewModel;
                }
            }

            foreach (var subNodeUpdate in update.SubNodes)
            {
                var subNode = new MonitoringNodeBase(subNodeUpdate, this);
                SubNodes.Add(subNode);
                _nameToNode[subNode.Name] = subNode;
            }
        }
        //protected MonitoringNodeBase()
        //{

        //}
        public MonitoringNodeBase Parent
        {
            get { return _parent; }
            set { _parent = value; }
        }

        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string InternalStatus
        {
            get
            {
                if (_internalStatus == null)
                    _internalStatus = TextConstants.Unknown;
                return _internalStatus;
            }
            set
            {
                _internalStatus = value;
                UpdateStatus();
            }
        }

        public string Status
        {
            get
            {
                if (_status == null)
                    _status = TextConstants.Unknown;
                return _status;
            }
            set
            {
                if (_status != value)
                {
                    _lastStatusUpdate = DateTime.Now;
                }
                _status = value;
                _parent?.UpdateStatus();
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusDuration));
            }
        }

        public virtual void UpdateStatus()
        {
            string status = TextConstants.Ok;
            foreach (var counter in Counters)
            {
                if (counter.Status == TextConstants.Warning && status == TextConstants.Ok)
                    status = TextConstants.Warning;
                if (counter.Status == TextConstants.Error && (status == TextConstants.Ok || status == TextConstants.Warning))
                    status = TextConstants.Error;
            }
            foreach (var node in SubNodes)
            {
                if (node.Status == TextConstants.Warning && status == TextConstants.Ok)
                    status = TextConstants.Warning;
                if (node.Status == TextConstants.Error && (status == TextConstants.Ok || status == TextConstants.Warning))
                    status = TextConstants.Error;
            }

            if (InternalStatus == TextConstants.Warning && status == TextConstants.Ok)
                status = TextConstants.Warning;
            if (InternalStatus == TextConstants.Error && (status == TextConstants.Ok || status == TextConstants.Warning))
                status = TextConstants.Error;

            Status = status;
        }

        public string StatusDuration
        {
            get
            {
                if (_lastStatusUpdate.ToBinary() == 0)
                    _lastStatusUpdate = DateTime.Now;
                TimeSpan duration = DateTime.Now - _lastStatusUpdate;
                return duration.ToString(@"hh\:mm\:ss");
            }
        }
        private ObservableCollection<MonitoringNodeBase> _subNodes;

        public ObservableCollection<MonitoringNodeBase> SubNodes
        {
            get { return _subNodes; }
            set
            {
                foreach (var subNode in value)
                {
                    subNode._parent = this;
                }

                _subNodes = value;
                OnPropertyChanged(nameof(SubNodes));
            }
        }

        private ObservableCollection<MonitoringCounterBaseViewModel> _counters;

        public ObservableCollection<MonitoringCounterBaseViewModel> Counters
        {
            get { return _counters; }
            set
            {
                foreach (var counter in value)
                {
                    counter.Parent = this;
                }

                _counters = value;
                OnPropertyChanged(nameof(Counters));
            }
        }

        public void Update(MonitoringNodeUpdate updateNode, IMonitoringCounterStatusHandler handler)
        {
            if(_nameToCounter == null)
                _nameToCounter = new Dictionary<string, MonitoringCounterBaseViewModel>();

            foreach (var updateCounter in updateNode.Counters)
            {
                if (!_nameToCounter.ContainsKey(updateCounter.Name))
                {
                    var counter = new MonitoringCounterBaseViewModel(updateCounter, this);
                    Counters.Add(counter);
                    _nameToCounter[updateCounter.Name] = counter;
                }
                _nameToCounter[updateCounter.Name].Update(updateCounter, handler);
            }

            if (_nameToNode == null)
                _nameToNode = new Dictionary<string, MonitoringNodeBase>();

            foreach (var subNodeUpdate in updateNode.SubNodes)
            {
                if (!_nameToNode.ContainsKey(subNodeUpdate.Name))
                {
                    var subNode = new MonitoringNodeBase(subNodeUpdate, this);
                    Application.Current.Dispatcher.Invoke(delegate { SubNodes.Add(subNode); });

                    _nameToNode[subNode.Name] = subNode;
                }

                _nameToNode[subNodeUpdate.Name].Update(subNodeUpdate, handler);
            }

            OnPropertyChanged(nameof(StatusDuration));
        }
    }
}
