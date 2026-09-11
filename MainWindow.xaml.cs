using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GanttSquared.ViewModels;

namespace GanttSquared
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!ViewModel.ConfirmProceedPastUnsavedChanges("closing"))
                e.Cancel = true;
        }

        private void TaskTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            ViewModel.SelectedNode = e.NewValue as TaskNodeViewModel;
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ViewModel.SearchCommand.CanExecute(null))
                ViewModel.SearchCommand.Execute(null);
        }
    }
}
