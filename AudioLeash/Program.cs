using System;
using System.Windows.Forms;

namespace AudioLeash
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Run without a visible form — only a system tray icon
            Application.Run(new AudioSwitcherContext());
        }
    }
}
