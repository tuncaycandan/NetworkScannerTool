using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkScannerTool
{
    internal sealed class UpdateDownloadProgress
    {
        public long BytesReceived { get; private set; }
        public long? TotalBytes { get; private set; }
        public bool IsVerifying { get; private set; }

        public UpdateDownloadProgress(long bytesReceived, long? totalBytes, bool isVerifying)
        {
            BytesReceived = bytesReceived;
            TotalBytes = totalBytes;
            IsVerifying = isVerifying;
        }
    }

    internal sealed class SecureUpdateService
    {
        private readonly HttpClient client;

        public SecureUpdateService()
        {
            client = new HttpClient(new HttpClientHandler())
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NetworkScannerTool-Updater/1.0");
        }

        public async Task<string> DownloadVerifiedPackageAsync(
            Uri packageUri,
            string expectedSha256,
            string destinationPath,
            CancellationToken cancellationToken,
            IProgress<UpdateDownloadProgress> progress = null)
        {
            if (packageUri == null || packageUri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("Update packages must be downloaded over HTTPS.", "packageUri");
            if (string.IsNullOrWhiteSpace(expectedSha256))
                throw new ArgumentException("A SHA-256 digest is required.", "expectedSha256");
            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("Destination path is required.", "destinationPath");

            string expected = NormalizeHash(expectedSha256);
            if (expected.Length != 64)
                throw new ArgumentException("A 64-character SHA-256 digest is required.", "expectedSha256");

            string fullDestination = Path.GetFullPath(destinationPath);
            string directory = Path.GetDirectoryName(fullDestination);
            Directory.CreateDirectory(directory);
            string tempPath = fullDestination + ".download-" + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                using (HttpResponseMessage response = await client.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        long? totalBytes = response.Content.Headers.ContentLength;
                        long bytesReceived = 0;
                        byte[] buffer = new byte[81920];
                        DateTime lastProgressReport = DateTime.UtcNow;
                        int bytesRead;

                        while ((bytesRead = await input.ReadAsync(
                            buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(
                                buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                            bytesReceived += bytesRead;

                            if (progress != null &&
                                (DateTime.UtcNow - lastProgressReport >= TimeSpan.FromMilliseconds(150) ||
                                 (totalBytes.HasValue && bytesReceived >= totalBytes.Value)))
                            {
                                progress.Report(new UpdateDownloadProgress(bytesReceived, totalBytes, false));
                                lastProgressReport = DateTime.UtcNow;
                            }
                        }

                        if (progress != null)
                            progress.Report(new UpdateDownloadProgress(bytesReceived, totalBytes, true));
                    }
                }

                string actual = ComputeSha256(tempPath);
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Downloaded update hash does not match the trusted digest.");

                if (File.Exists(fullDestination))
                    File.Replace(tempPath, fullDestination, fullDestination + ".backup", true);
                else
                    File.Move(tempPath, fullDestination);
                return fullDestination;
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        public static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string NormalizeHash(string hash)
        {
            return hash.Trim().Replace(" ", string.Empty).Replace(":", string.Empty).ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
