using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>
/// Album covers. The game ships no album art: assets/covers.json maps each song to a MusicBrainz release group and the
/// front image is downloaded once from the Cover Art Archive into %LOCALAPPDATA%\Riffstorm\capas (a local
/// assets/covers/&lt;game&gt;/&lt;id&gt;.png also works). Songs without a cover get a generated one.
/// </summary>
public static class Covers
{
    static readonly Dictionary<string, Texture2D?> Cache = new();
    static Dictionary<string, string> _map;
    static readonly HashSet<string> _requested = new();
    static readonly System.Collections.Concurrent.ConcurrentQueue<(string key, string path, byte[] rgba, int w, int h)> _done = new();
    static readonly System.Collections.Concurrent.BlockingCollection<(string key, string rg, string path)> _jobs = new();
    static Thread _worker;

    static string CacheDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Riffstorm", "capas");

    static Dictionary<string, string> Map
    {
        get
        {
            if (_map != null) return _map;
            try { _map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "covers.json"))); }
            catch { }
            return _map ??= new();
        }
    }

    static Texture2D? LoadTex(string p)
    {
        var tx = Raylib.LoadTexture(p);
        if (tx.Id == 0) return null;
        Raylib.GenTextureMipmaps(ref tx);
        Raylib.SetTextureFilter(tx, TextureFilter.Trilinear);
        return tx;
    }

    public static Texture2D? Get(Song s)
    {
        if (s == null) return null;
        PumpDownloads();
        if (Cache.TryGetValue(s.Key, out var t)) return t;
        Texture2D? tex = null;
        var cached = Path.Combine(CacheDir, s.Game ?? "", s.Id + ".png");
        var local = Path.Combine(AppContext.BaseDirectory, "assets", "covers", s.Game ?? "", s.Id + ".png");
        if (File.Exists(cached)) tex = LoadTex(cached);
        else if (File.Exists(local)) tex = LoadTex(local);
        else if (Map.TryGetValue(s.Key, out var rg) && _requested.Add(s.Key)) Request(s.Key, rg, cached);
        Cache[s.Key] = tex;
        return tex;
    }

    static void Request(string key, string rg, string path)
    {
        _jobs.Add((key, rg, path));
        if (_worker != null) return;
        _worker = new Thread(() =>
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Riffstorm/1.0 (rhythm game; album art)");
            foreach (var (key2, rg2, path2) in _jobs.GetConsumingEnumerable())
            {
                try
                {
                    var bytes = http.GetByteArrayAsync($"https://coverartarchive.org/release-group/{rg2}/front-500").Result;
                    var img = StbImageSharp.ImageResult.FromMemory(bytes, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                    _done.Enqueue((key2, path2, img.Data, img.Width, img.Height));
                }
                catch { }
                Thread.Sleep(400);                               // be gentle with the public service
            }
        }) { IsBackground = true, Name = "covers" };
        _worker.Start();
    }

    /// <summary>Turns one finished download per frame into a texture (and saves it as PNG for next time).</summary>
    static unsafe void PumpDownloads()
    {
        if (!_done.TryDequeue(out var d)) return;
        void* mem = Raylib.MemAlloc((uint)d.rgba.Length);
        System.Runtime.InteropServices.Marshal.Copy(d.rgba, 0, (IntPtr)mem, d.rgba.Length);
        var img = new Image { Data = mem, Width = d.w, Height = d.h, Mipmaps = 1, Format = PixelFormat.UncompressedR8G8B8A8 };
        if (d.w > 512 || d.h > 512) Raylib.ImageResize(ref img, 512, 512);
        try { Directory.CreateDirectory(Path.GetDirectoryName(d.path)); Raylib.ExportImage(img, d.path); } catch { }
        var tx = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        if (tx.Id == 0) return;
        Raylib.GenTextureMipmaps(ref tx);
        Raylib.SetTextureFilter(tx, TextureFilter.Trilinear);
        Cache[d.key] = tx;
    }

    static Color Hue(string id, float sat, float val)
    {
        int h = 0; foreach (char c in id) h = h * 31 + c;
        return Raylib.ColorFromHSV(Math.Abs(h) % 360, sat, val);
    }

    /// <summary>Draws the cover (or the generated fallback) into a rectangle.</summary>
    public static void Draw(Song s, Rectangle r, Color tint, float rotation = 0)
    {
        var tex = Get(s);
        var origin = new Vector2(r.Width / 2, r.Height / 2);
        var dst = new Rectangle(r.X + origin.X, r.Y + origin.Y, r.Width, r.Height);
        if (tex is Texture2D tx)
        {
            Raylib.DrawTexturePro(tx, new Rectangle(0, 0, tx.Width, tx.Height), dst, origin, rotation, tint);
            return;
        }
        // fallback: two-tone gradient + title lettering
        var a = Hue(s.Id, 0.65f, 0.55f); var b = Hue(s.Id + "x", 0.8f, 0.18f);
        a = new Color(a.R * tint.R / 255, a.G * tint.G / 255, a.B * tint.B / 255, (int)tint.A);
        b = new Color(b.R * tint.R / 255, b.G * tint.G / 255, b.B * tint.B / 255, (int)tint.A);
        Raylib.DrawRectangleGradientEx(r, a, b, b, a);
        float fs = r.Height * 0.13f;
        var m = Raylib.MeasureTextEx(Gfx.Hand, s.Name, fs, 0);
        if (m.X > r.Width * 0.9f) { fs *= r.Width * 0.9f / m.X; m = Raylib.MeasureTextEx(Gfx.Hand, s.Name, fs, 0); }
        var ink = new Color(255, 245, 225, (int)(tint.A * 0.9f));
        Raylib.DrawTextEx(Gfx.Hand, s.Name, new Vector2(r.X + (r.Width - m.X) / 2, r.Y + r.Height * 0.42f - m.Y / 2), fs, 0, ink);
        float fa = fs * 0.55f;
        var ma = Raylib.MeasureTextEx(Gfx.Hand, s.Artist, fa, 0);
        Raylib.DrawTextEx(Gfx.Hand, s.Artist, new Vector2(r.X + (r.Width - ma.X) / 2, r.Y + r.Height * 0.58f), fa, 0, Gfx.A(ink, 0.75f));
    }

    /// <summary>Full-screen animated cover backdrop for gameplay and menus.</summary>
    public static void Backdrop(Song s, double t, float beat, bool star, bool failing, float dim = 0.55f)
    {
        int W = Gfx.W, H = Gfx.H;
        float side = MathF.Max(W, H);
        // slow drift + gentle zoom ("Ken Burns"), small kick on the beat
        float zoom = 1.12f + 0.06f * MathF.Sin((float)t * 0.045f) + MathF.Pow(1 - beat, 4) * 0.012f;
        float sz = side * zoom;
        float px = MathF.Sin((float)t * 0.031f) * 0.05f * W, py = MathF.Cos((float)t * 0.027f) * 0.05f * H;
        float rot = MathF.Sin((float)t * 0.02f) * 1.5f;
        Raylib.ClearBackground(Color.Black);
        byte k = (byte)(255 * dim);
        Draw(s, new Rectangle((W - sz) / 2 + px, (H - sz) / 2 + py, sz, sz), new Color(k, k, k, (byte)255), rot);
        // keep the highway readable: darker centre column and bottom, vignette on the sides
        Raylib.DrawRectangleGradientV(0, 0, W, H, new Color(0, 0, 0, 40), new Color(0, 0, 0, 170));
        Raylib.DrawRectangleGradientH(0, 0, W / 3, H, new Color(0, 0, 0, 120), new Color(0, 0, 0, 0));
        Raylib.DrawRectangleGradientH(W - W / 3, 0, W / 3, H, new Color(0, 0, 0, 0), new Color(0, 0, 0, 120));
        float pulse = MathF.Pow(1 - beat, 3);
        if (star) Raylib.DrawRectangle(0, 0, W, H, new Color(40, 120, 255, (int)(30 + pulse * 30)));
        if (failing) Raylib.DrawRectangle(0, 0, W, H, new Color(255, 0, 0, (int)(25 + pulse * 25)));
    }
}
