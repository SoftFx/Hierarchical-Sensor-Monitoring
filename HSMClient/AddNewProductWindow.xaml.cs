using System.Collections.Generic;
using System.Windows;

namespace HSMClient
{
    /// <summary>
    /// Interaction logic for AddNewProductWindow.xaml
    /// </summary>
    public partial class AddNewProductWindow : Window
    {
        private readonly List<string> _existingProductsNames;
        public string NewProductName { get; private set; }
        public AddNewProductWindow(List<string> currentNamesList)
        {
            _existingProductsNames = currentNamesList;
            InitializeComponent();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }


        private void ButtonAdd_Click(object sender, RoutedEventArgs e)
        {
            string newName = ProductNameBox.Text.Trim();
            if (_existingProductsNames.Contains(newName))
            {
                MessageBox.Show($"Product with name {newName} already exists!");
                return;
            }

            NewProductName = newName;
            DialogResult = true;
        }

        private void ProductNameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }
    }
}
