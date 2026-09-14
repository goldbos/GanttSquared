using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace GanttSquared
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            base.OnStartup(e);

            // No StartupUri in App.xaml - the startup window is chosen here instead, so
            // `--test` can launch TestEditWindow without ever touching MainWindow.xaml.
            Window window = e.Args.Contains("--test") ? new TestEditWindow() : new MainWindow();
            MainWindow = window;
            window.Show();
        }

        // A safety net, not a fix: an unhandled exception on the UI thread (the vast majority
        // of them here - RelayCommand executions, bindings, event handlers) would otherwise
        // close the whole app instantly with no explanation and no chance to save. This logs
        // the details, tells the user plainly what happened, and marks the exception handled
        // so the app - and any unsaved work - survives.
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            TryLogCrash(e.Exception);

            MessageBox.Show(
                $"Something went wrong and that action couldn't complete:\n\n{e.Exception.Message}\n\n" +
                "The app will stay open so you don't lose your work, but consider saving under a new " +
                "file name and restarting if this keeps happening.",
                "GanttSquared - Unexpected Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        private static void TryLogCrash(Exception ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GanttSquared");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // Logging is best-effort; a failure here shouldn't itself surface to the user.
            }
        }
    }

}
