namespace LHLauncher;
using System.Diagnostics;
static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 1. Check for existing instances
        var current = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName(current.ProcessName);

        // 2. If more than one instance, an instance is already running.
        foreach (var process in processes)
        {
            if (process.Id != current.Id) // Don't kill the current process
            {
                process.Kill(); // Kill the existing process
                process.WaitForExit(); // Optionally wait for the process to terminate
            }
        }
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new LHLauncher());
    }
}