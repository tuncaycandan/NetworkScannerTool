using System;
using System.Collections.Generic;
using System.Net;

namespace NetworkScannerTool
{
    internal static class IpRangeService
    {
        public static bool TryParseCidr(string value, out uint network, out uint broadcast)
        {
            network = 0;
            broadcast = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Trim().Split('/');
            if (parts.Length != 2)
                return false;

            IPAddress address;
            int prefix;
            if (!IPAddress.TryParse(parts[0], out address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
                !int.TryParse(parts[1], out prefix) || prefix < 0 || prefix > 32)
                return false;

            uint ip = ToUInt(address.GetAddressBytes());
            uint mask = prefix == 0 ? 0U : uint.MaxValue << (32 - prefix);
            network = ip & mask;
            broadcast = network | ~mask;
            return true;
        }

        public static ulong Count(string value)
        {
            uint network;
            uint broadcast;
            if (!TryParseCidr(value, out network, out broadcast))
                return 0;
            return (ulong)broadcast - network + 1UL;
        }

        public static IEnumerable<string> Enumerate(string value)
        {
            uint network;
            uint broadcast;
            if (!TryParseCidr(value, out network, out broadcast))
                yield break;

            for (uint current = network; ; current++)
            {
                yield return new IPAddress(ToBytes(current)).ToString();
                if (current == broadcast)
                    break;
            }
        }

        private static uint ToUInt(byte[] bytes)
        {
            return ((uint)bytes[0] << 24) |
                   ((uint)bytes[1] << 16) |
                   ((uint)bytes[2] << 8) |
                   bytes[3];
        }

        private static byte[] ToBytes(uint value)
        {
            return new[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            };
        }
    }
}
