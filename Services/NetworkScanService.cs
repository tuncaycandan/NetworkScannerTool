using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkScannerTool
{
    internal sealed class NetworkScanService
    {
        private readonly Func<string, Dictionary<string, string>, string> macResolver;
        private readonly Func<string> searchingText;
        private readonly Func<string> detectingText;
        private readonly Func<string> activeText;

        public NetworkScanService(
            Func<string, Dictionary<string, string>, string> macResolver,
            Func<string> searchingText,
            Func<string> detectingText,
            Func<string> activeText)
        {
            this.macResolver = macResolver;
            this.searchingText = searchingText;
            this.detectingText = detectingText;
            this.activeText = activeText;
        }

        public async Task<DeviceInfo> ScanTargetAsync(
            string ip,
            string network,
            Dictionary<string, string> localMacByIp,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PingReply reply = null;
            try
            {
                using (var ping = new Ping())
                    reply = await ping.SendPingAsync(ip, 220).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (reply == null || reply.Status != IPStatus.Success)
                return null;

            return new DeviceInfo
            {
                Ip = ip,
                Hostname = ip,
                Mac = macResolver(ip, localMacByIp),
                Vendor = searchingText(),
                DeviceType = detectingText(),
                Response = reply.RoundtripTime + " ms",
                Status = activeText(),
                Network = network,
                Seen = DateTime.Now
            };
        }
    }
}
