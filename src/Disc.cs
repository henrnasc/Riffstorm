using System.Text;

namespace Riffstorm;

/// <summary>Reads GEN/MAIN.HDR + GEN/MAIN_0.ARK straight from the user's ISO (ISO9660) or a mounted/extracted folder.</summary>
public sealed class Disc : IDisposable
{
    readonly FileStream _ark;
    readonly long _arkBase;
    readonly object _lock = new();
    public readonly Dictionary<string, (long off, int size)> Files = new(StringComparer.OrdinalIgnoreCase);
    public string SourcePath { get; }

    Disc(string src, FileStream ark, long arkBase, byte[] hdr)
    {
        SourcePath = src; _ark = ark; _arkBase = arkBase;
        ParseHdr(hdr);
    }

    public static Disc Open(string path)
    {
        if (Directory.Exists(path))
        {
            string hdr = Path.Combine(path, "GEN", "MAIN.HDR");
            string ark = Path.Combine(path, "GEN", "MAIN_0.ARK");
            if (!File.Exists(hdr) || !File.Exists(ark)) throw new FileNotFoundException("GEN/MAIN.HDR não encontrado em " + path);
            return new Disc(path, new FileStream(ark, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16), 0, File.ReadAllBytes(hdr));
        }
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        try
        {
            var pvd = new byte[2048];
            fs.Position = 16 * 2048; fs.ReadExactly(pvd);
            if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001")
                throw new InvalidDataException("O arquivo não é uma ISO válida.");
            long rootLba = BitConverter.ToUInt32(pvd, 156 + 2), rootSize = BitConverter.ToUInt32(pvd, 156 + 10);
            var gen = FindInDir(fs, rootLba, rootSize, "GEN");
            if (gen.lba < 0) throw new InvalidDataException("ISO sem pasta GEN – não parece ser o disco certo.");
            var hdrE = FindInDir(fs, gen.lba, gen.size, "MAIN.HDR");
            var arkE = FindInDir(fs, gen.lba, gen.size, "MAIN_0.ARK");
            if (hdrE.lba < 0 || arkE.lba < 0) throw new InvalidDataException("MAIN.HDR/MAIN_0.ARK ausentes na ISO.");
            var hdr = new byte[hdrE.size];
            fs.Position = hdrE.lba * 2048; fs.ReadExactly(hdr);
            return new Disc(path, fs, arkE.lba * 2048, hdr);
        }
        catch { fs.Dispose(); throw; }
    }

    /// <summary>Reads SYSTEM.CNF from an ISO or a disc folder (to identify the game by its serial).</summary>
    public static string ReadSystemCnf(string path)
    {
        if (Directory.Exists(path))
        {
            var f = Path.Combine(path, "SYSTEM.CNF");
            return File.Exists(f) ? File.ReadAllText(f) : null;
        }
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var pvd = new byte[2048];
        fs.Position = 16 * 2048; fs.ReadExactly(pvd);
        if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001") throw new InvalidDataException("O arquivo não é uma ISO válida.");
        var e = FindInDir(fs, BitConverter.ToUInt32(pvd, 156 + 2), BitConverter.ToUInt32(pvd, 156 + 10), "SYSTEM.CNF");
        if (e.lba < 0) return null;
        var buf = new byte[e.size];
        fs.Position = e.lba * 2048; fs.ReadExactly(buf);
        return Encoding.ASCII.GetString(buf);
    }

    static (long lba, long size) FindInDir(FileStream fs, long lba, long size, string name)
    {
        var buf = new byte[size];
        fs.Position = lba * 2048; fs.ReadExactly(buf);
        int p = 0;
        while (p < buf.Length)
        {
            int len = buf[p];
            if (len == 0) { p = (p / 2048 + 1) * 2048; continue; }
            int nl = buf[p + 32];
            string n = Encoding.ASCII.GetString(buf, p + 33, nl);
            int semi = n.IndexOf(';'); if (semi >= 0) n = n[..semi];
            if (n.Equals(name, StringComparison.OrdinalIgnoreCase))
                return (BitConverter.ToUInt32(buf, p + 2), BitConverter.ToUInt32(buf, p + 10));
            p += len;
        }
        return (-1, 0);
    }

    void ParseHdr(byte[] d)
    {
        int p = 0;
        int I() { int v = BitConverter.ToInt32(d, p); p += 4; return v; }
        if (I() != 3) throw new InvalidDataException("Versão de ARK inesperada (esperado GH1 = 3).");
        I(); int nSizes = I(); p += 4 * nSizes;
        int tblLen = I(); int tbl = p; p += tblLen;
        int nOff = I(); var offs = new int[nOff];
        for (int i = 0; i < nOff; i++) offs[i] = I();
        string S(int i) { int o = tbl + offs[i]; int e = o; while (d[e] != 0) e++; return Encoding.Latin1.GetString(d, o, e - o); }
        int n = I();
        for (int i = 0; i < n; i++)
        {
            uint off = BitConverter.ToUInt32(d, p); int ni = BitConverter.ToInt32(d, p + 4), di = BitConverter.ToInt32(d, p + 8);
            int size = BitConverter.ToInt32(d, p + 12); p += 20;
            string dir = S(di).Replace("../", "");
            Files[dir + "/" + S(ni)] = (off, size);
        }
    }

    public bool Has(string name) => Files.ContainsKey(name);
    public int Size(string name) => Files[name].size;

    public byte[] Read(string name, long start = 0, int count = -1)
    {
        if (!Files.TryGetValue(name, out var e)) throw new FileNotFoundException(name);
        if (count < 0) count = (int)(e.size - start);
        count = (int)Math.Max(0, Math.Min(count, e.size - start));
        var buf = new byte[count];
        lock (_lock) { _ark.Position = _arkBase + e.off + start; _ark.ReadExactly(buf); }
        return buf;
    }

    public void Dispose() => _ark.Dispose();
}
