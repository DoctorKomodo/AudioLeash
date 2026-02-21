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

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new AudioLeashContext());
    }
}
