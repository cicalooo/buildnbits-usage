namespace BuildnBits.Usage.Tray;

internal static class Program
{
    private const string MutexName = @"Local\BuildnBits.Usage.Tray";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        if (!created)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var context = new TrayApplicationContext();
        Application.Run(context);
    }
}
