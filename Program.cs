using System;
using System.Windows.Forms;
using System.IO;

// Suppress spell-check warning for project name 'AnonPDF'
#pragma warning disable SPELL
namespace AnonPDF
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {

            // Global exception handler for all UI threads
            Application.ThreadException += (sender, e) =>
            {
                LogUnhandledException(e.Exception, "ThreadException");
                ShowError(e.Exception);
            };

            // Handler for unhandled exceptions in non‑UI threads and background tasks
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Exception ex = e.ExceptionObject as Exception ?? new Exception("Unknown exception");
                LogUnhandledException(ex, "UnhandledException");
                ShowError(ex);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PDFForm());
        }

        // Log unhandled exceptions to AppData
        private static void LogUnhandledException(Exception ex, string exceptionType)
        {
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WINF",
                    "AnonPDF"
                );
                Directory.CreateDirectory(appDataDir);

                string logPath = Path.Combine(appDataDir, "error.log");

                File.AppendAllText(
                    logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{exceptionType}]\r\n{ex}\r\n\r\n"
                );
            }
            catch
            {
                // Swallow logging failures to avoid blocking the application
            }
        }

        // Show an error dialog (includes log file location)
        private static void ShowError(Exception ex)
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WINF",
                "AnonPDF"
            );
            string logPath = Path.Combine(appDataDir, "error.log");

            MessageBox.Show(
                string.Format(Properties.Resources.Err_UnhandledException, ex.Message, logPath),
                Properties.Resources.Title_CriticalAppError,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }

    }
}
#pragma warning restore SPELL
