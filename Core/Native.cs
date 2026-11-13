using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace FallenSec.Core;

public sealed record SocketRow(bool Udp, int Pid, IPAddress LocalAddr, int LocalPort,
    IPAddress RemoteAddr, int RemotePort, int State)
{
    // valori brute pentru SetTcpEntry (doar IPv4 TCP)
    public uint RawLA, RawLP, RawRA, RawRP;
}

public static class Native
{
    const int AF_INET = 2, AF_INET6 = 23;
    const int TCP_TABLE_OWNER_PID_ALL = 5, UDP_TABLE_OWNER_PID = 1;
    public const int TCP_STATE_LISTEN = 2, TCP_STATE_ESTAB = 5, TCP_STATE_DELETE_TCB = 12;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);
    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    struct MIB_TCPROW { public uint dwState, dwLocalAddr, dwLocalPort, dwRemoteAddr, dwRemotePort; }
    [DllImport("iphlpapi.dll")]
    static extern uint SetTcpEntry(ref MIB_TCPROW row);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")]
    static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

    static int Port(uint raw) => (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));

    static IntPtr GetTable(bool udp, int af, int cls)
    {
        int size = 0;
        for (int i = 0; i < 5; i++)
        {
            IntPtr buf = size > 0 ? Marshal.AllocHGlobal(size) : IntPtr.Zero;
            uint r = udp ? GetExtendedUdpTable(buf, ref size, false, af, cls, 0)
                         : GetExtendedTcpTable(buf, ref size, false, af, cls, 0);
            if (r == 0) return buf;
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            if (r != 122) return IntPtr.Zero; // ERROR_INSUFFICIENT_BUFFER
            size += 4096;
        }
        return IntPtr.Zero;
    }

    public static List<SocketRow> GetSockets()
    {
        var list = new List<SocketRow>(512);
        // TCP v4
        var t = GetTable(false, AF_INET, TCP_TABLE_OWNER_PID_ALL);
        if (t != IntPtr.Zero)
        {
            try
            {
                int n = Marshal.ReadInt32(t); IntPtr p = t + 4;
                for (int i = 0; i < n; i++, p += 24)
                {
                    uint st = (uint)Marshal.ReadInt32(p), la = (uint)Marshal.ReadInt32(p, 4), lp = (uint)Marshal.ReadInt32(p, 8),
                         ra = (uint)Marshal.ReadInt32(p, 12), rp = (uint)Marshal.ReadInt32(p, 16);
                    int pid = Marshal.ReadInt32(p, 20);
                    list.Add(new SocketRow(false, pid, new IPAddress(la), Port(lp), new IPAddress(ra), Port(rp), (int)st)
                    { RawLA = la, RawLP = lp, RawRA = ra, RawRP = rp });
                }
            }
            finally { Marshal.FreeHGlobal(t); }
        }
        // TCP v6
        t = GetTable(false, AF_INET6, TCP_TABLE_OWNER_PID_ALL);
        if (t != IntPtr.Zero)
        {
            try
            {
                int n = Marshal.ReadInt32(t); IntPtr p = t + 4;
                var la = new byte[16]; var ra = new byte[16];
                for (int i = 0; i < n; i++, p += 56)
                {
                    Marshal.Copy(p, la, 0, 16);
                    uint lp = (uint)Marshal.ReadInt32(p, 20);
                    Marshal.Copy(p + 24, ra, 0, 16);
                    uint rp = (uint)Marshal.ReadInt32(p, 44);
                    int st = Marshal.ReadInt32(p, 48), pid = Marshal.ReadInt32(p, 52);
                    list.Add(new SocketRow(false, pid, Norm(new IPAddress(la)), Port(lp), Norm(new IPAddress(ra)), Port(rp), st));
                }
            }
            finally { Marshal.FreeHGlobal(t); }
        }
        // UDP v4 (doar local)
        t = GetTable(true, AF_INET, UDP_TABLE_OWNER_PID);
        if (t != IntPtr.Zero)
        {
            try
            {
                int n = Marshal.ReadInt32(t); IntPtr p = t + 4;
                for (int i = 0; i < n; i++, p += 12)
                {
                    uint la = (uint)Marshal.ReadInt32(p), lp = (uint)Marshal.ReadInt32(p, 4);
                    int pid = Marshal.ReadInt32(p, 8);
                    list.Add(new SocketRow(true, pid, new IPAddress(la), Port(lp), IPAddress.Any, 0, 0));
                }
            }
            finally { Marshal.FreeHGlobal(t); }
        }
        t = GetTable(true, AF_INET6, UDP_TABLE_OWNER_PID);
        if (t != IntPtr.Zero)
        {
            try
            {
                int n = Marshal.ReadInt32(t); IntPtr p = t + 4;
                var la = new byte[16];
                for (int i = 0; i < n; i++, p += 28)
                {
                    Marshal.Copy(p, la, 0, 16);
                    uint lp = (uint)Marshal.ReadInt32(p, 20);
                    int pid = Marshal.ReadInt32(p, 24);
                    list.Add(new SocketRow(true, pid, Norm(new IPAddress(la)), Port(lp), IPAddress.IPv6Any, 0, 0));
                }
            }
            finally { Marshal.FreeHGlobal(t); }
        }
        return list;
    }

    public static IPAddress Norm(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a;

    /// <summary>Închide forțat o conexiune TCP IPv4 (necesită admin).</summary>
    public static bool KillTcp(SocketRow r)
    {
        if (r.Udp || r.LocalAddr.AddressFamily != AddressFamily.InterNetwork) return false;
        var row = new MIB_TCPROW
        {
            dwState = TCP_STATE_DELETE_TCB, dwLocalAddr = r.RawLA, dwLocalPort = r.RawLP,
            dwRemoteAddr = r.RawRA, dwRemotePort = r.RawRP
        };
        return SetTcpEntry(ref row) == 0;
    }

    public static string? GetProcessPath(int pid, out long startTime)
    {
        startTime = 0;
        IntPtr h = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            GetProcessTimes(h, out startTime, out _, out _, out _);
            var sb = new StringBuilder(1024); int sz = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref sz) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    public static string TcpStateName(int s) => s switch
    {
        1 => "CLOSED", 2 => "LISTEN", 3 => "SYN_SENT", 4 => "SYN_RCVD", 5 => "ESTABLISHED",
        6 => "FIN_WAIT1", 7 => "FIN_WAIT2", 8 => "CLOSE_WAIT", 9 => "CLOSING", 10 => "LAST_ACK",
        11 => "TIME_WAIT", 12 => "DELETE_TCB", _ => "?"
    };

    public static bool IsLocalOrPrivate(IPAddress a)
    {
        a = Norm(a);
        if (IPAddress.IsLoopback(a)) return true;
        if (a.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = a.GetAddressBytes();
            return b[0] == 10 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224;
        }
        if (a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast) return true;
        var bb = a.GetAddressBytes();
        if ((bb[0] & 0xFE) == 0xFC) return true; // ULA fc00::/7
        return a.Equals(IPAddress.IPv6Any) || a.Equals(IPAddress.IPv6None);
    }
}
