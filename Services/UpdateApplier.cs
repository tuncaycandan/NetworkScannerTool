using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace NetworkScannerTool
{
    internal static class UpdateApplier
    {
        public static int Apply(string[] args)
        {
            if (args == null || args.Length != 5 || args[0] != "--apply-update")
                return 2;

            int parentPid;
            if (!int.TryParse(args[3], out parentPid) || parentPid <= 0)
                return 3;

            string tempPath = Path.GetFullPath(args[1]);
            string currentPath = Path.GetFullPath(args[2]);
            try
            {
                using (Process parent = Process.GetProcessById(parentPid))
                    parent.WaitForExit(30000);
                if (File.Exists(tempPath) == false || File.Exists(currentPath) == false)
                    return 4;
                if (!IsPeFile(tempPath))
                    return 5;
                if (!string.Equals(SecureUpdateService.ComputeSha256(tempPath), args[4].Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                    return 6;
                string backup = currentPath + ".backup";
                File.Replace(tempPath, currentPath, backup, true);
                Process.Start(new ProcessStartInfo
                {
                    FileName = currentPath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(currentPath)
                });
                return 0;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Apply update", currentPath, ex);
                return 1;
            }
        }

        private static bool IsPeFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                int first = stream.ReadByte();
                int second = stream.ReadByte();
                return first == 'M' && second == 'Z';
            }
        }
    }
}
