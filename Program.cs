using System;
using System.Windows.Forms;

namespace VSCodePortableInstaller
{
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                // Default: Full installation 
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InstallForm());
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message + "\n\n" + ex.StackTrace,
                    "VSCode Portable Installer Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
