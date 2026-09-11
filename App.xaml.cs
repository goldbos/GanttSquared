using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

namespace GanttSquared
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // No StartupUri in App.xaml - the startup window is chosen here instead, so
            // `--test` can launch TestEditWindow without ever touching MainWindow.xaml.
            Window window = e.Args.Contains("--test") ? new TestEditWindow() : new MainWindow();
            MainWindow = window;
            window.Show();
        }
    }

}
