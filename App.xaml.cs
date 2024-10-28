using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PSTInsight
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        #region Application Startup

        /// <summary>
        /// Overrides the default startup behavior of the application.
        /// Sets up global exception handlers for different types of unhandled exceptions.
        /// </summary>
        /// <param name="e">Startup event arguments</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            // Call the base implementation first
            base.OnStartup(e);

            // Set up global exception handlers
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            Console.WriteLine("Application startup complete. Global exception handlers set up.");
        }

        #endregion

        #region Exception Handlers

        /// <summary>
        /// Handles unhandled exceptions at the AppDomain level.
        /// These are typically the most severe and unexpected exceptions.
        /// </summary>
        /// <param name="sender">The source of the unhandled exception</param>
        /// <param name="e">Contains information about the exception</param>
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogException("Unhandled Exception", e.ExceptionObject as Exception);
            Console.WriteLine("Unhandled AppDomain exception occurred.");
        }

        /// <summary>
        /// Handles unhandled exceptions in the UI thread.
        /// These are typically exceptions that occur during event handlers or other UI operations.
        /// </summary>
        /// <param name="sender">The source of the unhandled exception</param>
        /// <param name="e">Contains information about the exception and allows marking it as handled</param>
        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException("Unhandled UI Exception", e.Exception);
            e.Handled = true; // Prevents the application from crashing
            Console.WriteLine("Unhandled UI exception occurred and was marked as handled.");
        }

        /// <summary>
        /// Handles unobserved task exceptions.
        /// These are exceptions that occur in Tasks that have no await or ContinueWith handlers.
        /// </summary>
        /// <param name="sender">The source of the unobserved task exception</param>
        /// <param name="e">Contains information about the exception and allows marking it as observed</param>
        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException("Unobserved Task Exception", e.Exception);
            e.SetObserved(); // Prevents the exception from being re-thrown
            Console.WriteLine("Unobserved task exception occurred and was marked as observed.");
        }

        #endregion

        #region Logging

        /// <summary>
        /// Logs the exception details to a file and displays a message box to the user.
        /// If logging fails, it falls back to displaying the error message directly in a message box.
        /// </summary>
        /// <param name="title">A title describing the type of exception</param>
        /// <param name="exception">The exception object to be logged</param>
        private void LogException(string title, Exception exception)
        {
            try
            {
                // Prepare the log message
                string message = $"{DateTime.Now}: {title}\n{exception}\n\n";

                // Append the message to the log file
                File.AppendAllText("error_log.txt", message);

                // Inform the user about the error
                _ = MessageBox.Show("An error occurred. Please check error_log.txt for details.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                Console.WriteLine($"Exception logged: {title}");
            }
            catch (Exception loggingException)
            {
                // If we can't log the error, just show a message box with the original error
                _ = MessageBox.Show($"A critical error occurred: {exception.Message}", "Critical Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                Console.WriteLine($"Failed to log exception. Logging error: {loggingException.Message}");
            }
        }

        #endregion
    }
}