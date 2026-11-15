using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>Fonts, palette and 2005-style drawing helpers (chrome/flame gradients, thick outlines).</summary>
public static class Gfx
{
    public static Font Title, Body, Bold, Hand;
    public static float S = 1;                     // UI scale (720p reference)
    public static int W => Raylib.GetScreenWidth();
    public static int H => Raylib.GetScreenHeight();

    public static readonly Color[] Fret =
    {
        new(30, 210, 40, 255), new(230, 30, 30, 255), new(250, 220, 20, 255), new(30, 90, 245, 255), new(255, 130, 10, 255)
    };
    public static readonly Color StarBlue = new(110, 220, 255, 255);
    public static readonly Color Flame1 = new(255, 245, 160, 255), Flame2 = new(255, 150, 20, 255), Flame3 = new(200, 30, 10, 255);
    public static readonly Color Chrome1 = new(255, 255, 255, 255), Chrome2 = new(150, 160, 175, 255), Chrome3 = new(60, 65, 80, 255);

    public static void Init()
    {
        var cps = Enumerable.Range(32, 224).Concat(new[] { 0x2022, 0x2013, 0x2014, 0x2018, 0x2019, 0x201C, 0x201D, 0x2192 }).ToArray();
        Font Load(params string[] names)
        {
            foreach (var n in names)
            {
                var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), n);
                if (File.Exists(p))
                {
                    var f = Raylib.LoadFontEx(p, 96, cps, cps.Length);
                    Raylib.GenTextureMipmaps(ref f.Texture);
                    Raylib.SetTextureFilter(f.Texture, TextureFilter.Trilinear);
                    return f;
                }
            }
            return Raylib.GetFontDefault();
        }
        Title = Load("impact.ttf", "ariblk.ttf", "arialbd.ttf");
        Bold = Load("ariblk.ttf", "arialbd.ttf", "arial.ttf");
        Body = Load("arialbd.ttf", "arial.ttf");
        Hand = Load("segoeprb.ttf", "Inkfree.ttf", "arialbd.ttf");
    }

    public static void UpdateScale() => S = Math.Min(W / 1280f, H / 720f);

    public static Vector2 Measure(Font f, string t, float size) => Raylib.MeasureTextEx(f, t, size * S, size * S * 0.04f);

    public static void Text(Font f, string t, float x, float y, float size, Color c, float align = 0)
    {
        float sz = size * S;
        var m = Raylib.MeasureTextEx(f, t, sz, sz * 0.04f);
        Raylib.DrawTextEx(f, t, new Vector2(x - m.X * align, y), sz, sz * 0.04f, c);
    }

    public static void Shadowed(Font f, string t, float x, float y, float size, Color c, float align = 0)
    {
        Text(f, t, x + 2 * S, y + 2 * S, size, new Color(0, 0, 0, (int)(c.A * 0.8f)), align);
        Text(f, t, x, y, size, c, align);
    }

    /// <summary>Outlined text with a vertical gradient fill – the classic mid-2000s logo look.</summary>
    public static void Fancy(Font f, string t, float x, float y, float size, Color top, Color bottom, float align = 0, float outline = 3, Color? outlineColor = null, float alpha = 1)
    {
        float sz = size * S;
        var m = Raylib.MeasureTextEx(f, t, sz, sz * 0.04f);
        x -= m.X * align;
        float o = outline * S;
        var oc = outlineColor ?? new Color(10, 5, 5, 255);
        oc.A = (byte)(oc.A * alpha);
        Raylib.DrawTextEx(f, t, new Vector2(x + o * 1.6f, y + o * 2.2f), sz, sz * 0.04f, new Color(0, 0, 0, (int)(150 * alpha)));
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * MathF.Tau;
            Raylib.DrawTextEx(f, t, new Vector2(x + MathF.Cos(a) * o, y + MathF.Sin(a) * o), sz, sz * 0.04f, oc);
        }
        int bands = Math.Max(4, (int)(sz / 6));
        float bh = m.Y / bands;
        for (int b = 0; b < bands; b++)
        {
            float k = b / (float)(bands - 1);
            var c = Lerp(top, bottom, k);
            c.A = (byte)(c.A * alpha);
            Raylib.BeginScissorMode((int)(x - o * 2), (int)(y + b * bh), (int)(m.X + o * 4), (int)Math.Ceiling(bh) + 1);
            Raylib.DrawTextEx(f, t, new Vector2(x, y), sz, sz * 0.04f, c);
            Raylib.EndScissorMode();
        }
    }

    public static void Flame(string t, float x, float y, float size, float align = 0, float alpha = 1) =>
        Fancy(Title, t, x, y, size, Flame1, Flame3, align, 3.5f, null, alpha);

    public static void Chrome(string t, float x, float y, float size, float align = 0, float alpha = 1) =>
        Fancy(Title, t, x, y, size, Chrome1, Chrome3, align, 3f, null, alpha);

    public static Color Lerp(Color a, Color b, float k) =>
        new((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k), (int)(a.A + (b.A - a.A) * k));

    public static Color A(Color c, float a) => new(c.R, c.G, c.B, (int)Math.Clamp(c.A * a, 0, 255));
    public static Color Bright(Color c, float k) => new((int)Math.Min(255, c.R * k), (int)Math.Min(255, c.G * k), (int)Math.Min(255, c.B * k), c.A);

    /// <summary>Beveled metal panel, like the brushed-steel menus of the era.</summary>
    public static void Panel(float x, float y, float w, float h, float alpha = 1, Color? tint = null)
    {
        var t = tint ?? new Color(40, 40, 48, 255);
        var r = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(new Rectangle(x + 5 * S, y + 7 * S, w, h), 0.08f, 6, new Color(0, 0, 0, (int)(120 * alpha)));
        Raylib.DrawRectangleRounded(r, 0.08f, 6, A(new Color(150, 150, 160, 255), alpha));
        var inner = new Rectangle(x + 3 * S, y + 3 * S, w - 6 * S, h - 6 * S);
        Raylib.DrawRectangleRounded(inner, 0.08f, 6, A(new Color(15, 15, 20, 255), alpha));
        var core = new Rectangle(x + 6 * S, y + 6 * S, w - 12 * S, h - 12 * S);
        Raylib.BeginScissorMode((int)core.X, (int)core.Y, (int)core.Width, (int)core.Height);
        Raylib.DrawRectangleGradientV((int)core.X, (int)core.Y, (int)core.Width, (int)core.Height, A(Bright(t, 1.6f), alpha * 0.95f), A(Bright(t, 0.5f), alpha * 0.95f));
        for (int i = 0; i < core.Height; i += Math.Max(2, (int)(3 * S)))
            Raylib.DrawRectangle((int)core.X, (int)(core.Y + i), (int)core.Width, 1, new Color(255, 255, 255, (int)(6 * alpha)));
        Raylib.EndScissorMode();
        // rivets
        foreach (var (rx, ry) in new[] { (x + 12 * S, y + 12 * S), (x + w - 12 * S, y + 12 * S), (x + 12 * S, y + h - 12 * S), (x + w - 12 * S, y + h - 12 * S) })
        {
            Raylib.DrawCircleV(new Vector2(rx, ry), 4 * S, A(new Color(90, 90, 100, 255), alpha));
            Raylib.DrawCircleV(new Vector2(rx - S, ry - S), 2 * S, A(new Color(220, 220, 230, 255), alpha));
        }
    }

    public static void Stars(float x, float y, float size, int count, int max = 5, float align = 0)
    {
        float step = size * 1.15f * S;
        x -= step * max * align;
        for (int i = 0; i < max; i++)
            StarShape(new Vector2(x + i * step + size * S / 2, y + size * S / 2), size * S / 2, i < count ? new Color(255, 215, 40, 255) : new Color(60, 60, 70, 200));
    }

    public static void StarShape(Vector2 c, float r, Color col)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = -MathF.PI / 2 + i * MathF.PI / 5;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            pts[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * rr;
        }
        for (int i = 0; i < 10; i++)
            Raylib.DrawTriangle(c, pts[(i + 1) % 10], pts[i], col);
        for (int i = 0; i < 10; i++)
            Raylib.DrawLineEx(pts[i], pts[(i + 1) % 10], Math.Max(1, r * 0.08f), new Color(40, 20, 0, (int)col.A));
    }

    /// <summary>Smoky dark backdrop with moving light beams, used behind menus.</summary>
    public static void MenuBackdrop(double t, Color tint)
    {
        Raylib.DrawRectangleGradientV(0, 0, W, H, Bright(tint, 0.35f), new Color(5, 3, 3, 255));
        Raylib.BeginBlendMode(BlendMode.Additive);
        for (int i = 0; i < 5; i++)
        {
            float ang = (float)(Math.Sin(t * 0.35 + i * 1.7) * 0.5);
            var o = new Vector2(W * (0.1f + i * 0.2f), -20);
            var d = new Vector2(MathF.Sin(ang), MathF.Cos(ang));
            var side = new Vector2(d.Y, -d.X);
            float len = H * 1.4f, wid = 160 * S;
            var c1 = A(i % 2 == 0 ? new Color(255, 120, 40, 255) : new Color(120, 140, 255, 255), 0.10f);
            Raylib.DrawTriangle(o, o + d * len - side * wid, o + d * len + side * wid, c1);
            Raylib.DrawTriangle(o, o + d * len + side * wid, o + d * len - side * wid, c1);
        }
        Raylib.EndBlendMode();
        // smoke blobs
        var rng = new Random(3);
        for (int i = 0; i < 18; i++)
        {
            float x = (float)((rng.NextDouble() * W + t * (10 + rng.NextDouble() * 20)) % (W + 400) - 200);
            float y = (float)(H * (0.55 + rng.NextDouble() * 0.5));
            Raylib.DrawCircleGradient(new Vector2(x, y), (float)(150 + rng.NextDouble() * 200) * S, new Color(90, 80, 80, 25), new Color(0, 0, 0, 0));
        }
        // vignette
        Raylib.DrawRectangleGradientV(0, H - (int)(160 * S), W, (int)(160 * S), new Color(0, 0, 0, 0), new Color(0, 0, 0, 200));
        Raylib.DrawRectangleGradientH(0, 0, (int)(200 * S), H, new Color(0, 0, 0, 160), new Color(0, 0, 0, 0));
        Raylib.DrawRectangleGradientH(W - (int)(200 * S), 0, (int)(200 * S), H, new Color(0, 0, 0, 0), new Color(0, 0, 0, 160));
    }

    public static void Logo(float cx, float y, float size, double t)
    {
        float wob = (float)Math.Sin(t * 2) * 2;
        Fancy(Title, "RIFFSTORM", cx, y + wob, size, new Color(255, 255, 210, 255), new Color(220, 40, 10, 255), 0.5f, 5, new Color(20, 0, 0, 255));
        var m = Measure(Title, "RIFFSTORM", size);
        // lightning slash under the logo
        float x0 = cx - m.X / 2, x1 = cx + m.X / 2, yy = y + m.Y * 0.95f + wob;
        Raylib.DrawTriangle(new Vector2(x0, yy), new Vector2(x1, yy + 4 * S), new Vector2(x1, yy - 3 * S), new Color(255, 200, 60, 230));
        Fancy(Bold, "TURNÊ ROCK 2005", cx, yy + 8 * S, size * 0.22f, Chrome1, Chrome2, 0.5f, 2);
    }
}
