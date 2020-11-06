using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
            Close();
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
        }
    }
}
