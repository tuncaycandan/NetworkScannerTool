using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace NetworkScannerTool
{
    [XmlRoot("DeviceInventory")]
    public sealed class DeviceInventoryDocument
    {
        public int SchemaVersion { get; set; }

        [XmlElement("Device")]
        public List<InventoryDeviceRecord> Devices { get; set; }

        public DeviceInventoryDocument()
        {
            Devices = new List<InventoryDeviceRecord>();
        }
    }

    public sealed class InventoryDeviceRecord
    {
        public string Ip { get; set; }
        public string Hostname { get; set; }
        public string DisplayName { get; set; }
        public string Notes { get; set; }
        public string Tags { get; set; }
        public string Mac { get; set; }
        public string Vendor { get; set; }
        public string DeviceType { get; set; }
        public string Network { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public bool WasInLastScan { get; set; }
        public bool WasNewInLastScan { get; set; }
        public bool IsTrusted { get; set; }
        [XmlElement("HistoryEntry")]
        public List<InventoryHistoryRecord> History { get; set; } = new List<InventoryHistoryRecord>();
    }

    public sealed class InventoryHistoryRecord
    {
        public DateTime Time { get; set; }
        public string Event { get; set; }
        public string EventKind { get; set; }
        public string Status { get; set; }
        public string Hostname { get; set; }
        public string Mac { get; set; }
    }

    internal sealed class DeviceInventoryStore
    {
        private readonly string filePath;
        private readonly XmlSerializer serializer = new XmlSerializer(typeof(DeviceInventoryDocument));

        public DeviceInventoryStore()
        {
            filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NetworkScannerTool",
                "device-inventory.xml");
        }

        public List<InventoryDeviceRecord> Load()
        {
            if (!File.Exists(filePath))
                return new List<InventoryDeviceRecord>();

            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 5 * 1024 * 1024
                };

                using (var stream = File.OpenRead(filePath))
                using (var reader = XmlReader.Create(stream, settings))
                {
                    var document = serializer.Deserialize(reader) as DeviceInventoryDocument;
                    if (document == null || document.Devices == null)
                        return new List<InventoryDeviceRecord>();

                    // Earlier inventory files stored only devices from the last completed scan.
                    if (document.SchemaVersion < 1)
                    {
                        foreach (InventoryDeviceRecord device in document.Devices)
                        {
                            if (device != null)
                                device.WasInLastScan = true;
                        }
                    }

                    return document.Devices
                        .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Ip))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Load device inventory", filePath, ex);
                return new List<InventoryDeviceRecord>();
            }
        }

        public void Clear()
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            string temporaryPath = filePath + ".tmp";
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            string backupPath = filePath + ".bak";
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }

        public void Save(IEnumerable<DeviceInfo> devices)
        {
            string directory = Path.GetDirectoryName(filePath);
            Directory.CreateDirectory(directory);

            string temporaryPath = filePath + ".tmp";
            string backupPath = filePath + ".bak";
            var document = new DeviceInventoryDocument
            {
                SchemaVersion = 2,
                Devices = (devices ?? Enumerable.Empty<DeviceInfo>())
                    .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Ip))
                    .Select(d => new InventoryDeviceRecord
                    {
                        Ip = d.Ip,
                        Hostname = d.Hostname,
                        DisplayName = d.DisplayName,
                        Notes = d.Notes,
                        Tags = d.Tags,
                        Mac = d.Mac,
                        Vendor = d.Vendor,
                        DeviceType = d.DeviceType,
                        Network = d.Network,
                        FirstSeen = d.FirstSeen == DateTime.MinValue ? d.Seen : d.FirstSeen,
                        LastSeen = d.Seen,
                        WasInLastScan = d.WasInLastScan,
                        WasNewInLastScan = d.WasNewInLastScan,
                        IsTrusted = d.IsTrusted,
                        History = (d.History ?? new List<HistoryEntry>())
                            .Select(h => new InventoryHistoryRecord
                            {
                                Time = h.Time,
                                Event = h.Event,
                                EventKind = h.EventKind,
                                Status = h.Status,
                                Hostname = h.Hostname,
                                Mac = h.Mac
                            }).ToList()
                    })
                    .ToList()
            };

            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true }))
                    serializer.Serialize(writer, document);

                if (File.Exists(filePath))
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Replace(temporaryPath, filePath, backupPath, true);
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                }
                else
                {
                    File.Move(temporaryPath, filePath);
                }
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
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
