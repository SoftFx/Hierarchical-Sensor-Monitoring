using System;
using System.Windows.Input;

namespace HSMClientWPFControls.ViewModel
{
    public class MultipleDelegateCommand : ICommand
    {
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public delegate bool ExecuteDelegate(object param, bool isCheckOnly);


        private readonly Action _execMethod;
        private readonly Func<bool> _canExecuteEvaluator;

        public MultipleDelegateCommand(Action executeMethod, Func<bool> canExecuteEvaluator)
        {
            _execMethod = executeMethod;
            _canExecuteEvaluator = canExecuteEvaluator;
        }

        #region Implementation of ICommand

        public void Execute(object parameter)
        {
            _execMethod?.Invoke();
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecuteEvaluator == null)
                return true;

            return _canExecuteEvaluator.Invoke();
        }

        #endregion
    }
}
