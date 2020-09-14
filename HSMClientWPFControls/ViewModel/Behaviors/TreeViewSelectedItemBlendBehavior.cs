using System.Windows;
using System.Windows.Controls;
using System.Windows.Interactivity;

namespace HSMClientWPFControls.ViewModel.Behaviors
{
    public class TreeViewSelectedItemBlendBehavior : Behavior<TreeView>
    {
        //dependency property
        public static readonly DependencyProperty SelectedItemProperty = 
            DependencyProperty.Register("SelectedItem", typeof(object),
                typeof(TreeViewSelectedItemBlendBehavior),
                new FrameworkPropertyMetadata(null) { BindsTwoWayByDefault = true });

        //property wrapper
        public object SelectedItem
        {
            get { return (object) GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            this.AssociatedObject.SelectedItemChanged += TreeView_SelectedItemChanged;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (this.AssociatedObject != null)
                this.AssociatedObject.SelectedItemChanged -= TreeView_SelectedItemChanged;
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            this.SelectedItem = e.NewValue;
        }
    }
}
