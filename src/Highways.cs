using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Riffstorm;

/// <summary>
/// Original highway surfaces, read from each player's own disc when it is imported and saved as PNG next to the songs
/// (&lt;library&gt;/&lt;game&gt;/pistas/&lt;name&gt;.png). Nothing from the discs ships with the game.
/// GH1/GH2/80s: track/surfaces/gen/*.bmp_ps2 in the ARK (PS2 palette textures). GH3: MODELS/GUITARISTS/*_1_HIGHWAY.TEX/TVX,
/// Metallica: PAK/HIGHWAY_*.PAK + _VRAM (DXT1). GH: Aerosmith: the 512x1024 textures in the guitarist paks.
/// </summary>
public static class Highways
{
    public static string Dir(string lib, string game) => Path.Combine(lib, game, "pistas");

    /// <summary>Extraction already ran for this game (the folder exists even when the disc has none we can read).</summary>
    public static bool Has(string lib, string game) => Directory.Exists(Dir(lib, game));

    static readonly string[] Gh3Names = { "AXEL", "CASEY", "IZZY", "JOHNNY", "JUDY", "LARS", "MIDORI", "MORELLO", "RIPPER", "ROCKGOD", "SATAN", "SLASH", "XAVIER" };
    static readonly string[] GhmNames =
    {
        "ALT_JAMES", "ALT_KIRK", "ALT_LARS", "ALT_ROB", "AXEL", "BLACK", "CASEY", "IZZY", "JOHNNY", "JUDY", "JUSTICE", "KING", "KIRK",
        "LARS", "LARSU", "LEMMY", "LIGHTNING", "MIDORI", "PUPPETS", "ROB", "SKULLLOGO", "STANGER", "WINSTON", "ZACH",
        "ZOMBIE_JAMES", "ZOMBIE_KIRK", "ZOMBIE_LARS", "ZOMBIE_ROB",
    };

    /// <summary>Extracts every highway surface of an imported disc. Returns how many were saved.</summary>
    public static int Extract(string discPath, string lib, Importer.KnownGame kg)
    {
        string dir = Dir(lib, kg.Id);
        Directory.CreateDirectory(dir);
        int n = 0;
        void Save(string name, byte[] rgba, int w, int h)
        {
            try { SavePng(Path.Combine(dir, name.ToLowerInvariant() + ".png"), rgba, w, h); n++; } catch { }
        }
        switch (kg.Id)
        {
            case "gh1": case "gh2": case "gh80s":
            {
                using var disc = Disc.Open(discPath);
                foreach (var f in disc.Files.Keys.Where(k => k.Contains("track/surfaces/gen/", StringComparison.OrdinalIgnoreCase) && k.EndsWith(".bmp_ps2", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    try
                    {
                        var (rgba, w, h) = DecodePs2(disc.Read(f));
                        string name = Path.GetFileNameWithoutExtension(f).Replace("_keep", "");
                        Save(name, rgba, w, h);
                    }
                    catch { }
                }
                break;
            }
            case "gh3":
            {
                using var iso = new IsoFs(discPath);
                const string M = "PS3_GAME/USRDIR/DATA/MODELS/GUITARISTS/";
                foreach (var g in Gh3Names)
                {
                    try
                    {
                        string b = $"{M}{g}_1_HIGHWAY";
                        if (!iso.Exists(b + ".TEX.PS3")) continue;
                        if (BiggestDxt(iso.Read(b + ".TEX.PS3"), iso.Read(b + ".TVX.PS3")) is var (rgba, w, h)) Save(g, rgba, w, h);
                    }
                    catch { }
                }
                break;
            }
            case "ghm":
            {
                using var iso = new IsoFs(discPath);
                var ns = new NsData(iso);
                foreach (var g in GhmNames)
                {
                    try
                    {
                        string rel = $"PAK/HIGHWAY_{g}.PAK.PS3";
                        if (!ns.Exists(rel)) continue;
                        var (tex, tvx) = TexFromPak(ns.Read(rel), ns.Read(rel.Replace(".PAK.PS3", "_VRAM.PAK.PS3")));
                        if (BiggestDxt(tex, tvx) is var (rgba, w, h)) Save(g, rgba, w, h);
                        else Log($"{g}: sem textura 1:2 (tex {tex.Length} b, tvx {tvx.Length} b)");
                    }
                    catch (Exception ex) { Log($"{g}: {ex.GetType().Name} {ex.Message} @ {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"); }
                }
                break;
            }
            case "gha":
            {
                using var iso = new IsoFs(discPath);
                var ns = new NsData(iso);
                foreach (var (pak, name) in AeroPaks)
                {
                    try
                    {
                        if (!ns.Exists(pak)) continue;
                        var (tex, tvx) = TexFromPak(ns.Read(pak), ns.Read(pak.Replace(".PAK.PS3", "_VRAM.PAK.PS3")));
                        if (BiggestDxt(tex, tvx) is var (rgba, w, h)) Save(name, rgba, w, h);
                    }
                    catch { }
                }
                break;
            }
        }
        return n;
    }

    static void Log(string s) { try { File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "highways.log"), s + "\n"); } catch { } }

    /// <summary>GH: Aerosmith keeps each guitarist's highway in their pak (filled in from the disc scan).</summary>
    static readonly (string pak, string name)[] AeroPaks = System.Array.Empty<(string, string)>();

    // ------------------------------------------------------------------ PS2 palette textures (Harmonix)
    /// <summary>.bmp_ps2: 32-byte header (bpp @1, width @7, height @9, bytes per line @11), RGBA palette (alpha 0x80 = opaque), pixels.</summary>
    public static (byte[] rgba, int w, int h) DecodePs2(byte[] d)
    {
        int bpp = d[1], w = BitConverter.ToUInt16(d, 7), h = BitConverter.ToUInt16(d, 9), bpl = BitConverter.ToUInt16(d, 11);
        if (bpp != 4 && bpp != 8) throw new InvalidDataException("bpp " + bpp);
        int n = 1 << bpp, p = 32;
        var pal = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            int src = i;
            if (bpp == 8) { int r = i & 31, blk = i & ~31; src = r is >= 8 and < 16 ? blk + r + 8 : r is >= 16 and < 24 ? blk + r - 8 : i; }   // PS2 CLUT order
            for (int c = 0; c < 3; c++) pal[i * 4 + c] = d[p + src * 4 + c];
            pal[i * 4 + 3] = (byte)Math.Min(255, d[p + src * 4 + 3] * 2);
        }
        p += n * 4;
        var outp = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int row = p + y * bpl;
                int idx = bpp == 8 ? d[row + x] : (d[row + (x >> 1)] >> (4 * (x & 1))) & 15;
                System.Array.Copy(pal, idx * 4, outp, (y * w + x) * 4, 4);
            }
        return (outp, w, h);
    }

    // ------------------------------------------------------------------ PS3 texture dictionaries (Neversoft)
    /// <summary>The .tex entry and the pixel data (.tvx) of a pak + its _VRAM pak.</summary>
    static (byte[] tex, byte[] tvx) TexFromPak(byte[] pak, byte[] vram)
    {
        uint texT = Qb.K(".tex"), tvxT = Qb.K(".tvx");
        byte[] tex = null, tvx = null;
        foreach (var e in Qb.ReadPak(pak)) if (e.Type == texT && e.Offset + e.Size <= pak.Length) { tex = pak.AsSpan(e.Offset, e.Size).ToArray(); break; }
        foreach (var e in Qb.ReadPak(vram)) if (e.Type == tvxT && e.Offset + e.Size <= vram.Length) { tvx = vram.AsSpan(e.Offset, e.Size).ToArray(); break; }
        if (tex == null || tvx == null) throw new InvalidDataException("pak sem textura");
        return (tex, tvx);
    }

    /// <summary>
    /// .tex: entries tagged 0C 30 32 00 = {u32 key, u16 w, u16 h, u16 depth, u16 w, u16 h, u16 depth, u8 mips, u8 fmt, u16 ?,
    /// u32 ?, u32 data offset, u32 data size} (big endian). Pixels are plain DXT1 (little endian blocks).
    /// Returns the largest DXT1 texture with a 1:2 aspect (the highway).
    /// </summary>
    static (byte[] rgba, int w, int h)? BiggestDxt(byte[] tex, byte[] tvx)
    {
        (int w, int h, int off, int size)? best = null;
        for (int p = tex.AsSpan().IndexOf(new byte[] { 0x0C, 0x30, 0x32, 0x00 }); p >= 0 && p + 36 <= tex.Length;)
        {
            int w = BinaryPrimitives.ReadUInt16BigEndian(tex.AsSpan(p + 8)), h = BinaryPrimitives.ReadUInt16BigEndian(tex.AsSpan(p + 10));
            int off = (int)BinaryPrimitives.ReadUInt32BigEndian(tex.AsSpan(p + 28)), size = (int)BinaryPrimitives.ReadUInt32BigEndian(tex.AsSpan(p + 32));
            bool dxt1 = size >= w * h / 2 && size < w * h;
            if (dxt1 && h == w * 2 && off + w * h / 2 <= tvx.Length && (best == null || w > best.Value.w)) best = (w, h, off, size);
            int nx = tex.AsSpan(p + 4).IndexOf(new byte[] { 0x0C, 0x30, 0x32, 0x00 });
            if (nx < 0) break;
            p += 4 + nx;
        }
        if (best is not var (bw, bh, boff, _)) return null;
        return (DecodeDxt1(tvx.AsSpan(boff), bw, bh), bw, bh);
    }

    public static byte[] DecodeDxt1(ReadOnlySpan<byte> src, int w, int h)
    {
        var outp = new byte[w * h * 4];
        var pal = new byte[16];
        int bw = (w + 3) / 4, bh = (h + 3) / 4;
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                int b = (by * bw + bx) * 8;
                if (b + 8 > src.Length) return outp;
                int c0 = src[b] | src[b + 1] << 8, c1 = src[b + 2] | src[b + 3] << 8;
                void Rgb(int c, int i) { pal[i] = (byte)((c >> 11 & 31) * 255 / 31); pal[i + 1] = (byte)((c >> 5 & 63) * 255 / 63); pal[i + 2] = (byte)((c & 31) * 255 / 31); pal[i + 3] = 255; }
                Rgb(c0, 0); Rgb(c1, 4);
                for (int k = 0; k < 3; k++)
                {
                    if (c0 > c1) { pal[8 + k] = (byte)((2 * pal[k] + pal[4 + k]) / 3); pal[12 + k] = (byte)((pal[k] + 2 * pal[4 + k]) / 3); }
                    else { pal[8 + k] = (byte)((pal[k] + pal[4 + k]) / 2); pal[12 + k] = 0; }
                }
                pal[11] = 255; pal[15] = (byte)(c0 > c1 ? 255 : 0);
                uint bits = (uint)(src[b + 4] | src[b + 5] << 8 | src[b + 6] << 16 | src[b + 7] << 24);
                for (int py = 0; py < 4; py++)
                    for (int px = 0; px < 4; px++)
                    {
                        int x = bx * 4 + px, y = by * 4 + py;
                        if (x >= w || y >= h) continue;
                        int idx = (int)(bits >> (2 * (py * 4 + px)) & 3);
                        System.Array.Copy(pal, idx * 4, outp, (y * w + x) * 4, 4);
                    }
            }
        return outp;
    }

    static unsafe void SavePng(string path, byte[] rgba, int w, int h)
    {
        void* mem = Raylib.MemAlloc((uint)rgba.Length);
        Marshal.Copy(rgba, 0, (IntPtr)mem, rgba.Length);
        var img = new Image { Data = mem, Width = w, Height = h, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
        Raylib.ExportImage(img, path);
        Raylib.UnloadImage(img);
    }

    // ------------------------------------------------------------------ catalogue + the one in use
    public sealed record Entry(string Game, string Name, string Path)
    {
        public string Key => Game + "/" + Name;
    }

    public static List<Entry> All(string lib)
    {
        var res = new List<Entry>();
        foreach (var g in SongDb.Games)
        {
            string d = Dir(lib, g.Id);
            if (!Directory.Exists(d)) continue;
            foreach (var f in Directory.EnumerateFiles(d, "*.png").OrderBy(x => x))
                res.Add(new Entry(g.Id, System.IO.Path.GetFileNameWithoutExtension(f), f));
        }
        return res;
    }

    static string _loadedKey;
    static Texture2D? _tex;
    static readonly Dictionary<string, Texture2D> _thumbs = new();

    /// <summary>The texture for the configured highway (null = the Riffstorm one).</summary>
    public static Texture2D? Current(Settings cfg)
    {
        string key = cfg.Highway;
        if (key == _loadedKey) return _tex;
        if (_tex is Texture2D old) Raylib.UnloadTexture(old);
        _tex = null; _loadedKey = key;
        if (string.IsNullOrEmpty(key)) return null;
        var parts = key.Split('/');
        if (parts.Length != 2) return null;
        string path = System.IO.Path.Combine(Dir(cfg.LibraryPath ?? SongDb.DefaultRoot, parts[0]), parts[1] + ".png");
        if (!File.Exists(path)) return null;
        var t = Raylib.LoadTexture(path);
        if (t.Id == 0) return null;
        Raylib.GenTextureMipmaps(ref t);
        Raylib.SetTextureFilter(t, TextureFilter.Trilinear);
        Raylib.SetTextureWrap(t, TextureWrap.Repeat);
        _tex = t;
        return _tex;
    }

    public static Texture2D? Thumb(Entry e)
    {
        if (_thumbs.TryGetValue(e.Path, out var t)) return t;
        if (!File.Exists(e.Path)) return null;
        t = Raylib.LoadTexture(e.Path);
        if (t.Id == 0) return null;
        Raylib.SetTextureFilter(t, TextureFilter.Bilinear);
        _thumbs[e.Path] = t;
        return t;
    }
}
