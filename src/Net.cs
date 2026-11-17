using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Riffstorm;

/// <summary>
/// One network message (newline-delimited JSON over TCP). Only the fields a message uses are written.
/// </summary>
public sealed class Msg
{
    public string T { get; set; }              // type
    public int Id { get; set; }
    public string Name { get; set; }
    public string Ver { get; set; }
    public string Song { get; set; }           // "game/id|charthash"
    public List<string> Songs { get; set; }
    public int Diff { get; set; }
    public bool Ready { get; set; }
    public double Time { get; set; }
    public double Time2 { get; set; }
    public string Phase { get; set; }
    public string Text { get; set; }
    public List<NetPlayer> Players { get; set; }
    // live state / final result
    public long Score { get; set; }
    public int Mult { get; set; }
    public int Streak { get; set; }
    public float Energy { get; set; }
    public float Meter { get; set; }
    public bool Star { get; set; }
    public bool Failed { get; set; }
    public int From { get; set; }              // first note index in Res
    public string Res { get; set; }            // 'h' hit / 'm' missed for notes From..From+len-1
    public int Stars { get; set; }
    public int Hit { get; set; }
    public int Total { get; set; }
    public int Best { get; set; }
}

public sealed class NetPlayer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Diff { get; set; }
    public bool Ready { get; set; }
    public bool Host { get; set; }
    public bool Loaded { get; set; }
    public bool Done { get; set; }
    public long Score { get; set; }
    public int Stars { get; set; }
    public int Hit { get; set; }
    public int Total { get; set; }
    public int Best { get; set; }
    public bool Failed { get; set; }
    public string Pick { get; set; }           // song this player put in the draw ("game/id|hash")
    public bool InMatch { get; set; }          // played the current song and has not left the results screen yet
}

static class Wire
{
    static readonly JsonSerializerOptions Opt = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
    public static string Ser(Msg m) => JsonSerializer.Serialize(m, Opt);
    public static Msg De(string s) => JsonSerializer.Deserialize<Msg>(s, Opt);
}

/// <summary>
/// The room server, run inside the host's game. Everyone (the host included, over loopback) is a client.
/// It keeps the lobby, the list of songs every player owns, and starts the song at the same instant for all.
/// </summary>
public sealed class NetServer
{
    public const int MaxPlayers = 6;
    public static bool LoopbackOnly;           // tests on one PC (no firewall prompt)

    sealed class Conn
    {
        public TcpClient Tcp; public StreamWriter W; public readonly object Lock = new();
        public NetPlayer P; public HashSet<string> Songs; public bool Alive = true;
    }

    TcpListener _listener;
    readonly List<Conn> _conns = new();
    readonly object _lock = new();
    int _nextId = 1;
    string _phase = "lobby", _song;
    volatile bool _running;

    readonly string _roomId = Guid.NewGuid().ToString("N")[..12];
    UdpClient _udp;

    public void Start()
    {
        _listener = new TcpListener(LoopbackOnly ? IPAddress.Loopback : IPAddress.Any, Net.Port);
        _listener.Start();
        _running = true;
        new Thread(AcceptLoop) { IsBackground = true, Name = "net-accept" }.Start();
        try { _udp = Lan.OpenResponder(LoopbackOnly); new Thread(DiscoveryLoop) { IsBackground = true, Name = "net-discovery" }.Start(); } catch { }
    }

    /// <summary>Answers the "is there a room here?" probes of the "Salas disponíveis" list.</summary>
    void DiscoveryLoop()
    {
        while (_running)
        {
            try
            {
                var ep = new IPEndPoint(IPAddress.Any, 0);
                var data = _udp.Receive(ref ep);
                if (Encoding.UTF8.GetString(data) != Lan.Probe) continue;
                RoomInfo info;
                lock (_lock)
                    info = new RoomInfo
                    {
                        Id = _roomId, Ver = Net.Version, Phase = _phase, Players = Joined.Count(), Max = MaxPlayers,
                        Host = Joined.FirstOrDefault(x => x.P.Host)?.P.Name ?? "?",
                    };
                var reply = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info));
                _udp.Send(reply, reply.Length, ep);
            }
            catch { if (!_running) break; }
        }
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        try { _udp?.Close(); } catch { }
        lock (_lock) foreach (var c in _conns) try { c.Tcp.Close(); } catch { }
    }

    void AcceptLoop()
    {
        while (_running)
        {
            TcpClient tcp;
            try { tcp = _listener.AcceptTcpClient(); } catch { break; }
            tcp.NoDelay = true;
            var c = new Conn { Tcp = tcp, W = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" } };
            new Thread(() => ReadLoop(c)) { IsBackground = true, Name = "net-conn" }.Start();
        }
    }

    void ReadLoop(Conn c)
    {
        try
        {
            using var r = new StreamReader(c.Tcp.GetStream(), Encoding.UTF8);
            string line;
            while (_running && (line = r.ReadLine()) != null)
            {
                Msg m;
                try { m = Wire.De(line); } catch { continue; }
                if (m != null) lock (_lock) Handle(c, m);
            }
        }
        catch { }
        lock (_lock) Remove(c);
    }

    static void Send(Conn c, Msg m)
    {
        if (!c.Alive) return;
        try { lock (c.Lock) c.W.WriteLine(Wire.Ser(m)); } catch { c.Alive = false; }
    }

    IEnumerable<Conn> Joined => _conns.Where(x => x.P != null);

    void Broadcast(Msg m, Conn except = null) { foreach (var c in Joined.ToList()) if (c != except) Send(c, m); }

    List<string> Common()
    {
        HashSet<string> set = null;
        foreach (var c in Joined) { if (set == null) set = new HashSet<string>(c.Songs); else set.IntersectWith(c.Songs); }
        return set?.OrderBy(s => s).ToList() ?? new List<string>();
    }

    void BroadcastLobby()
    {
        var common = Common();
        if (_song != null && !common.Contains(_song)) _song = null;   // someone without that song joined
        foreach (var x in Joined) if (x.P.Pick != null && !common.Contains(x.P.Pick)) x.P.Pick = null;
        Broadcast(new Msg { T = "lobby", Phase = _phase, Song = _song, Songs = common, Players = Joined.Select(x => x.P).ToList() });
    }

    void Handle(Conn c, Msg m)
    {
        switch (m.T)
        {
            case "hello":
                if (m.Ver != Net.Version) { Send(c, new Msg { T = "error", Text = "Versão do Riffstorm diferente da do anfitrião." }); c.Tcp.Close(); return; }
                if (_phase != "lobby") { Send(c, new Msg { T = "error", Text = Net.BusyRoomText }); c.Tcp.Close(); return; }
                if (Joined.Count() >= MaxPlayers) { Send(c, new Msg { T = "error", Text = "A sala está cheia (6 jogadores)." }); c.Tcp.Close(); return; }
                string name = string.IsNullOrWhiteSpace(m.Name) ? "Jogador" : m.Name.Trim();
                if (name.Length > 16) name = name[..16];
                string baseName = name; int k = 2;
                while (Joined.Any(x => x.P.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"{baseName} {k++}";
                c.P = new NetPlayer { Id = _nextId++, Name = name, Diff = Math.Clamp(m.Diff, 0, 3), Host = !Joined.Any() };
                c.Songs = new HashSet<string>(m.Songs ?? new());
                _conns.Add(c);
                Send(c, new Msg { T = "welcome", Id = c.P.Id, Name = name });
                BroadcastLobby();
                break;
            case "ping":
                Send(c, new Msg { T = "pong", Time = m.Time, Time2 = Net.Clock });
                break;
            case "diff":
                if (c.P == null) return;
                c.P.Diff = Math.Clamp(m.Diff, 0, 3); BroadcastLobby();
                break;
            case "ready":
                if (c.P == null) return;
                c.P.Ready = m.Ready; BroadcastLobby();
                break;
            case "pick":                       // every player may put one song in the draw
                if (c.P == null || _phase is not ("lobby" or "results")) return;
                if (Common().Contains(m.Song)) { c.P.Pick = m.Song; BroadcastLobby(); }
                break;
            case "unpick":
                if (c.P == null || _phase is not ("lobby" or "results")) return;
                c.P.Pick = null; BroadcastLobby();
                break;
            case "start":
            {
                if (c.P?.Host != true || _phase != "lobby") return;
                if (Joined.Any(x => !x.P.Host && !x.P.Ready)) return;
                var picks = Joined.Select(x => x.P.Pick).Where(s => s != null).ToList();
                if (picks.Count == 0) return;
                _song = picks[Random.Shared.Next(picks.Count)];          // the draw
                _phase = "loading";
                foreach (var x in Joined) { var p = x.P; p.Loaded = p.Done = p.Failed = false; p.InMatch = true; p.Score = 0; p.Stars = p.Hit = p.Total = p.Best = 0; }
                Broadcast(new Msg { T = "load", Song = _song, Songs = picks });
                BroadcastLobby();
                break;
            }
            case "react":
                if (c.P == null) return;
                m.Id = c.P.Id; m.Diff = Math.Clamp(m.Diff, 0, 3);
                Broadcast(m, c);
                break;
            case "loaded":
                if (c.P == null || _phase != "loading") return;
                c.P.Loaded = true;
                CheckAllLoaded();
                break;
            case "st":
                if (c.P == null || _phase != "play") return;
                m.Id = c.P.Id; c.P.Score = m.Score;
                Broadcast(m, c);
                break;
            case "back":                       // left the results screen, back at the song selection
                if (c.P == null) return;
                c.P.InMatch = false;
                CheckBackToLobby();
                BroadcastLobby();
                break;
            case "done":
                if (c.P == null || (_phase != "play" && _phase != "loading")) return;
                var pl = c.P;
                pl.Done = true; pl.Loaded = true; pl.Score = m.Score; pl.Stars = m.Stars; pl.Hit = m.Hit; pl.Total = m.Total; pl.Best = m.Best; pl.Failed = m.Failed;
                CheckAllLoaded();
                CheckAllDone();
                BroadcastLobby();
                break;
        }
    }

    void CheckAllLoaded()
    {
        if (_phase != "loading" || Joined.Any(x => !x.P.Loaded)) return;
        _phase = "play";
        Broadcast(new Msg { T = "go", Time = Net.Clock + 1.5 });
        BroadcastLobby();
    }

    void CheckAllDone()
    {
        if (_phase != "play" || Joined.Any(x => !x.P.Done)) return;
        _phase = "results";                    // nobody can join until everyone is back at the song selection
        foreach (var x in Joined) { x.P.Ready = false; if (x.P.Pick == _song) x.P.Pick = null; }   // the song just played leaves the draw
    }

    void Remove(Conn c)
    {
        if (!_conns.Remove(c)) { try { c.Tcp.Close(); } catch { } return; }
        c.Alive = false;
        try { c.Tcp.Close(); } catch { }
        if (c.P?.Host == true) { var next = Joined.FirstOrDefault(); if (next != null) next.P.Host = false; }  // host leaving closes the room anyway
        CheckAllLoaded();
        CheckAllDone();
        CheckBackToLobby();
        BroadcastLobby();
    }

    void CheckBackToLobby()
    {
        if (_phase == "results" && !Joined.Any(x => x.P.InMatch)) _phase = "lobby";
    }
}

/// <summary>
/// Client side of the online duel (also used by the host over loopback) + room codes.
/// Network traffic is read on a background thread and handed to the game in <see cref="Poll"/>.
/// </summary>
public static class Net
{
    public const int Port = 47823;
    public const string Version = "riffstorm-net-3";
    public const string BusyRoomText = "Uma partida está em andamento nessa sala. Você poderá entrar quando todos voltarem para a escolha de músicas.";

    static readonly Stopwatch Sw = Stopwatch.StartNew();
    public static double Clock => Sw.Elapsed.TotalSeconds;

    public static NetServer Server;
    static TcpClient _tcp;
    static StreamWriter _w;
    static readonly object _wLock = new();
    static readonly ConcurrentQueue<Msg> _inbox = new();
    static volatile bool _closed;

    public static bool Active => _tcp != null;
    public static bool IsHost => Server != null;
    public static int MyId;
    public static string RoomCode, RoomIp, RoomVia;
    public static string Error;                 // why we left the room (shown in the online menu)
    public static string Phase = "lobby";
    public static string Song;                  // chosen song key "game/id|hash"
    public static List<NetPlayer> Players = new();
    public static HashSet<string> Common = new();
    public static double Offset;                // server clock - local clock
    static double _bestRtt = 99, _nextPing;
    static int _pings;

    public static NetPlayer Me => Players.FirstOrDefault(p => p.Id == MyId);

    // ------------------------------------------------------------------ songs
    public static string SongKey(Song s)
    {
        if (string.IsNullOrEmpty(s.ChartHash))
            try { s.ChartHash = Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(s.ChartPath)))[..16].ToLowerInvariant(); } catch { s.ChartHash = "?"; }
        return s.Key + "|" + s.ChartHash;
    }

    public static Song FindSong(string key) => key == null ? null : SongDb.Songs.FirstOrDefault(s => SongKey(s) == key);

    // ------------------------------------------------------------------ room codes (IPv4 in Crockford base32)
    const string B32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string CodeFor(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        uint v = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        var sb = new StringBuilder();
        for (int i = 6; i >= 0; i--) sb.Append(B32[(int)((v >> (i * 5)) & 31)]);
        return sb.ToString(0, 3) + "-" + sb.ToString(3, 4);
    }

    /// <summary>Room code (or a plain IP address) → address to connect to. Null if it is not valid.</summary>
    public static IPAddress ParseCode(string code)
    {
        code = (code ?? "").Trim();
        if (code.Contains('.')) return IPAddress.TryParse(code.Split(':')[0], out var ip) ? ip : null;
        var s = code.ToUpperInvariant().Replace("-", "").Replace(" ", "").Replace('O', '0').Replace('I', '1').Replace('L', '1');
        if (s.Length != 7) return null;
        ulong v = 0;
        foreach (char ch in s) { int d = B32.IndexOf(ch); if (d < 0) return null; v = v << 5 | (uint)d; }
        if (v > uint.MaxValue) return null;
        return new IPAddress(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
    }

    /// <summary>The address friends should use: the Radmin VPN one if present, else the local network one.</summary>
    public static (IPAddress ip, string via) BestAddress()
    {
        (IPAddress, string, int) best = (IPAddress.Loopback, "este computador", 99);
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = ua.Address.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue;
                    int rank; string via;
                    if (d.Contains("radmin") || b[0] == 26) { rank = 0; via = "Radmin VPN"; }
                    else if (d.Contains("hamachi") || b[0] == 25) { rank = 1; via = "Hamachi"; }
                    else if (d.Contains("tailscale") || d.Contains("zerotier") || (b[0] == 100 && b[1] >= 64 && b[1] < 128)) { rank = 2; via = "rede virtual"; }
                    else { rank = 3; via = "rede local"; }
                    if (rank < best.Item3) best = (ua.Address, via, rank);
                }
            }
        }
        catch { }
        return (best.Item1, best.Item2);
    }

    // ------------------------------------------------------------------ connect / leave
    static List<string> MySongs() => SongDb.Songs.Select(SongKey).Distinct().ToList();

    /// <summary>Opens a room on this computer and joins it.</summary>
    public static string Host(string name, int diff)
    {
        Leave();
        try { Server = new NetServer(); Server.Start(); }
        catch (Exception e) { Server = null; return "Não foi possível abrir a sala (porta " + Port + " ocupada?). " + e.Message; }
        var (ip, via) = BestAddress();
        RoomIp = ip.ToString(); RoomVia = via; RoomCode = CodeFor(ip);
        var err = Connect(IPAddress.Loopback, name, diff);
        if (err != null) { Server.Stop(); Server = null; }
        return err;
    }

    public static string Join(IPAddress ip, string name, int diff, string code)
    {
        Leave();
        RoomIp = ip.ToString(); RoomVia = null; RoomCode = code.Contains('.') ? CodeFor(ip) : code.ToUpperInvariant();
        return Connect(ip, name, diff);
    }

    static string Connect(IPAddress ip, string name, int diff)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            var task = tcp.ConnectAsync(ip, Port);
            if (!task.Wait(6000) || !tcp.Connected) { tcp.Close(); return "Não encontrei a sala. Confira o código e se o anfitrião está com a sala aberta e o Radmin VPN ligado."; }
        }
        catch { tcp.Close(); return "Não encontrei a sala. Confira o código e se o anfitrião está com a sala aberta e o Radmin VPN ligado."; }
        _tcp = tcp; _closed = false; Error = null;
        _w = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        Players = new(); Common = new(); Song = null; Phase = "lobby"; MyId = 0;
        _bestRtt = 99; _pings = 0; _nextPing = 0; Offset = 0;
        while (_inbox.TryDequeue(out _)) { }
        new Thread(() => ReadLoop(tcp)) { IsBackground = true, Name = "net-client" }.Start();
        Send(new Msg { T = "hello", Ver = Version, Name = name, Diff = diff, Songs = MySongs() });
        return null;
    }

    static void ReadLoop(TcpClient tcp)
    {
        try
        {
            using var r = new StreamReader(tcp.GetStream(), Encoding.UTF8);
            string line;
            while ((line = r.ReadLine()) != null)
            {
                try { var m = Wire.De(line); if (m != null) _inbox.Enqueue(m); } catch { }
            }
        }
        catch { }
        if (tcp == _tcp) _closed = true;
    }

    public static void Send(Msg m)
    {
        if (_w == null) return;
        try { lock (_wLock) _w.WriteLine(Wire.Ser(m)); } catch { _closed = true; }
    }

    public static void Leave()
    {
        var tcp = _tcp; _tcp = null; _w = null;
        try { tcp?.Close(); } catch { }
        Server?.Stop(); Server = null;
        Players = new(); Common = new(); Song = null; MyId = 0;
    }

    /// <summary>Handles lobby/clock messages; returns the ones the game reacts to (welcome, load, go, st, error).</summary>
    public static List<Msg> Poll()
    {
        var outp = new List<Msg>();
        if (_tcp == null) return outp;
        double now = Clock;
        if (now >= _nextPing)
        {
            Send(new Msg { T = "ping", Time = now });
            _nextPing = now + (_pings++ < 8 ? 0.25 : 2.0);
        }
        while (_inbox.TryDequeue(out var m))
        {
            switch (m.T)
            {
                case "welcome": MyId = m.Id; break;
                case "lobby":
                    Phase = m.Phase ?? "lobby"; Song = m.Song;
                    Players = m.Players ?? new(); Common = new HashSet<string>(m.Songs ?? new());
                    break;
                case "pong":
                    double rtt = now - m.Time;
                    if (rtt <= _bestRtt * 1.2 || rtt < 0.02) { _bestRtt = Math.Min(_bestRtt, rtt); Offset = m.Time2 - (m.Time + rtt / 2); }
                    continue;
                case "error": Error = m.Text; break;
            }
            outp.Add(m);
        }
        if (_closed && _tcp != null)
        {
            Error ??= IsHost ? "A sala foi fechada." : "A conexão com a sala caiu (o anfitrião fechou a sala ou a internet/Radmin caiu).";
            Leave();
            outp.Add(new Msg { T = "closed" });
        }
        return outp;
    }

    /// <summary>Local clock time for a server time.</summary>
    public static double ToLocal(double serverTime) => serverTime - Offset;
}
