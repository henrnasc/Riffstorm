using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>
/// "Sketchbook" UI kit shared by every menu (same look as the title screen): aged parchment backdrop,
/// taped paper cards, notebook pages, ribbons, hand-lettered ink text, red marker boxes, polaroids.
/// </summary>
public static class Paper
{
    public static readonly Color Ink = new(28, 20, 14, 255);
    public static readonly Color InkSoft = new(70, 55, 40, 255);
    public static readonly Color Red = new(185, 25, 22, 255);
    public static readonly Color Teal = new(30, 130, 130, 255);
    public static readonly Color Cream = new(240, 230, 205, 255);

    static Texture2D _art; static bool _artTried, _hasArt;
    static Texture2D _paper;

    static void Load()
    {
        if (_artTried) return; _artTried = true;
        var p = Path.Combine(AppContext.BaseDirectory, "assets", "ui", "title_bg.png");
        if (File.Exists(p))
        {
            _art = Raylib.LoadTexture(p);
            Raylib.GenTextureMipmaps(ref _art);
            Raylib.SetTextureFilter(_art, TextureFilter.Trilinear);
            _hasArt = true;
        }
        _paper = Tex.Make(512, 288, (x, y) =>
        {
            float n = Tex.Fbm(x / 60f, y / 60f, 4, 5), s = Tex.Fbm(x / 14f, y / 14f, 9, 3);
            float ex = MathF.Min(x, 511 - x) / 512f, ey = MathF.Min(y, 287 - y) / 288f;
            float burn = Math.Clamp((MathF.Min(ex, ey) - 0.02f + (n - 0.5f) * 0.05f) * 14, 0, 1);
            float b = (0.78f + n * 0.18f - s * 0.06f) * (0.35f + 0.65f * burn);
            return Tex.C(b, b * 0.88f, b * 0.66f);
        });
    }

    /// <summary>Parchment background: the empty right part of the title art (stains, burnt edges), drifting slowly.</summary>
    public static void Backdrop(double t, bool stars = true)
    {
        Load();
        int W = Gfx.W, H = Gfx.H;
        Raylib.ClearBackground(new Color(14, 10, 8, 255));
        float z = 1.04f + 0.012f * MathF.Sin((float)t * 0.1f);
        float dx = MathF.Sin((float)t * 0.06f) * 10 * Gfx.S, dy = MathF.Cos((float)t * 0.08f) * 8 * Gfx.S;
        var dst = new Rectangle((W - W * z) / 2 + dx, (H - H * z) / 2 + dy, W * z, H * z);
        if (_hasArt)
        {
            // the plain parchment strip of the title art (right of the illustration), stretched to the screen
            var src = new Rectangle(_art.Width * 0.745f, 0, _art.Width * 0.24f, _art.Height);
            Raylib.DrawTexturePro(_art, src, dst, Vector2.Zero, 0, Color.White);
            // burnt side edges (the strip only had the right one)
            Raylib.DrawRectangleGradientH(0, 0, (int)(70 * Gfx.S), H, new Color(25, 12, 5, 200), new Color(0, 0, 0, 0));
            Raylib.DrawRectangleGradientH(W - (int)(70 * Gfx.S), 0, (int)(70 * Gfx.S), H, new Color(0, 0, 0, 0), new Color(25, 12, 5, 200));
        }
        else Raylib.DrawTexturePro(_paper, new Rectangle(0, 0, _paper.Width, _paper.Height), dst, Vector2.Zero, 0, Color.White);
        // soft burnt vignette
        Raylib.DrawRectangleGradientV(0, 0, W, (int)(60 * Gfx.S), new Color(20, 10, 5, 120), new Color(0, 0, 0, 0));
        Raylib.DrawRectangleGradientV(0, H - (int)(60 * Gfx.S), W, (int)(60 * Gfx.S), new Color(0, 0, 0, 0), new Color(20, 10, 5, 120));
        if (!stars) return;
        (float u, float v, Color c, float s)[] st =
        {
            (0.03f, 0.08f, Teal, 12), (0.97f, 0.12f, Red, 11), (0.5f, 0.04f, Teal, 9), (0.02f, 0.9f, Red, 10), (0.96f, 0.93f, Teal, 11),
        };
        for (int i = 0; i < st.Length; i++)
        {
            float k = 0.75f + 0.3f * MathF.Sin((float)t * 2.2f + i * 1.7f);
            Gfx.StarShape(new Vector2(st[i].u * W, st[i].v * H), st[i].s * Gfx.S * k, Gfx.A(st[i].c, 0.85f));
        }
    }

    static void Tri(Vector2 a, Vector2 b, Vector2 c, Color col) { Raylib.DrawTriangle(a, b, c, col); Raylib.DrawTriangle(a, c, b, col); }
    static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col) { Tri(a, b, c, col); Tri(a, c, d, col); }
    static Vector2 Rot(Vector2 c, float x, float y, float rad) { float cs = MathF.Cos(rad), sn = MathF.Sin(rad); return c + new Vector2(x * cs - y * sn, x * sn + y * cs); }

    /// <summary>A sheet of paper (optionally ruled like a notebook) held by tape.</summary>
    public static void Card(Rectangle r, float rotDeg = 0, bool tape = true, bool ruled = false, Color? fill = null, float alpha = 1)
    {
        float rad = rotDeg * MathF.PI / 180;
        var c = new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2);
        float hw = r.Width / 2, hh = r.Height / 2;
        var f = fill ?? Cream;
        Vector2 P(float x, float y) => Rot(c, x, y, rad);
        // shadow, sheet, ink edge
        var sh = new Vector2(5, 7) * Gfx.S;
        Quad(P(-hw, -hh) + sh, P(hw, -hh) + sh, P(hw, hh) + sh, P(-hw, hh) + sh, new Color(30, 18, 8, (int)(90 * alpha)));
        Quad(P(-hw, -hh), P(hw, -hh), P(hw, hh), P(-hw, hh), Gfx.A(f, alpha));
        // slight aged gradient
        Quad(P(-hw, hh * 0.3f), P(hw, hh * 0.3f), P(hw, hh), P(-hw, hh), new Color(150, 110, 60, (int)(18 * alpha)));
        if (ruled)
        {
            for (float y = -hh + 46 * Gfx.S; y < hh - 8 * Gfx.S; y += 34 * Gfx.S)
                Raylib.DrawLineEx(P(-hw + 6 * Gfx.S, y), P(hw - 6 * Gfx.S, y), 1.2f * Gfx.S, new Color(90, 130, 190, (int)(70 * alpha)));
            Raylib.DrawLineEx(P(-hw + 52 * Gfx.S, -hh + 4 * Gfx.S), P(-hw + 52 * Gfx.S, hh - 4 * Gfx.S), 1.6f * Gfx.S, new Color(200, 60, 60, (int)(110 * alpha)));
        }
        var ink = Gfx.A(Ink, 0.85f * alpha);
        Raylib.DrawLineEx(P(-hw, -hh), P(hw, -hh), 2 * Gfx.S, ink); Raylib.DrawLineEx(P(hw, -hh), P(hw, hh), 2 * Gfx.S, ink);
        Raylib.DrawLineEx(P(hw, hh), P(-hw, hh), 2 * Gfx.S, ink); Raylib.DrawLineEx(P(-hw, hh), P(-hw, -hh), 2 * Gfx.S, ink);
        if (tape)
            foreach (int s in new[] { -1, 1 })
            {
                var tc = P(s * (hw - 30 * Gfx.S), -hh);
                float tr = rad + s * 0.6f;
                Vector2 T(float x, float y) => Rot(tc, x, y, tr);
                Quad(T(-30 * Gfx.S, -10 * Gfx.S), T(30 * Gfx.S, -10 * Gfx.S), T(30 * Gfx.S, 10 * Gfx.S), T(-30 * Gfx.S, 10 * Gfx.S), new Color(235, 225, 180, (int)(170 * alpha)));
            }
    }

    /// <summary>Hand-lettered ink text. align 0 = left, 0.5 = centre, 1 = right.</summary>
    public static Vector2 Text(string t, float x, float y, float size, Color col, float align = 0, float rotDeg = 0, Font? font = null)
    {
        var f = font ?? Gfx.Hand;
        float sz = size * Gfx.S;
        var m = Raylib.MeasureTextEx(f, t, sz, 0);
        Raylib.DrawTextPro(f, t, new Vector2(x, y), new Vector2(m.X * align, 0), rotDeg, sz, 0, col);
        return m;
    }

    public static string Fit(string t, float maxW, float size, Font? font = null)
    {
        var f = font ?? Gfx.Hand;
        while (t.Length > 4 && Raylib.MeasureTextEx(f, t, size * Gfx.S, 0).X > maxW) t = t[..^4] + "...";
        return t;
    }

    /// <summary>Parchment ribbon with folded, notched tails (the title-screen banner).</summary>
    public static void Ribbon(Vector2 c, float w, float h, float rotDeg, string text, bool enabled, bool selected, double t, float textScale = 1)
    {
        float rot = rotDeg * MathF.PI / 180;
        Vector2 R(float x, float y) => Rot(c, x, y, rot);
        var ink = new Color(25, 18, 12, enabled ? 255 : 170);
        var face = enabled ? new Color(236, 222, 188, 255) : new Color(150, 138, 116, 255);
        var back = enabled ? new Color(196, 176, 136, 255) : new Color(118, 106, 88, 255);
        float hw = w / 2, hh = h / 2, tail = h * 0.9f, drop = h * 0.28f;
        foreach (int s in new[] { -1, 1 })
        {
            var a = R(s * (hw - tail * 0.4f), -hh + drop); var b = R(s * (hw + tail * 0.75f), -hh + drop);
            var notch = R(s * (hw + tail * 0.45f), drop); var d = R(s * (hw + tail * 0.75f), hh + drop); var e = R(s * (hw - tail * 0.4f), hh + drop);
            Quad(a, b, notch, e, back); Tri(e, notch, d, back);
            Raylib.DrawLineEx(a, b, 2.4f * Gfx.S, ink); Raylib.DrawLineEx(b, notch, 2.4f * Gfx.S, ink); Raylib.DrawLineEx(notch, d, 2.4f * Gfx.S, ink); Raylib.DrawLineEx(d, e, 2.4f * Gfx.S, ink);
            Tri(R(s * hw, hh), R(s * (hw - tail * 0.4f), hh + drop), R(s * (hw - tail * 0.4f), hh), new Color(60, 45, 30, 200));
        }
        int seg = 14;
        var top = new Vector2[seg + 1]; var bot = new Vector2[seg + 1];
        for (int i = 0; i <= seg; i++)
        {
            float x = -hw + w * i / seg;
            float wave = MathF.Sin(i / (float)seg * MathF.PI * 2 + (float)t * 1.6f) * 3.2f * Gfx.S;
            top[i] = R(x, -hh + wave); bot[i] = R(x, hh + wave);
        }
        for (int i = 0; i < seg; i++) Quad(top[i], top[i + 1], bot[i + 1], bot[i], face);
        for (int i = 0; i < seg; i++) { Raylib.DrawLineEx(top[i], top[i + 1], 2.8f * Gfx.S, ink); Raylib.DrawLineEx(bot[i], bot[i + 1], 2.8f * Gfx.S, ink); }
        Raylib.DrawLineEx(top[0], bot[0], 2.8f * Gfx.S, ink); Raylib.DrawLineEx(top[seg], bot[seg], 2.8f * Gfx.S, ink);
        for (int i = 1; i < seg; i += 3) Raylib.DrawLineEx(Vector2.Lerp(top[i], bot[i], 0.82f), Vector2.Lerp(top[i], bot[i], 0.95f), 1.2f * Gfx.S, Gfx.A(ink, 0.35f));
        float size = MathF.Min(h * 0.62f, w * 1.6f / MathF.Max(8, text.Length)) * textScale;
        var m = Raylib.MeasureTextEx(Gfx.Hand, text, size, 0);
        Raylib.DrawTextPro(Gfx.Hand, text, R(0, -h * 0.03f), m / 2, rotDeg, size, 0, enabled ? new Color(20, 14, 10, 255) : new Color(45, 38, 30, 200));
    }

    /// <summary>Red marker rectangle that "boils" like hand-drawn animation.</summary>
    public static void RedBox(Rectangle r, double t, float thick = 3.2f)
    {
        var rnd = new Random((int)(t * 8));
        Vector2 J() => new((float)(rnd.NextDouble() - 0.5) * 6 * Gfx.S, (float)(rnd.NextDouble() - 0.5) * 6 * Gfx.S);
        var red = Gfx.A(Red, 0.92f);
        for (int pass = 0; pass < 2; pass++)
        {
            var p0 = new Vector2(r.X, r.Y) + J(); var p1 = new Vector2(r.X + r.Width, r.Y) + J();
            var p2 = new Vector2(r.X + r.Width, r.Y + r.Height) + J(); var p3 = new Vector2(r.X, r.Y + r.Height) + J();
            Raylib.DrawLineEx(p0, p1 + new Vector2(8 * Gfx.S, 0), thick * Gfx.S, red); Raylib.DrawLineEx(p1, p2, thick * Gfx.S, red);
            Raylib.DrawLineEx(p2, p3 - new Vector2(9 * Gfx.S, 0), thick * Gfx.S, red); Raylib.DrawLineEx(p3, p0, thick * Gfx.S, red);
        }
    }

    /// <summary>Red marker circle (for picking one of several options).</summary>
    public static void RedCircle(Vector2 c, float rx, float ry, double t)
    {
        var rnd = new Random((int)(t * 8) + 3);
        int n = 28;
        Vector2 prev = default;
        for (int i = 0; i <= n + 3; i++)
        {
            float a = i / (float)n * MathF.Tau - 0.4f;
            float wob = 1 + (float)(rnd.NextDouble() - 0.5) * 0.06f;
            var p = c + new Vector2(MathF.Cos(a) * rx * wob, MathF.Sin(a) * ry * wob);
            if (i > 0) Raylib.DrawLineEx(prev, p, 3 * Gfx.S, Gfx.A(Red, 0.9f));
            prev = p;
        }
    }

    /// <summary>Curved red arrow pointing right at <paramref name="tip"/>.</summary>
    public static void Arrow(Vector2 tip, double t, float len = 70)
    {
        float bob = MathF.Sin((float)t * 5) * 6 * Gfx.S;
        tip += new Vector2(bob, 0);
        var tail = tip + new Vector2(-len * Gfx.S, -len * 0.7f * Gfx.S);
        var mid = (tip + tail) / 2 + new Vector2(10 * Gfx.S, -14 * Gfx.S);
        Raylib.DrawSplineSegmentBezierQuadratic(tail, mid, tip, 3.6f * Gfx.S, Red);
        var dir = Vector2.Normalize(tip - mid); var side = new Vector2(-dir.Y, dir.X);
        Tri(tip + dir * 6 * Gfx.S, tip - dir * 16 * Gfx.S + side * 10 * Gfx.S, tip - dir * 16 * Gfx.S - side * 10 * Gfx.S, Red);
    }

    /// <summary>Album cover as a taped polaroid.</summary>
    public static void Polaroid(Song s, Vector2 c, float size, float rotDeg, float alpha = 1)
    {
        float pad = size * 0.06f, bottom = size * 0.2f;
        var frame = new Rectangle(c.X - size / 2 - pad, c.Y - size / 2 - pad, size + pad * 2, size + pad + bottom);
        Card(frame, rotDeg, true, false, new Color(250, 248, 240, 255), alpha);
        float rad = rotDeg * MathF.PI / 180;
        var fc = new Vector2(frame.X + frame.Width / 2, frame.Y + frame.Height / 2);
        var imgC = Rot(fc, 0, -(bottom - pad) / 2, rad);
        Covers.Draw(s, new Rectangle(imgC.X - size / 2, imgC.Y - size / 2, size, size), Gfx.A(Color.White, alpha), rotDeg);
    }

    /// <summary>Hand-drawn stars (filled = earned).</summary>
    public static void Stars(float x, float y, float size, int count, int max = 5, float align = 0)
    {
        float step = size * 1.2f * Gfx.S;
        x -= step * max * align;
        for (int i = 0; i < max; i++)
        {
            var c = new Vector2(x + i * step + size * Gfx.S / 2, y + size * Gfx.S / 2);
            if (i < count) Gfx.StarShape(c, size * Gfx.S / 2, new Color(215, 40, 30, 255));
            else Gfx.StarShape(c, size * Gfx.S / 2, new Color(200, 185, 150, 160));
        }
    }

    /// <summary>Rubber stamp text (e.g. "NOVO RECORDE!", "EM BREVE").</summary>
    public static void Stamp(string text, Vector2 c, float size, float rotDeg, Color col)
    {
        float ss = size * Gfx.S;
        var m = Raylib.MeasureTextEx(Gfx.Bold, text, ss, 1);
        var pad = new Vector2(14, 8) * Gfx.S;
        var r = new Rectangle(c.X, c.Y, m.X + pad.X * 2, m.Y + pad.Y * 2);
        Raylib.DrawRectanglePro(r, new Vector2(r.Width, r.Height) / 2, rotDeg, Gfx.A(col, 0.85f));
        Raylib.DrawRectanglePro(new Rectangle(c.X, c.Y, r.Width - 6 * Gfx.S, r.Height - 6 * Gfx.S), new Vector2(r.Width - 6 * Gfx.S, r.Height - 6 * Gfx.S) / 2, rotDeg, new Color(250, 240, 225, 60));
        Raylib.DrawTextPro(Gfx.Bold, text, c, m / 2, rotDeg, ss, 1, new Color(252, 244, 232, 255));
    }

    /// <summary>Footer button hints, right-aligned: (gem colour, label).</summary>
    public static void Hints(params (Color c, string label)[] items)
    {
        float x = Gfx.W - 30 * Gfx.S, y = Gfx.H - 46 * Gfx.S;
        for (int i = items.Length - 1; i >= 0; i--)
        {
            var (c, label) = items[i];
            var m = Raylib.MeasureTextEx(Gfx.Hand, label, 22 * Gfx.S, 0);
            float w = m.X + 58 * Gfx.S;
            x -= w;
            var r = new Rectangle(x, y - 18 * Gfx.S, w, 36 * Gfx.S);
            Raylib.DrawRectangleRounded(r, 1, 12, new Color(235, 228, 215, 230));
            Raylib.DrawRectangleRoundedLinesEx(r, 1, 12, 2 * Gfx.S, new Color(90, 85, 80, 255));
            var gc = new Vector2(x + 20 * Gfx.S, y);
            Raylib.DrawCircleV(gc, 12 * Gfx.S, Gfx.Bright(c, 0.6f));
            Raylib.DrawCircleGradient(gc - new Vector2(3, 3) * Gfx.S, 10 * Gfx.S, Gfx.Bright(c, 1.5f), c);
            Raylib.DrawTextEx(Gfx.Hand, label, new Vector2(x + 40 * Gfx.S, y - m.Y / 2), 22 * Gfx.S, 0, new Color(30, 25, 20, 255));
            x -= 14 * Gfx.S;
        }
    }

    public static readonly Color Blue = new(60, 140, 230, 255), RedGem = new(220, 60, 50, 255), YellowGem = new(235, 190, 40, 255), GreenGem = new(60, 180, 70, 255);
}
