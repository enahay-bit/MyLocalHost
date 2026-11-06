using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace MyLocalHost.Services;

/// <summary>TCP ポートで待ち受けている1件（どのプロセスか）。</summary>
public sealed record PortListener(int Port, IPAddress Address, int ProcessId)
{
    /// <summary>「python.exe（PID 1234）」のような表示用の名前。</summary>
    public string ProcessLabel => $"{PortChecker.GetProcessName(ProcessId)}（PID {ProcessId}）";
}

/// <summary>
/// どのポートがどのプロセスに使われているかを調べる。
/// Windows の GetExtendedTcpTable（netstat -ano と同じ情報）を使う。
/// </summary>
public static class PortChecker
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    /// <summary>
    /// localhost での待ち受けとぶつかる待ち受けをすべて返す
    /// （0.0.0.0 / :: / 127.0.0.1 / ::1。LAN の特定アドレスだけで待ち受けているものは除く）。
    /// </summary>
    public static IReadOnlyList<PortListener> GetLocalhostListeners()
    {
        var list = new List<PortListener>();
        try
        {
            list.AddRange(ReadTable(AF_INET));
            list.AddRange(ReadTable(AF_INET6));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        return list
            .Where(l => l.Address.Equals(IPAddress.Any) || l.Address.Equals(IPAddress.IPv6Any) ||
                        IPAddress.IsLoopback(l.Address))
            .ToList();
    }

    public static string GetProcessName(int pid)
    {
        if (pid == 0) return "不明";
        if (pid == 4) return "System（HTTP.sys / IIS など）";
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName + ".exe";
        }
        catch
        {
            return "不明なプロセス";
        }
    }

    private static IEnumerable<PortListener> ReadTable(int family)
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0);

        // 呼ぶ間に表が大きくなることがあるので、少し余裕を持って確保して数回試す
        for (var attempt = 0; attempt < 3; attempt++)
        {
            size += 1024;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var result = GetExtendedTcpTable(buffer, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0);
                if (result == 122) continue; // ERROR_INSUFFICIENT_BUFFER
                if (result != 0) return [];
                return family == AF_INET ? ParseV4(buffer) : ParseV6(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return [];
    }

    // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, owningPid（各 4 バイト）
    private static List<PortListener> ParseV4(IntPtr table)
    {
        var count = Marshal.ReadInt32(table);
        var list = new List<PortListener>(count);
        var row = table + 4;
        const int rowSize = 24;
        for (var i = 0; i < count; i++, row += rowSize)
        {
            var addr = new IPAddress((uint)Marshal.ReadInt32(row, 4));
            var port = ToHostPort(Marshal.ReadInt32(row, 8));
            var pid = Marshal.ReadInt32(row, 20);
            list.Add(new PortListener(port, addr, pid));
        }
        return list;
    }

    // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScopeId, localPort, remoteAddr[16], remoteScopeId, remotePort, state, owningPid
    private static List<PortListener> ParseV6(IntPtr table)
    {
        var count = Marshal.ReadInt32(table);
        var list = new List<PortListener>(count);
        var row = table + 4;
        const int rowSize = 56;
        var bytes = new byte[16];
        for (var i = 0; i < count; i++, row += rowSize)
        {
            Marshal.Copy(row, bytes, 0, 16);
            var addr = new IPAddress(bytes);
            var port = ToHostPort(Marshal.ReadInt32(row, 20));
            var pid = Marshal.ReadInt32(row, 52);
            list.Add(new PortListener(port, addr, pid));
        }
        return list;
    }

    /// <summary>ポート番号はネットワークバイト順で下位 2 バイトに入っている。</summary>
    private static int ToHostPort(int raw) => ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
}
