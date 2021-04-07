using System;
using System.Windows.Input;

namespace HSMClientWPFControls.ViewModel
{
    public class SingleDelegateCommand : ICommand
    {
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public delegate bool ExecuteDelegate(object param, bool isCheckOnly);

        private readonly ExecuteDelegate _execDelegate;

        public SingleDelegateCommand(ExecuteDelegate executeDelegate)
        {
            _execDelegate = executeDelegate;
        }

        #region Implementation of ICommand

        public void Execute(object parameter)
        {
            _execDelegate?.Invoke(parameter, false);
        }

        public bool CanExecute(object parameter)
        {
            if (_execDelegate != null)
                return _execDelegate(parameter, true);
            return false;
        }

        #endregion
    }
}
