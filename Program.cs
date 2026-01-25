using System;
using System.Windows.Forms;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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
            LicenseManager.Initialize(AppDomain.CurrentDomain.BaseDirectory);
            if (!ValidateRequiredLicenseFiles(out string licenseError))
            {
                MessageBox.Show(
                    licenseError,
                    Properties.Resources.Title_Error,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
            var splash = new SplashForm();
            var mainForm = new PDFForm(splash);
            splash.Owner = mainForm;
            splash.Show();
            Application.DoEvents();
            mainForm.FormClosed += (_, __) =>
            {
                if (!splash.IsDisposed)
                {
                    splash.Close();
                }
            };

            Application.Run(mainForm);
        }

        private static bool ValidateRequiredLicenseFiles(out string errorMessage)
        {
            var issues = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string configPath = Path.Combine(baseDir, "config.json");
            if (!File.Exists(configPath))
            {
                issues.Add(string.Format(Properties.Resources.License_ConfigMissing, configPath));
            }
            else
            {
                try
                {
                    JObject.Parse(File.ReadAllText(configPath));
                }
                catch (Exception ex)
                {
                    issues.Add(string.Format(Properties.Resources.License_ConfigInvalid, ex.Message));
                }
            }

            var config = LicenseManager.Config;
            string licensePath = config?.ResolveLicensePath(baseDir) ?? Path.Combine(baseDir, "license.json");
            if (!File.Exists(licensePath))
            {
                issues.Add(string.Format(Properties.Resources.License_FileMissing, licensePath));
            }

            string publicKeyPath = config?.ResolvePublicKeyPath(baseDir) ?? Path.Combine(baseDir, "license_public.xml");
            if (!File.Exists(publicKeyPath))
            {
                issues.Add(string.Format(Properties.Resources.License_PublicKeyMissing, publicKeyPath));
            }

            var info = LicenseManager.Current;
            if (info == null || !info.IsSignatureValid || info.Payload == null)
            {
                string detail = info?.Error;
                if (string.IsNullOrWhiteSpace(detail))
                {
                    detail = "-";
                }
                issues.Add(string.Format(Properties.Resources.License_Invalid, detail));
            }

            if (issues.Count > 0)
            {
                errorMessage = string.Format(
                    Properties.Resources.License_StartupError,
                    string.Join(Environment.NewLine, issues));
                return false;
            }

            errorMessage = string.Empty;
            return true;
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
