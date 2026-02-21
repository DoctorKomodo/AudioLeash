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
            Application.Run(new AudioLeashContext());
        }
    }
}
