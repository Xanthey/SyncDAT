using System;
using System.IO;
using System.Windows.Forms;

namespace SyncDAT
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Set up error log file
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SyncDAT",
                "crash.log"
            );
            
            try
            {
                // Enable visual styles for modern Windows appearance
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                // Set up comprehensive exception handling
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    LogException(logPath, "UnhandledException", e.ExceptionObject as Exception);
                    MessageBox.Show(
                        $"A fatal error occurred. Details have been logged to:\n{logPath}\n\nError: {(e.ExceptionObject as Exception)?.Message}",
                        "Fatal Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                };

                Application.ThreadException += (s, e) =>
                {
                    LogException(logPath, "ThreadException", e.Exception);
                    MessageBox.Show(
                        $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails have been logged to:\n{logPath}\n\nThe application will continue running.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                };

                // Run the main form
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                LogException(logPath, "Main", ex);
                MessageBox.Show(
                    $"Failed to start application:\n\n{ex.Message}\n\nDetails have been logged to:\n{logPath}",
                    "Startup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void LogException(string logPath, string source, Exception? ex)
        {
            try
            {
                string dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string logEntry = $"\n\n=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {source} ===\n";
                logEntry += ex?.ToString() ?? "Unknown error";
                
                File.AppendAllText(logPath, logEntry);
            }
            catch
            {
                // If we can't log, at least don't crash trying
            }
        }
    }
}