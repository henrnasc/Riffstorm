using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Riffstorm;

/// <summary>Reads files from an ISO9660 image by path (used for the PS3 disc).</summary>
public sealed class IsoFs : IDisposable
{
    readonly FileStream _fs;
    readonly (long lba, long size) _root;
    readonly bool _joliet;
    public IsoFs(string path)
    {
        _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        var vd = new byte[2048];
        _fs.Position = 16 * 2048; _fs.ReadExactly(vd);
        if (vd[0] != 1 || Encoding.ASCII.GetString(vd, 1, 5) != "CD001") throw new InvalidDataException("ISO inválida.");
        _root = (BitConverter.ToUInt32(vd, 158), BitConverter.ToUInt32(vd, 166));
        // prefer the Joliet tree: the primary one shortens names (QB.PAK.PS3 -> QB_PAK.PS3, 31 chars max)
        for (int s = 17; s < 32; s++)
        {
            _fs.Position = s * 2048L; _fs.ReadExactly(vd);
            if (Encoding.ASCII.GetString(vd, 1, 5) != "CD001" || vd[0] == 255) break;
            if (vd[0] == 2 && vd[88] == 0x25 && vd[89] == 0x2F)
            {
                _root = (BitConverter.ToUInt32(vd, 158), BitConverter.ToUInt32(vd, 166));
                _joliet = true;
                break;
            }
        }
    }

    (long lba, long size) Find(long lba, long size, string name)
    {
        var buf = new byte[size];
        _fs.Position = lba * 2048; _fs.ReadExactly(buf);
        int p = 0;
        while (p < buf.Length)
        {
            int len = buf[p];
            if (len == 0) { p = (p / 2048 + 1) * 2048; continue; }
            string n = _joliet ? Encoding.BigEndianUnicode.GetString(buf, p + 33, buf[p + 32]) : Encoding.ASCII.GetString(buf, p + 33, buf[p + 32]);
            int semi = n.IndexOf(';'); if (semi >= 0) n = n[..semi];
            if (n.Equals(name, StringComparison.OrdinalIgnoreCase))
                return (BitConverter.ToUInt32(buf, p + 2), BitConverter.ToUInt32(buf, p + 10));
            p += len;
        }
        return (-1, 0);
    }

    public (long lba, long size) Lookup(string path)
    {
        var cur = _root;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            cur = Find(cur.lba, cur.size, part);
            if (cur.lba < 0) return cur;
        }
        return cur;
    }

    public bool Exists(string path) => Lookup(path).lba >= 0;

    public byte[] Read(string path)
    {
        var e = Lookup(path);
        if (e.lba < 0) throw new FileNotFoundException(path);
        var buf = new byte[e.size];
        _fs.Position = e.lba * 2048; _fs.ReadExactly(buf);
        return buf;
    }

    public void Dispose() => _fs.Dispose();
}

/// <summary>Neversoft QB script data (big-endian, PS3) and PAK/PAB archives.</summary>
public static class Qb
{
    public readonly record struct Key(uint V);

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>QBKey: CRC-32 of the lowercase name without the final inversion.</summary>
    public static uint K(string s)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in Encoding.ASCII.GetBytes(s.ToLowerInvariant())) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c;
    }

    public sealed record PakEntry(uint Type, int Offset, int Size, uint FullName, uint Name);

    /// <summary>Entries of a .pak (data may continue in the sibling .pab: pass both concatenated).</summary>
    public static List<PakEntry> ReadPak(byte[] d)
    {
        var list = new List<PakEntry>();
        for (int p = 0; p + 32 <= d.Length; p += 32)
        {
            uint t = U(d, p), off = U(d, p + 4), size = U(d, p + 8);
            if (t == 0x2cb3ef3b || t == 0xb524565f || (t == 0 && off == 0)) break;
            list.Add(new PakEntry(t, p + (int)off, (int)size, U(d, p + 16), U(d, p + 20)));
        }
        return list;
    }

    static uint U(byte[] d, int p) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p));

    /// <summary>Value of the top-level section named <paramref name="key"/> inside one QB file, or null.</summary>
    public static object Find(byte[] d, int start, int len, uint key)
    {
        var span = d.AsSpan(start, len);
        Span<byte> kb = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(kb, key);
        int from = 4;
        while (from < len)
        {
            int i = span[from..].IndexOf(kb);
            if (i < 0) return null;
            i += from;
            if (i % 4 == 0)
            {
                uint t = U(d, start + i - 4);
                if ((t & 0xFFFF00FF) == 0x00200000)
                    return Value(d, start, (int)((t >> 8) & 0xFF), U(d, start + i + 8));
            }
            from = i + 1;
        }
        return null;
    }

    static object Value(byte[] d, int b, int kind, uint v)
    {
        switch (kind)
        {
            case 1: return (int)v;
            case 2: return BitConverter.Int32BitsToSingle((int)v);
            case 3: { int s = b + (int)v, e = s; while (d[e] != 0) e++; return Encoding.Latin1.GetString(d, s, e - s); }
            case 4: { int s = b + (int)v, e = s; while (d[e] != 0 || d[e + 1] != 0) e += 2; return Encoding.BigEndianUnicode.GetString(d, s, e - s); }
            case 0xA: return Struct(d, b, (int)v);
            case 0xC: return Array(d, b, (int)v);
            case 0xD: case 0x1A: case 0x1B: case 0x1C: return new Key(v);
            default: return null;
        }
    }

    static List<object> Array(byte[] d, int b, int p)
    {
        uint t = U(d, b + p); int n = (int)U(d, b + p + 4);
        int kind = (int)((t >> 8) & 0xFF);
        var res = new List<object>(n);
        if (n == 0) return res;
        bool inlineVals = kind is 1 or 2 or 0xD;
        if (n == 1) { res.Add(Value(d, b, kind, U(d, b + p + 8))); return res; }
        int basep = b + (int)U(d, b + p + 8);
        for (int i = 0; i < n; i++)
        {
            uint x = U(d, basep + 4 * i);
            res.Add(inlineVals ? Value(d, b, kind, x) : Value(d, b, kind, x));
        }
        return res;
    }

    static Dictionary<uint, object> Struct(byte[] d, int b, int p)
    {
        var res = new Dictionary<uint, object>();
        int q = (int)U(d, b + p + 4), guard = 0;
        while (q != 0 && guard++ < 4000)
        {
            uint typ = U(d, b + q), key = U(d, b + q + 4), v = U(d, b + q + 8), nxt = U(d, b + q + 12);
            res[key] = Value(d, b, (int)((typ >> 16) & 0x7F), v);
            q = (int)nxt;
        }
        return res;
    }

    public static List<int> Ints(object o) => (o as List<object>)?.Select(x => x is int i ? i : 0).ToList() ?? new List<int>();
}

/// <summary>GH3 (PS3) importer: song list + career from QB.PAK/PAB, audio from encrypted FSB3 (MP3 stems), charts from song PAKs.</summary>
public static class NeversoftImport
{
    static readonly byte[] FsbKey = Encoding.ASCII.GetBytes("5atu6w4zaw");
    static readonly byte[] Rev = Enumerable.Range(0, 256).Select(i => { int r = 0; for (int b = 0; b < 8; b++) if ((i & (1 << b)) != 0) r |= 1 << (7 - b); return (byte)r; }).ToArray();

    public static void Import(string isoPath, string libraryRoot, Importer.KnownGame kg, Importer.Progress pr)
    {
        using var iso = new IsoFs(isoPath);
        const string Data = "PS3_GAME/USRDIR/DATA/";
        var qbAll = iso.Read(Data + "PAK/QB.PAK.PS3").Concat(iso.Read(Data + "PAK/QB.PAB.PS3")).ToArray();
        var ents = Qb.ReadPak(qbAll);
        object FindAny(string name)
        {
            uint k = Qb.K(name);
            foreach (var e in ents)
            {
                if (e.Offset + e.Size > qbAll.Length) continue;
                var v = Qb.Find(qbAll, e.Offset, e.Size, k);
                if (v != null) return v;
            }
            return null;
        }
        var props = FindAny("permanent_songlist_props") as Dictionary<uint, object> ?? throw new InvalidDataException("Lista de músicas do GH3 não encontrada.");
        var career = FindAny("gh3_career_songs") as Dictionary<uint, object>;
        var songlist = FindAny("gh3_songlist") as List<object>;

        // song metadata by QB key of its name
        var meta = new Dictionary<uint, (string id, string title, string artist)>();
        foreach (var v in props.Values)
            if (v is Dictionary<uint, object> st && st.TryGetValue(Qb.K("name"), out var nm) && nm is string id)
                meta[Qb.K(id)] = (id, st.GetValueOrDefault(Qb.K("title")) as string ?? id, st.GetValueOrDefault(Qb.K("artist")) as string ?? "");

        var game = new GameEntry { Id = kg.Id, Name = kg.Name, Serial = kg.Serial, SourceSha1 = kg.Sha1, Imported = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        var inCareer = new HashSet<uint>();
        if (career != null)
            for (int t = 1; t <= 12; t++)
            {
                if (career.GetValueOrDefault(Qb.K($"tier{t}")) is not Dictionary<uint, object> tier) continue;
                var te = new TierEntry { Name = tier.GetValueOrDefault(Qb.K("title")) as string ?? $"Nível {t}" };
                if (te.Name.Length > 3 && char.IsDigit(te.Name[0]) && te.Name[1] == '.') te.Name = te.Name[2..].Trim();
                if (tier.GetValueOrDefault(Qb.K("songs")) is List<object> songs)
                    foreach (var s in songs)
                        if (s is Qb.Key k && meta.TryGetValue(k.V, out var m)) { te.Songs.Add(m.id.ToLowerInvariant()); inCareer.Add(k.V); }
                game.Tiers.Add(te);
            }
        // the song list also carries internal sync tests and tutorial lessons – not songs
        var all = (songlist?.OfType<Qb.Key>().Select(k => k.V) ?? meta.Keys).Where(meta.ContainsKey).Distinct()
            .Where(k => !meta[k].id.StartsWith("tutorial", StringComparison.OrdinalIgnoreCase) && !meta[k].id.StartsWith("synctest", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var gdir = Path.Combine(libraryRoot, kg.Id);
        Directory.CreateDirectory(gdir);
        pr.Total = all.Count;
        var imported = new HashSet<string>();
        foreach (var key in all)
        {
            var (id, title, artist) = meta[key];
            pr.Current = title;
            string up = id.ToUpperInvariant();
            try
            {
                if (!iso.Exists(Data + $"MUSIC/{up}.FSB.PS3") || !iso.Exists(Data + $"SONGS/{up}_SONG.PAK.PS3")) { pr.Done++; continue; }
                var sid = id.ToLowerInvariant();
                var sdir = Path.Combine(gdir, sid);
                Directory.CreateDirectory(sdir);
                var stems = SplitFsb(iso.Read(Data + $"MUSIC/{up}.FSB.PS3"), sdir);
                var pak = iso.Read(Data + $"SONGS/{up}_SONG.PAK.PS3");
                var (mid, lastMs) = ChartToMidi(pak, sid);
                Chart.Parse(mid);
                File.WriteAllBytes(Path.Combine(sdir, "notes.mid"), mid);
                var order = new[] { "song", "rhythm", "guitar" }.Where(stems.Contains).ToList();
                var s = new Song
                {
                    Id = sid, Game = kg.Id, Name = title, Artist = artist, Chart = "notes.mid", Audio = null,
                    Stems = order.Select(x => x + ".mp3").ToArray(),
                    Preview = stems.Contains("preview") ? "preview.mp3" : null,
                    Pans = order.SelectMany(_ => new[] { -1f, 1f }).ToArray(),
                    Vols = order.SelectMany(_ => new[] { 0.9f, 0.9f }).ToArray(),
                    Guitar = order.SelectMany(x => new[] { x == "guitar", x == "guitar" }).ToArray(),
                    PreviewStart = 0, PreviewEnd = 30,
                    ChartHash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(mid))[..16].ToLowerInvariant(),
                };
                File.WriteAllText(Path.Combine(sdir, "song.json"), JsonSerializer.Serialize(s, SongDb.Json));
                imported.Add(sid);
                pr.Imported++;
            }
            catch (Exception) { /* skip a broken song, keep importing the rest */ }
            pr.Done++;
        }
        foreach (var t in game.Tiers) t.Songs.RemoveAll(x => !imported.Contains(x));
        game.Tiers.RemoveAll(t => t.Songs.Count == 0);
        var careerIds = game.Tiers.SelectMany(t => t.Songs).ToHashSet();
        game.Bonus = imported.Where(x => !careerIds.Contains(x)).OrderBy(x => x).ToList();
        File.WriteAllText(Path.Combine(gdir, "game.json"), JsonSerializer.Serialize(game, SongDb.Json));
    }

    /// <summary>Decrypts the FSB3 (xor key + bit reverse) and writes each MP3 stream (guitar, rhythm, song, preview).</summary>
    static HashSet<string> SplitFsb(byte[] raw, string dir)
    {
        var d = new byte[raw.Length];
        for (int i = 0; i < raw.Length; i++) d[i] = Rev[raw[i] ^ FsbKey[i % FsbKey.Length]];
        if (Encoding.ASCII.GetString(d, 0, 4) != "FSB3") throw new InvalidDataException("FSB inesperado");
        int n = BitConverter.ToInt32(d, 4), shdr = BitConverter.ToInt32(d, 8);
        int p = 24; long off = 24 + shdr;
        var names = new HashSet<string>();
        for (int i = 0; i < n; i++)
        {
            int sz = BitConverter.ToUInt16(d, p);
            string name = Encoding.ASCII.GetString(d, p + 2, 30).TrimEnd('\0');
            int comp = BitConverter.ToInt32(d, p + 36);
            string kind = name.Contains("guitar") ? "guitar" : name.Contains("rhythm") ? "rhythm" : name.Contains("preview") ? "preview" : name.Contains("song") ? "song" : null;
            if (kind != null && !names.Contains(kind))
            {
                File.WriteAllBytes(Path.Combine(dir, kind + ".mp3"), d.AsSpan((int)off, comp).ToArray());
                names.Add(kind);
            }
            p += sz; off += comp;
        }
        return names;
    }

    /// <summary>
    /// Converts the QB chart (ms timings) into a standard MIDI file: tempo map from the beat lines, notes on 60/72/84/96 + fret,
    /// star phrases on base+7, and explicit hammer-on (base+5) / strum (base+6) markers so the game uses GH3's own HOPO rules.
    /// </summary>
    static (byte[] mid, int lastMs) ChartToMidi(byte[] pak, string id)
    {
        var ents = Qb.ReadPak(pak);
        var qbe = ents.First(e => e.Type == Qb.K(".qb"));
        object F(string n) => Qb.Find(pak, qbe.Offset, qbe.Size, Qb.K(n));
        var beats = Qb.Ints(F(id + "_fretbars"));
        if (beats.Count < 2) throw new InvalidDataException("sem batidas");
        const int Res = 480;
        double Tick(double ms)
        {
            int i = beats.BinarySearch((int)ms); if (i < 0) i = ~i - 1;
            i = Math.Clamp(i, 0, beats.Count - 2);
            return Res * (i + (ms - beats[i]) / Math.Max(1, beats[i + 1] - beats[i]));
        }
        var tempo = new List<(long tick, byte[] ev)>();
        int lastUs = -1;
        for (int i = 0; i + 1 < beats.Count; i++)
        {
            int us = (beats[i + 1] - beats[i]) * 1000;
            if (us == lastUs) continue;
            lastUs = us;
            tempo.Add(((long)i * Res, new byte[] { 0xFF, 0x51, 3, (byte)(us >> 16), (byte)(us >> 8), (byte)us }));
        }
        if (F(id + "_timesig") is List<object> ts)
            foreach (var x in ts.OfType<List<object>>())
            {
                var v = Qb.Ints(x);
                if (v.Count < 3) continue;
                int den = 0; for (int dd = v[2]; dd > 1; dd >>= 1) den++;
                tempo.Add(((long)Math.Round(Tick(v[0])), new byte[] { 0xFF, 0x58, 4, (byte)v[1], (byte)den, 24, 8 }));
            }

        var notes = new List<(long tick, byte[] ev)>();
        int lastMs = 0;
        string[] diffs = { "easy", "medium", "hard", "expert" };
        for (int di = 0; di < 4; di++)
        {
            int baseN = 60 + di * 12;
            var raw = Qb.Ints(F($"{id}_song_{diffs[di]}"));
            long prevTick = -100000; int prevMask = -1;
            for (int i = 0; i + 2 < raw.Count; i += 3)
            {
                int ms = raw[i], len = raw[i + 1], bits = raw[i + 2];
                int mask = bits & 31; bool flip = (bits & 32) != 0;
                if (mask == 0) continue;
                long on = (long)Math.Round(Tick(ms));
                long lenT = (long)Math.Round(Tick(ms + len)) - on;
                long off = on + (lenT > Res / 2 ? lenT : 60);
                bool chord = System.Numerics.BitOperations.PopCount((uint)mask) > 1;
                bool natural = !chord && on - prevTick <= Res / 2.95 && mask != prevMask;
                bool hopo = natural ^ flip;
                for (int f = 0; f < 5; f++)
                    if ((mask & (1 << f)) != 0) Note(notes, on, off, baseN + f);
                Note(notes, on, on + 30, baseN + (hopo ? 5 : 6));
                prevTick = on; prevMask = mask;
                lastMs = Math.Max(lastMs, ms + len);
            }
            var star = F($"{id}_{diffs[di]}_star") as List<object>;
            if (star != null)
                foreach (var x in star.OfType<List<object>>())
                {
                    var v = Qb.Ints(x);
                    if (v.Count < 2) continue;
                    long a = (long)Math.Round(Tick(v[0])), b = (long)Math.Round(Tick(v[0] + v[1]));
                    Note(notes, a, Math.Max(b, a + 1), baseN + 7);
                }
        }
        var events = new List<(long, byte[])> { (0, Meta(0x01, "[riffstorm_forced_hopo]")) };
        return (WriteMidi(Res, (id, tempo), ("PART GUITAR", notes), ("EVENTS", events)), lastMs);
    }

    static void Note(List<(long, byte[])> l, long on, long off, int n)
    {
        l.Add((on, new byte[] { 0x90, (byte)n, 100 }));
        l.Add((off, new byte[] { 0x80, (byte)n, 0 }));
    }

    static byte[] Meta(int type, string text)
    {
        var b = Encoding.Latin1.GetBytes(text);
        return new byte[] { 0xFF, (byte)type, (byte)b.Length }.Concat(b).ToArray();
    }

    static byte[] WriteMidi(int res, params (string name, List<(long tick, byte[] ev)> evs)[] tracks)
    {
        var ms = new MemoryStream();
        void W32(int v) { ms.WriteByte((byte)(v >> 24)); ms.WriteByte((byte)(v >> 16)); ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        void W16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        ms.Write("MThd"u8); W32(6); W16(1); W16(tracks.Length); W16(res);
        foreach (var (name, evs) in tracks)
        {
            var tr = new MemoryStream();
            void Vlq(long v)
            {
                var stack = new Stack<byte>(); stack.Push((byte)(v & 0x7F)); v >>= 7;
                while (v > 0) { stack.Push((byte)(0x80 | (v & 0x7F))); v >>= 7; }
                foreach (var b in stack) tr.WriteByte(b);
            }
            Vlq(0); tr.Write(Meta(0x03, name));
            long last = 0;
            // note-offs before note-ons at the same tick
            foreach (var (tick, ev) in evs.OrderBy(e => e.tick).ThenBy(e => e.ev[0] == 0x90 ? 1 : 0))
            {
                Vlq(tick - last); tr.Write(ev); last = tick;
            }
            Vlq(0); tr.Write(new byte[] { 0xFF, 0x2F, 0 });
            ms.Write("MTrk"u8); W32((int)tr.Length); tr.Position = 0; tr.CopyTo(ms);
        }
        return ms.ToArray();
    }
}
