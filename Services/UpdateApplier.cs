using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetworkScannerTool
{
    internal static class UpdateApplier
    {
        public static int Apply(string[] args)
        {
            if (args == null || args.Length != 5 || args[0] != "--apply-update")
                return 2;

            int parentPid;
            if (!int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out parentPid) || parentPid <= 0)
                return 3;

            string tempPath = Path.GetFullPath(args[1]);
            string currentPath = Path.GetFullPath(args[2]);
            string expectedSha256 = args[4] == null ? string.Empty : args[4].Trim().ToLowerInvariant();
            string scriptPath = Path.Combine(
                Path.GetTempPath(),
                "NetworkScannerTool_Apply_" + Guid.NewGuid().ToString("N") + ".ps1");

            try
            {
                if (!WaitForProcessToExit(parentPid, 30000))
                    return 7;

                if (!File.Exists(tempPath) || !File.Exists(currentPath))
                    return 4;
                if (!IsPeFile(tempPath))
                    return 5;
                if (!string.Equals(
                    SecureUpdateService.ComputeSha256(tempPath),
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return 6;
                }

                string script = BuildReplacementScript(
                    scriptPath,
                    tempPath,
                    currentPath,
                    parentPid,
                    Process.GetCurrentProcess().Id);
                File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.GetEnvironmentVariable("WINDIR") + "\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
                    Arguments = JoinArguments(new[]
                    {
                        "-NoLogo",
                        "-NoProfile",
                        "-NonInteractive",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-File",
                        scriptPath
                    }),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(currentPath)
                });

                // PowerShell betiği bu updater süreci kapandıktan sonra dosya değişimini yapar.
                return 0;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Apply update", currentPath, ex);
                TryDelete(scriptPath);
                return 1;
            }
        }

        private static bool WaitForProcessToExit(int processId, int timeoutMilliseconds)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    return process.WaitForExit(timeoutMilliseconds);
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private static string BuildReplacementScript(
            string scriptPath,
            string tempPath,
            string currentPath,
            int parentPid,
            int updaterPid)
        {
            string source = PowerShellLiteral(tempPath);
            string target = PowerShellLiteral(currentPath);
            string backup = PowerShellLiteral(currentPath + ".backup");
            string script = PowerShellLiteral(scriptPath);
            string workingDirectory = PowerShellLiteral(Path.GetDirectoryName(currentPath));

            return @"
$ErrorActionPreference = 'Stop'
$source = " + source + @"
$target = " + target + @"
$backup = " + backup + @"
$script = " + script + @"
$workingDirectory = " + workingDirectory + @"
$parentPid = " + parentPid.ToString(CultureInfo.InvariantCulture) + @"
$updaterPid = " + updaterPid.ToString(CultureInfo.InvariantCulture) + @"

# Hem eski uygulamanın hem de bu updater sürecinin dosya kilitlerini bırakmasını bekle.
while ((Get-Process -Id $parentPid -ErrorAction SilentlyContinue) -or
       (Get-Process -Id $updaterPid -ErrorAction SilentlyContinue)) {
    Start-Sleep -Milliseconds 250
}

for ($attempt = 0; $attempt -lt 30; $attempt++) {
    try {
        if (-not (Test-Path -LiteralPath $source)) { throw 'Update package is missing.' }
        if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
        Move-Item -LiteralPath $target -Destination $backup -Force
        Move-Item -LiteralPath $source -Destination $target -Force
        if (-not (Test-Path -LiteralPath $target)) { throw 'Updated executable was not created.' }
        $newProcess = Start-Process -FilePath $target -WorkingDirectory $workingDirectory -PassThru
        if (-not $newProcess) { throw 'Updated executable could not be started.' }

        # Yeni uygulama başlatıldıktan sonra eski EXE yedeğini temizle.
        # Silme başarısız olursa güncelleme yine başarılı kabul edilir; yedek
        # sonraki güncellemede tekrar temizlenebilir.
        for ($cleanupAttempt = 0; $cleanupAttempt -lt 10; $cleanupAttempt++) {
            try {
                if (Test-Path -LiteralPath $backup) {
                    Remove-Item -LiteralPath $backup -Force -ErrorAction Stop
                }
                break
            }
            catch {
                Start-Sleep -Milliseconds 250
            }
        }

        Remove-Item -LiteralPath $script -Force -ErrorAction SilentlyContinue
        exit 0
    }
    catch {
        try {
            if ((-not (Test-Path -LiteralPath $target)) -and (Test-Path -LiteralPath $backup)) {
                Move-Item -LiteralPath $backup -Destination $target -Force
            }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
}

Remove-Item -LiteralPath $script -Force -ErrorAction SilentlyContinue
exit 1
";
        }

        private static string PowerShellLiteral(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }

        private static string JoinArguments(string[] arguments)
        {
            var result = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (result.Length > 0)
                    result.Append(' ');
                result.Append(QuoteArgument(argument));
            }
            return result.ToString();
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";
            if (value.IndexOfAny(new[] { ' ', '\"', '\t' }) < 0)
                return value;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
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

