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

            // TODO: Run(new AudioLeashContext()) once AudioLeashContext is implemented
            // as part of the NAudio migration (Task 4+).
        }
    }
}
