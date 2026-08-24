using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NetworkScannerTool
{
    internal static class NetShareEnumerator
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHARE_INFO_1
        {
            public string shi1_netname;
            public uint shi1_type;
            public string shi1_remark;
        }

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetShareEnum(
            string servername,
            int level,
            out IntPtr bufptr,
            int prefmaxlen,
            out int entriesread,
            out int totalentries,
            ref int resume_handle);

        [DllImport("Netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        public static List<ShareInfo> GetShares(string ip)
        {
            var result = new List<ShareInfo>();
            IntPtr buffer;
            int read;
            int total;
            int resume = 0;
            int code = NetShareEnum(@"\\" + ip, 1, out buffer, -1, out read, out total, ref resume);

            if (code == 5)
                throw new UnauthorizedAccessException();
            if (code != 0 && code != 234)
                throw new InvalidOperationException("NetShareEnum hata kodu: " + code);

            try
            {
                int size = Marshal.SizeOf(typeof(SHARE_INFO_1));
                IntPtr current = buffer;
                for (int i = 0; i < read; i++)
                {
                    var share = (SHARE_INFO_1)Marshal.PtrToStructure(
                        current,
                        typeof(SHARE_INFO_1));
                    result.Add(new ShareInfo
                    {
                        Name = share.shi1_netname,
                        Type = ShareTypeName(share.shi1_type)
                    });
                    current = IntPtr.Add(current, size);
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    NetApiBufferFree(buffer);
            }

            return result;
        }

        private static string ShareTypeName(uint type)
        {
            uint baseType = type & 0xFF;
            if (baseType == 0) return "Disk";
            if (baseType == 1) return "Print";
            if (baseType == 3) return "IPC";
            return "Diğer";
        }
    }

    internal sealed class ShareInfo
    {
        public string Name;
        public string Type;
    }
}
