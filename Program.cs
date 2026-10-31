using System;
using System.Windows.Forms;

namespace NetworkScannerTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--apply-update")
            {
                Environment.ExitCode = UpdateApplier.Apply(args);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
