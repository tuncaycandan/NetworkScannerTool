using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkScannerTool
{
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

        public async Task<string> DownloadVerifiedPackageAsync(Uri packageUri, string expectedSha256, string destinationPath, CancellationToken cancellationToken)
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
                        await input.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
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
