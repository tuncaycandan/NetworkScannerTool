using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkScannerTool
{
    internal sealed class ProcessResult
    {
        public int ExitCode { get; private set; }
        public string StandardOutput { get; private set; }
        public string StandardError { get; private set; }
        public bool TimedOut { get; private set; }

        public ProcessResult(int exitCode, string standardOutput, string standardError, bool timedOut)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
            TimedOut = timedOut;
        }
    }

    internal sealed class ProcessRunner
    {
        public async Task<ProcessResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Executable path is required.", "fileName");
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("timeout");

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = JoinArguments(arguments),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                if (!process.Start())
                    throw new InvalidOperationException("Process could not be started.");

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                Task exitTask = WaitForExitAsync(process, cancellationToken);
                Task timeoutTask = Task.Delay(timeout, cancellationToken);
                Task completed = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);

                if (completed != exitTask)
                {
                    bool cancelled = cancellationToken.IsCancellationRequested;
                    TryKill(process);
                    if (cancelled)
                        throw new OperationCanceledException(cancellationToken);
                    return new ProcessResult(-1, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false), true);
                }

                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
                return new ProcessResult(process.ExitCode, outputTask.Result, errorTask.Result, false);
            }
        }

        public Process StartElevated(string fileName, IEnumerable<string> arguments)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Executable path is required.", "fileName");

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = JoinArguments(arguments),
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            return Process.Start(psi);
        }

        public Process StartInteractive(string fileName, IEnumerable<string> arguments)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Executable path is required.", "fileName");

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = JoinArguments(arguments),
                UseShellExecute = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            return Process.Start(psi);
        }

        public void OpenHttpUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Only HTTP and HTTPS URLs are allowed.", "url");

            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }

        public static string QuoteArgument(string value)
        {
            if (value == null)
                return "\"\"";
            if (value.Length == 0)
                return "\"\"";

            bool needsQuotes = false;
            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c) || c == '"')
                {
                    needsQuotes = true;
                    break;
                }
            }
            if (!needsQuotes)
                return value;

            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string JoinArguments(IEnumerable<string> arguments)
        {
            if (arguments == null)
                return string.Empty;
            var result = new System.Text.StringBuilder();
            foreach (string argument in arguments)
            {
                if (result.Length > 0)
                    result.Append(' ');
                result.Append(QuoteArgument(argument));
            }
            return result.ToString();
        }

        private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
        {
            while (!process.HasExited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception ex)
            {
                AppLogger.Error("ProcessRunner kill", process.StartInfo.FileName, ex);
            }
        }
    }
}
