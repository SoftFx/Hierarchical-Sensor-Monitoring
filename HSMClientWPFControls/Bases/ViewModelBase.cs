using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace HSMClientWPFControls.Bases
{
    public abstract class ViewModelBase : DependencyObject, INotifyPropertyChanged, IDisposable
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
                    if (_model != null)
                        _model.PropertyChanged -= model_PropertyChanged;

                    _model?.Dispose();
                }

                // Dispose unmanaged resources here...

                // Set large fields to null here...

                // Mark as disposed.
                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~ViewModelBase()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion

        protected ViewModelBase(ModelBase model = null)
        {
            if (model != null)
            {
                model.PropertyChanged += model_PropertyChanged;
                _model = model;
            }
            _modelPropertyToProperty = new Dictionary<string, string>();
        }



        protected Dictionary<string, string> _modelPropertyToProperty;

        protected void RegisterProperty(string modelProperty, string viewModelProperty)
        {
            _modelPropertyToProperty[modelProperty] = viewModelProperty;
        }

        private void model_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_modelPropertyToProperty.ContainsKey(e.PropertyName))
                OnPropertyChanged(_modelPropertyToProperty[e.PropertyName]);
            else
                OnPropertyChanged(e.PropertyName);
        }

        private ModelBase _model;

        public ModelBase Model
        {
            get => _model;
            set
            {
                if (_model != null)
                    _model.PropertyChanged -= model_PropertyChanged;
                _model = value;
                if (_model != null)
                    _model.PropertyChanged += model_PropertyChanged;
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
