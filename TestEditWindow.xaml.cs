using System;
using System.Windows;
using GanttSquared.ViewModels;

namespace GanttSquared
{
    public partial class TestEditWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        public TestEditWindow()
        {
            InitializeComponent();

            var vm = new MainViewModel();
            DataContext = vm;
            vm.Properties.Applied += (_, _) => SavedFlag.Text = $"Saved at {DateTime.Now:T}";
        }

        private void TaskTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            ViewModel.SelectedNode = e.NewValue as TaskNodeViewModel;
        }
    }
}
