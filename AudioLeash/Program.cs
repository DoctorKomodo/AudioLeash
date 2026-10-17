#nullable enable
using System.Threading;
using System.Windows.Forms;

namespace AudioLeash;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Prevent duplicate instances. The Global\ prefix makes the mutex session-global
        // so it works correctly across UAC elevation boundaries.
        using var mutex = new Mutex(initiallyOwned: false, "Global\\AudioLeash");
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance was force-terminated without releasing the mutex.
            // The OS has granted us ownership — proceed normally.
            acquired = true;
        }

        if (!acquired)
            return; // Another instance is already running — exit silently.

        // Per-monitor DPI awareness: without it Windows bitmap-stretches the tray menu (blurry)
        // on any display above 100% scaling. Must be set before the first window is created.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Follow the Windows light/dark theme in the settings window. The tray menu sets
        // its own renderer (DarkMenuRenderer), so it is unaffected. Experimental since .NET 9.
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
        Application.Run(new AudioLeashContext());
    }
}
