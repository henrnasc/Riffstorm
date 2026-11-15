using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Riffstorm;

/// <summary>
/// Reads Neversoft PS3 data files: DATA/&lt;path&gt; when it exists, else DATA/COMPRESSED/PS3/&lt;path&gt;, which is either
/// LZSS ("method 2" in COMPRESS.TOC — GH3, GH: Aerosmith) or a chain of deflate "CHNK" blocks (GH: Metallica).
/// </summary>
public sealed class NsData
{
    const string Data = "PS3_GAME/USRDIR/DATA/";
    const string Comp = Data + "COMPRESSED/PS3/";
    readonly IsoFs _iso;
    Dictionary<string, (int usize, int method)> _toc;

    public NsData(IsoFs iso) => _iso = iso;
    public IsoFs Iso => _iso;

    public bool Exists(string rel) => _iso.Exists(Data + rel) || _iso.Exists(Comp + rel);

    public byte[] Read(string rel)
    {
        if (_iso.Exists(Data + rel)) return _iso.Read(Data + rel);
        var raw = _iso.Read(Comp + rel);
        if (raw.Length >= 4 && raw[0] == 'C' && raw[1] == 'H' && raw[2] == 'N' && raw[3] == 'K') return Unchnk(raw);
        LoadToc();
        if (_toc.TryGetValue(rel.Replace('/', '\\').ToLowerInvariant(), out var e) && e.method == 2) return Unlzss(raw, e.usize);
        return raw;
    }

    /// <summary>A .pak plus its .pab, concatenated the way PAK entry offsets expect.</summary>
    public byte[] ReadPakPab(string pakRel)
    {
        var pak = Read(pakRel);
        string pabRel = pakRel[..^".PAK.PS3".Length] + ".PAB.PS3";
        if (!Exists(pabRel)) return pak;
        // the header part ends where the first entry's data starts (files in COMPRESSED are padded)
        int first = (int)BinaryPrimitives.ReadUInt32BigEndian(pak.AsSpan(4));
        if (first > 0 && first < pak.Length) pak = pak[..first];
        return pak.Concat(Read(pabRel)).ToArray();
    }

    void LoadToc()
    {
        if (_toc != null) return;
        _toc = new();
        try
        {
            // COMPRESS.TOC.LST: "Name: pak\qb.pab.ps3 <stored size> <id>"; COMPRESS.TOC: "TOC1", n, entries {id, usize, csize, method}
            var lst = Encoding.ASCII.GetString(_iso.Read(Comp + "COMPRESS.TOC.LST.PS3"));
            var toc = _iso.Read(Comp + "COMPRESS.TOC.PS3");
            int n = (int)BinaryPrimitives.ReadUInt32BigEndian(toc.AsSpan(4));
            var byId = new Dictionary<uint, (int, int)>();
            for (int i = 0; i < n && 0x20 + i * 16 + 16 <= toc.Length; i++)
            {
                int p = 0x20 + i * 16;
                byId[BinaryPrimitives.ReadUInt32BigEndian(toc.AsSpan(p))] =
                    ((int)BinaryPrimitives.ReadUInt32BigEndian(toc.AsSpan(p + 4)), (int)BinaryPrimitives.ReadUInt32BigEndian(toc.AsSpan(p + 12)));
            }
            // only the GH3 / Aerosmith layout (methods 0 and 2); Metallica's TOC is different and its files are CHNK anyway
            if (byId.Values.Any(v => v.Item2 != 0 && v.Item2 != 2)) return;
            foreach (Match m in Regex.Matches(lst, @"Name: (\S+) (\d+) ([0-9a-fA-F]+)"))
                if (byId.TryGetValue(Convert.ToUInt32(m.Groups[3].Value, 16), out var e)) _toc[m.Groups[1].Value.ToLowerInvariant()] = e;
        }
        catch { }
    }

    /// <summary>Okumura LZSS: 4 KB ring buffer pre-filled with spaces, writing from 0xFEE; flag byte, 1 = literal.</summary>
    public static byte[] Unlzss(byte[] src, int outSize)
    {
        const int N = 4096, F = 18, TH = 2;
        var buf = new byte[N]; System.Array.Fill(buf, (byte)' ');
        var outp = new byte[outSize];
        int r = N - F, i = 0, o = 0, flags = 0;
        while (i < src.Length && o < outSize)
        {
            flags >>= 1;
            if ((flags & 0x100) == 0) { flags = src[i++] | 0xFF00; if (i >= src.Length) break; }
            if ((flags & 1) != 0)
            {
                byte c = src[i++]; outp[o++] = c; buf[r] = c; r = (r + 1) & (N - 1);
            }
            else
            {
                if (i + 1 >= src.Length) break;
                int a = src[i], b = src[i + 1]; i += 2;
                int pos = a | ((b & 0xF0) << 4), len = (b & 0x0F) + TH + 1;
                for (int k = 0; k < len && o < outSize; k++)
                {
                    byte c = buf[(pos + k) & (N - 1)]; outp[o++] = c; buf[r] = c; r = (r + 1) & (N - 1);
                }
            }
        }
        return outp;
    }

    /// <summary>CHNK blocks: big-endian header {"CHNK", header size, compressed size, next chunk offset, ...} + raw deflate.</summary>
    public static byte[] Unchnk(byte[] d)
    {
        var outp = new MemoryStream();
        int p = 0;
        while (p + 16 <= d.Length && d[p] == 'C' && d[p + 1] == 'H' && d[p + 2] == 'N' && d[p + 3] == 'K')
        {
            int hs = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 4));
            int cs = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 8));
            uint nxtU = BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 12));
            using (var z = new DeflateStream(new MemoryStream(d, p + hs, Math.Min(cs, d.Length - p - hs)), CompressionMode.Decompress)) z.CopyTo(outp);
            if (nxtU == 0 || nxtU == 0xFFFFFFFF || nxtU > int.MaxValue) break;     // FFFFFFFF marks the last chunk
            int nxt = (int)nxtU;
            p += nxt;
        }
        return outp.ToArray();
    }

    /// <summary>Localized strings (.qs entries: UTF-16 lines <c>xxxxxxxx "text"</c>) from a pak, by QB key.</summary>
    public static Dictionary<uint, string> Strings(byte[] pak)
    {
        // the text blocks start at even or odd positions, so the whole pak is read as UTF-16 in both alignments
        var res = new Dictionary<uint, string>();
        foreach (int shift in new[] { 0, 1 })
        {
            var text = Encoding.BigEndianUnicode.GetString(pak, shift, (pak.Length - shift) & ~1);
            foreach (Match m in Regex.Matches(text, "([0-9a-fA-F]{8}) \"(.*?)\""))
            {
                string s = Regex.Replace(m.Groups[2].Value, @"\\[A-Za-z]", "").Trim();
                res.TryAdd(Convert.ToUInt32(m.Groups[1].Value, 16), s);
            }
        }
        return res;
    }
}
