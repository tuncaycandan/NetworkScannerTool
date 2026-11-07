using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace NetworkScannerTool
{
    internal sealed class DeviceSignatureStore : IDisposable
    {
        private const long MaximumFileBytes = 16 * 1024 * 1024;
        private readonly HttpClient httpClient;
        private readonly string cacheDirectory;
        private readonly string bundledDirectory;
        private readonly Dictionary<string, string> ouiVendors =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DeviceSignature> deviceSignatures =
            new List<DeviceSignature>();
        private readonly object syncRoot = new object();

        public DeviceSignatureStore()
        {
            cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NetworkScannerTool");
            bundledDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NetworkScannerTool/1.5.3");
            LoadOui(Path.Combine(cacheDirectory, "oui.xml"));
            if (ouiVendors.Count == 0)
                LoadOui(Path.Combine(bundledDirectory, "oui.xml"));
            LoadDeviceSignatures(Path.Combine(cacheDirectory, "device-signatures.xml"));
            if (deviceSignatures.Count == 0)
                LoadDeviceSignatures(Path.Combine(bundledDirectory, "device-signatures.xml"));
        }

        public bool TryGetDeviceType(
            string vendor,
            string hostname,
            IEnumerable<int> openPorts,
            out string deviceType)
        {
            deviceType = null;
            var ports = new HashSet<int>(openPorts ?? Enumerable.Empty<int>());
            string normalizedVendor = (vendor ?? string.Empty).ToUpperInvariant();
            string normalizedHostname = (hostname ?? string.Empty).ToUpperInvariant();

            lock (syncRoot)
            {
                int bestScore = 0;
                foreach (DeviceSignature signature in deviceSignatures)
                {
                    bool vendorMatch = signature.Vendors.Any(x => normalizedVendor.Contains(x));
                    bool hostnameMatch = signature.Hostnames.Any(x => normalizedHostname.Contains(x));
                    bool requiredAll = signature.RequiredAll.Count == 0 ||
                        signature.RequiredAll.All(ports.Contains);
                    bool requiredAny = signature.RequiredAny.Count == 0 ||
                        signature.RequiredAny.Any(ports.Contains);

                    if (!requiredAll || !requiredAny ||
                        (!vendorMatch && !hostnameMatch && signature.TcpPorts.All(x => !ports.Contains(x))))
                        continue;

                    int score = signature.Priority;
                    if (vendorMatch) score += 20;
                    if (hostnameMatch) score += 15;
                    if (signature.RequiredAny.Any(ports.Contains)) score += 10;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        deviceType = signature.Type;
                    }
                }
            }

            return deviceType != null;
        }

        public bool TryGetVendor(string mac, out string vendor)
        {
            vendor = null;
            if (string.IsNullOrWhiteSpace(mac))
                return false;

            string normalized = NormalizeMac(mac);
            if (normalized.Length < 6)
                return false;

            lock (syncRoot)
                return ouiVendors.TryGetValue(normalized.Substring(0, 6), out vendor);
        }

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(cacheDirectory);
            await RefreshFileAsync(
                "https://raw.githubusercontent.com/tuncaycandan/NetworkScannerTool/main/data/oui.xml",
                "oui.xml",
                "OUIDatabase",
                cancellationToken).ConfigureAwait(false);
            await RefreshFileAsync(
                "https://raw.githubusercontent.com/tuncaycandan/NetworkScannerTool/main/data/device-signatures.xml",
                "device-signatures.xml",
                "DeviceSignatures",
                cancellationToken).ConfigureAwait(false);

            LoadOui(Path.Combine(cacheDirectory, "oui.xml"));
            LoadDeviceSignatures(Path.Combine(cacheDirectory, "device-signatures.xml"));
        }

        private void LoadDeviceSignatures(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumFileBytes
                };
                var loaded = new List<DeviceSignature>();
                var document = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(path, settings))
                    document.Load(reader);

                foreach (XmlNode node in document.SelectNodes("/DeviceSignatures/Signature"))
                {
                    var signature = new DeviceSignature
                    {
                        Type = node.Attributes["Type"] == null ? null : node.Attributes["Type"].Value,
                        Priority = ParseInt(node.Attributes["Priority"] == null ? null : node.Attributes["Priority"].Value)
                    };
                    signature.Vendors.AddRange(ReadTokens(node, "Vendors"));
                    signature.Hostnames.AddRange(ReadTokens(node, "Hostnames"));
                    signature.TcpPorts.AddRange(ReadPorts(node, "TcpPorts"));
                    signature.RequiredAny.AddRange(ReadPorts(node, "RequiredAny"));
                    signature.RequiredAll.AddRange(ReadPorts(node, "RequiredAll"));
                    if (!string.IsNullOrWhiteSpace(signature.Type))
                        loaded.Add(signature);
                }

                lock (syncRoot)
                {
                    deviceSignatures.Clear();
                    deviceSignatures.AddRange(loaded);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Device signature load", path + ": " + ex.Message);
            }
        }

        private static IEnumerable<string> ReadTokens(XmlNode node, string name)
        {
            XmlNode child = node.SelectSingleNode(name);
            return child == null
                ? Enumerable.Empty<string>()
                : child.InnerText.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().ToUpperInvariant());
        }

        private static IEnumerable<int> ReadPorts(XmlNode node, string name)
        {
            return ReadTokens(node, name).Select(ParseInt).Where(x => x > 0);
        }

        private static int ParseInt(string value)
        {
            int result;
            return int.TryParse(value, out result) ? result : 0;
        }

        private sealed class DeviceSignature
        {
            public readonly List<string> Vendors = new List<string>();
            public readonly List<string> Hostnames = new List<string>();
            public readonly List<int> TcpPorts = new List<int>();
            public readonly List<int> RequiredAny = new List<int>();
            public readonly List<int> RequiredAll = new List<int>();
            public string Type;
            public int Priority;
        }

        private async Task RefreshFileAsync(
            string url,
            string fileName,
            string expectedRoot,
            CancellationToken cancellationToken)
        {
            string destination = Path.Combine(cacheDirectory, fileName);
            string metadataPath = destination + ".meta";
            try
            {
                string remoteVersion = await GetRemoteVersionAsync(url, cancellationToken)
                    .ConfigureAwait(false);

                if (File.Exists(destination) &&
                    !string.IsNullOrWhiteSpace(remoteVersion) &&
                    string.Equals(
                        remoteVersion,
                        ReadMetadata(metadataPath),
                        StringComparison.Ordinal))
                {
                    return;
                }

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    using (HttpResponseMessage response = await httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength.HasValue &&
                            response.Content.Headers.ContentLength.Value > MaximumFileBytes)
                            return;

                        string xml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (Encoding.UTF8.GetByteCount(xml) > MaximumFileBytes ||
                            !IsValidDocument(xml, expectedRoot))
                            return;

                        string temporary = destination + ".tmp";
                        string backup = destination + ".bak";
                        File.WriteAllText(temporary, xml, new UTF8Encoding(false));
                        if (File.Exists(destination))
                            File.Replace(temporary, destination, backup, true);
                        else
                            File.Move(temporary, destination);

                        if (!string.IsNullOrWhiteSpace(remoteVersion))
                            File.WriteAllText(metadataPath, remoteVersion, Encoding.UTF8);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Device signature update", fileName + ": " + ex.Message);
            }
        }

        private async Task<string> GetRemoteVersionAsync(
            string url,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Head, url))
            using (HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    return string.Empty;

                string etag = response.Headers.ETag == null
                    ? string.Empty
                    : response.Headers.ETag.ToString();
                string modified = response.Content.Headers.LastModified.HasValue
                    ? response.Content.Headers.LastModified.Value.UtcDateTime.ToString("O")
                    : string.Empty;

                return etag + "\n" + modified;
            }
        }

        private static string ReadMetadata(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsValidDocument(string xml, string expectedRoot)
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumFileBytes
                };
                using (var reader = XmlReader.Create(new StringReader(xml), settings))
                {
                    var document = new XmlDocument { XmlResolver = null };
                    document.Load(reader);
                    return document.DocumentElement != null &&
                           string.Equals(document.DocumentElement.Name, expectedRoot, StringComparison.Ordinal);
                }
            }
            catch
            {
                return false;
            }
        }

        private void LoadOui(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumFileBytes
                };
                var loaded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var reader = XmlReader.Create(path, settings))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.Name != "OUI")
                            continue;
                        string prefix = NormalizeMac(reader.GetAttribute("Prefix"));
                        string vendor = reader.GetAttribute("Vendor");
                        if (prefix.Length >= 6 && !string.IsNullOrWhiteSpace(vendor))
                            loaded[prefix.Substring(0, 6)] = vendor.Trim();
                    }
                }

                lock (syncRoot)
                {
                    ouiVendors.Clear();
                    foreach (var pair in loaded)
                        ouiVendors[pair.Key] = pair.Value;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Device signature load", path + ": " + ex.Message);
            }
        }

        private static string NormalizeMac(string mac)
        {
            var builder = new StringBuilder(12);
            foreach (char character in mac ?? string.Empty)
            {
                if (Uri.IsHexDigit(character))
                    builder.Append(char.ToUpperInvariant(character));
            }
            return builder.ToString();
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }
}
