using System;
using System.Collections.Generic;

namespace NetworkScannerTool
{
    internal sealed class AdapterInfo
    {
        public string Name;
        public string Ip;
        public string Mask;
        public string Gateway;
        public string Mac;
    }

    internal sealed class DeviceInfo
    {
        public string Ip;
        public string Hostname;
        public string DisplayName;
        public string Notes;
        public string Tags;
        public string Mac;
        public string Vendor;
        public string DeviceType;
        public string Response;
        public string Status;
        public string Network;
        public DateTime FirstSeen;
        public DateTime Seen;
        public bool WasInLastScan;
        public bool WasNewInLastScan;
        public bool IsTrusted;
        public List<HistoryEntry> History = new List<HistoryEntry>();
        public string PendingHistoryEvent;
    }

    internal sealed class PortResult
    {
        public int Port;
        public string Service;
        public bool Open;
    }

    internal sealed class HistoryEntry
    {
        public DateTime Time;
        public string Event;
        public string EventKind;
        public string Status;
        public string Hostname;
        public string Mac;
    }
}
