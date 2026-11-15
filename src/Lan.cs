using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Riffstorm;

/// <summary>What a room answers to a discovery probe.</summary>
public sealed class RoomInfo
{
    public string Id { get; set; }
    public string Ver { get; set; }
    public string Host { get; set; }
    public string Phase { get; set; }
    public int Players { get; set; }
    public int Max { get; set; }
    // filled by the client
    [System.Text.Json.Serialization.JsonIgnore] public IPAddress Ip;
    [System.Text.Json.Serialization.JsonIgnore] public double Seen;
}

/// <summary>
/// "Salas disponíveis": finds rooms on the local/virtual network (Radmin VPN) without typing a code.
/// Clients send a small UDP probe to every network's broadcast address and to a multicast group; the room
/// (inside the host's game) answers with its name and player count. Nothing goes to the internet.
/// </summary>
public static class Lan
{
    public const int Port = 47824;
    public const string Probe = "RIFFSTORM?";
    static readonly IPAddress Group = IPAddress.Parse("239.255.47.82");

    // ------------------------------------------------------------------ host side
    public static UdpClient OpenResponder(bool loopbackOnly)
    {
        var u = new UdpClient(AddressFamily.InterNetwork);
        u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        u.Client.Bind(new IPEndPoint(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, Port));
        if (!loopbackOnly)
            foreach (var (ip, _) in Interfaces())
                try { u.JoinMulticastGroup(Group, ip); } catch { }
        return u;
    }

    // ------------------------------------------------------------------ client side
    static readonly object _lock = new();
    static readonly Dictionary<string, RoomInfo> _rooms = new();
    static Thread _thread;
    static volatile bool _run;

    static readonly System.Diagnostics.Stopwatch Sw = System.Diagnostics.Stopwatch.StartNew();
    static double Now => Sw.Elapsed.TotalSeconds;

    /// <summary>Rooms heard from in the last few seconds.</summary>
    public static List<RoomInfo> Rooms
    {
        get
        {
            lock (_lock)
            {
                foreach (var k in _rooms.Where(kv => Now - kv.Value.Seen > 6).Select(kv => kv.Key).ToList()) _rooms.Remove(k);
                return _rooms.Values.OrderBy(r => r.Host).ToList();
            }
        }
    }

    public static void Start()
    {
        _run = true;
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "lan-search" };
        _thread.Start();
    }

    public static void Stop() => _run = false;

    static void Loop()
    {
        var probe = Encoding.UTF8.GetBytes(Probe);
        double nextSend = 0;
        var socks = new List<(UdpClient u, IPAddress bcast)>();
        while (true)
        {
            if (!_run) { foreach (var s in socks) s.u.Close(); socks.Clear(); Thread.Sleep(300); continue; }
            if (Now >= nextSend)
            {
                nextSend = Now + 1.5;
                if (socks.Count == 0)
                {
                    foreach (var (ip, bc) in Interfaces().Append((IPAddress.Loopback, IPAddress.Loopback)))
                    {
                        try
                        {
                            var u = new UdpClient(new IPEndPoint(ip, 0)) { EnableBroadcast = true };
                            if (!ip.Equals(IPAddress.Loopback)) u.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ip.GetAddressBytes());
                            socks.Add((u, bc));
                        }
                        catch { }
                    }
                }
                foreach (var (u, bc) in socks)
                {
                    try
                    {
                        if (bc.Equals(IPAddress.Loopback)) { u.Send(probe, probe.Length, new IPEndPoint(IPAddress.Loopback, Port)); continue; }
                        u.Send(probe, probe.Length, new IPEndPoint(bc, Port));
                        u.Send(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, Port));
                        u.Send(probe, probe.Length, new IPEndPoint(Group, Port));
                    }
                    catch { }
                }
            }
            foreach (var (u, _) in socks)
            {
                try
                {
                    while (u.Available > 0)
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        var data = u.Receive(ref ep);
                        var info = JsonSerializer.Deserialize<RoomInfo>(Encoding.UTF8.GetString(data));
                        if (info?.Id == null) continue;
                        info.Ip = ep.Address; info.Seen = Now;
                        lock (_lock)
                        {
                            // the same room may answer on several networks: keep a non-loopback address
                            if (_rooms.TryGetValue(info.Id, out var old) && !IPAddress.IsLoopback(old.Ip) && IPAddress.IsLoopback(info.Ip)) { old.Seen = Now; continue; }
                            _rooms[info.Id] = info;
                        }
                    }
                }
                catch { }
            }
            Thread.Sleep(50);
        }
    }

    /// <summary>Every active IPv4 network of this PC with its broadcast address.</summary>
    static IEnumerable<(IPAddress ip, IPAddress bcast)> Interfaces()
    {
        var list = new List<(IPAddress, IPAddress)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    var a = ua.Address.GetAddressBytes(); var m = ua.IPv4Mask.GetAddressBytes();
                    if (a[0] == 169 && a[1] == 254) continue;
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                    list.Add((ua.Address, new IPAddress(b)));
                }
            }
        }
        catch { }
        return list;
    }
}
