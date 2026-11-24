using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using dlss_updater.Services;

namespace dlss_updater
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // An unexpected error in a button handler would otherwise close
            // the app without a word. Show it and keep running instead.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += (sender, args) => args.SetObserved();

            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;

            try
            {
                string message =
                    Translator.T("UnexpectedError") + Environment.NewLine + Environment.NewLine + e.Exception.Message;

                if (MainWindow != null && MainWindow.IsVisible)
                {
                    MessageBox.Show(MainWindow, message, "DLSS Updater", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show(message, "DLSS Updater", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch
            {
                // Nothing more can be done if even the message box fails.
            }
        }
    }
}
