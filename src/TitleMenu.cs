using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>
/// Title screen: illustrated collage background (assets/ui/title_bg.png) + hand-drawn ribbon menu
/// with simple animation (slow camera drift, flickering fire glow, twinkling stars, dust, logo shine,
/// swaying banners, line-boil selection box and bobbing arrow).
/// </summary>
public static class TitleMenu
{
    public record Item(string Label, bool Enabled);
    public static readonly Item[] Items =
    {
        new("INICIAR", true), new("MULTIPLAYER LOCAL", false), new("MULTIPLAYER ONLINE", true), new("CONFIGURAÇÕES", true), new("SAIR", true)
    };

    static Texture2D _bg; static bool _bgLoaded, _hasBg; static float _bgAspect = 16 / 9f;
    static Texture2D _paper;
    static readonly Random Rng = new(5);
    static readonly List<(Vector2 p, Vector2 v, float r, float ph)> Dust = new();
    static double _toastAt = -10; static string _toast;

    static void Load()
    {
        if (_bgLoaded) return; _bgLoaded = true;
        var dir = Path.Combine(AppContext.BaseDirectory, "assets", "ui");
        foreach (var name in new[] { "title_bg.png", "title_bg.jpg", "title_bg_preview.png" })
        {
            var p = Path.Combine(dir, name);
            if (!File.Exists(p)) continue;
            _bg = Raylib.LoadTexture(p);
            Raylib.GenTextureMipmaps(ref _bg);
            Raylib.SetTextureFilter(_bg, TextureFilter.Trilinear);
            _bgAspect = _bg.Width / (float)_bg.Height;
            _hasBg = true;
            break;
        }
        // fallback parchment (used behind everything if the art is missing)
        _paper = Tex.Make(512, 288, (x, y) =>
        {
            float n = Tex.Fbm(x / 60f, y / 60f, 4, 5), s = Tex.Fbm(x / 14f, y / 14f, 9, 3);
            float ex = MathF.Min(x, 511 - x) / 512f, ey = MathF.Min(y, 287 - y) / 288f;
            float burn = Math.Clamp((MathF.Min(ex, ey) - 0.02f + (n - 0.5f) * 0.05f) * 14, 0, 1);
            float b = (0.78f + n * 0.18f - s * 0.06f) * (0.35f + 0.65f * burn);
            return Tex.C(b * 1.0f, b * 0.88f, b * 0.66f);
        });
        for (int i = 0; i < 60; i++)
            Dust.Add((new Vector2((float)Rng.NextDouble(), (float)Rng.NextDouble()), new Vector2((float)(Rng.NextDouble() - 0.5) * 0.01f, -0.004f - (float)Rng.NextDouble() * 0.01f), 1 + (float)Rng.NextDouble() * 2.5f, (float)Rng.NextDouble() * 6));
    }

    /// <summary>Maps normalized coordinates of the background art to the screen (with the drift).</summary>
    static Rectangle _art;
    static Vector2 ArtPt(float u, float v) => new(_art.X + u * _art.Width, _art.Y + v * _art.Height);

    /// <summary>Draws the screen and handles input. Returns the chosen item index (-1 if none).</summary>
    public static int Update(ref int sel, double t, float dt)
    {
        Load();
        int W = Gfx.W, H = Gfx.H;
        // ---- background with slow drift ("Ken Burns")
        float cover = MathF.Max(W / (_bgAspect * H), 1f);
        float zoom = 1.035f + 0.015f * MathF.Sin((float)t * 0.11f);
        float aw = H * _bgAspect * cover * zoom, ah = H * cover * zoom;
        float px = MathF.Sin((float)t * 0.07f) * 0.012f * W, py = MathF.Cos((float)t * 0.09f) * 0.01f * H;
        _art = new Rectangle((W - aw) / 2 + px, (H - ah) / 2 + py, aw, ah);
        Raylib.ClearBackground(new Color(12, 9, 7, 255));
        if (_hasBg)
            Raylib.DrawTexturePro(_bg, new Rectangle(0, 0, _bg.Width, _bg.Height), _art, Vector2.Zero, 0, Color.White);
        else
            Raylib.DrawTexturePro(_paper, new Rectangle(0, 0, _paper.Width, _paper.Height), _art, Vector2.Zero, 0, Color.White);

        // ---- living details over the art
        Raylib.BeginBlendMode(BlendMode.Additive);
        // fire glow in the stage picture (top-left frame)
        float fl = 0.55f + 0.25f * MathF.Sin((float)t * 13) * MathF.Sin((float)t * 7.3f + 1) + 0.2f * MathF.Sin((float)t * 23);
        var fire = ArtPt(0.135f, 0.24f);
        Raylib.DrawCircleGradient(fire, 70 * Gfx.S * (0.9f + fl * 0.2f), new Color(255, 140, 40, (int)(70 * fl)), new Color(255, 80, 0, 0));
        // logo shine sweep every few seconds
        double ph = t % 4.5;
        if (ph < 1.1)
        {
            var a = ArtPt(0.05f, 0.35f); var b = ArtPt(0.51f, 0.6f);
            float sx = a.X + (b.X - a.X + 200 * Gfx.S) * (float)(ph / 1.1) - 100 * Gfx.S;
            Raylib.BeginScissorMode((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y));
            for (int i = 0; i < 3; i++)
                Raylib.DrawRectangleGradientH((int)(sx + i * 18 * Gfx.S), (int)a.Y, (int)(40 * Gfx.S), (int)(b.Y - a.Y), new Color(255, 255, 255, 0), new Color(255, 250, 220, 55 - i * 15));
            Raylib.EndScissorMode();
        }
        // dust motes floating in the light
        for (int i = 0; i < Dust.Count; i++)
        {
            var d = Dust[i];
            var p = d.p + d.v * (float)t;
            p.X = ((p.X % 1) + 1) % 1; p.Y = ((p.Y % 1) + 1) % 1;
            float a = 0.35f + 0.35f * MathF.Sin((float)t * 1.3f + d.ph);
            Raylib.DrawCircleV(new Vector2(p.X * W, p.Y * H), d.r * Gfx.S, new Color(255, 240, 200, (int)(70 * a)));
        }
        Raylib.EndBlendMode();
        // twinkling hand-drawn stars
        (float u, float v, Color c, float s)[] stars =
        {
            (0.265f, 0.085f, new Color(40, 170, 170, 255), 13), (0.275f, 0.135f, new Color(40, 170, 170, 255), 15), (0.27f, 0.21f, new Color(40, 170, 170, 255), 11),
            (0.04f, 0.44f, new Color(40, 170, 170, 255), 12), (0.525f, 0.49f, new Color(190, 30, 30, 255), 10), (0.755f, 0.8f, new Color(190, 30, 30, 255), 11),
            (0.97f, 0.62f, new Color(190, 30, 30, 255), 9),
        };
        for (int i = 0; i < stars.Length; i++)
        {
            var st = stars[i];
            float k = 0.75f + 0.35f * MathF.Sin((float)t * 2.2f + i * 1.7f);
            Gfx.StarShape(ArtPt(st.u, st.v), st.s * Gfx.S * k, Gfx.A(st.c, 0.85f));
        }

        // ---- ribbon menu on the right
        int n = Items.Length;
        float mx = W - 180 * Gfx.S, my0 = H * 0.19f, step = H * 0.13f;
        for (int i = 0; i < n; i++)
        {
            var it = Items[i];
            bool on = i == sel;
            float sway = MathF.Sin((float)t * 1.4f + i * 1.1f) * 1.2f;
            var c = new Vector2(mx + MathF.Sin((float)t * 0.9f + i) * 3 * Gfx.S, my0 + i * step);
            DrawBanner(c, 262 * Gfx.S * (on ? 1.05f : 1f), 54 * Gfx.S, sway + (i % 2 == 0 ? -1.5f : 1.5f), it.Label, it.Enabled, on, t);
        }
        // selection: hand-drawn red box with "line boil" + arrow
        {
            var c = new Vector2(mx, my0 + sel * step);
            int seed = (int)(t * 8);
            var r = new Random(seed);
            float hw = 150 * Gfx.S, hh = 37 * Gfx.S;
            Vector2 J() => new((float)(r.NextDouble() - 0.5) * 7 * Gfx.S, (float)(r.NextDouble() - 0.5) * 7 * Gfx.S);
            var red = new Color(200, 22, 22, 235);
            for (int pass = 0; pass < 2; pass++)
            {
                var p0 = c + new Vector2(-hw, -hh) + J(); var p1 = c + new Vector2(hw, -hh) + J(); var p2 = c + new Vector2(hw, hh) + J(); var p3 = c + new Vector2(-hw, hh) + J();
                Raylib.DrawLineEx(p0, p1 + new Vector2(8 * Gfx.S, 0), 3.2f * Gfx.S, red); Raylib.DrawLineEx(p1, p2, 3.2f * Gfx.S, red);
                Raylib.DrawLineEx(p2, p3 - new Vector2(10 * Gfx.S, 0), 3.2f * Gfx.S, red); Raylib.DrawLineEx(p3, p0, 3.2f * Gfx.S, red);
            }
            float bob = MathF.Sin((float)t * 5) * 8 * Gfx.S;
            var tip = c + new Vector2(-hw - 18 * Gfx.S + bob, 0);
            var tail = tip + new Vector2(-60 * Gfx.S, -50 * Gfx.S);
            var mid = (tip + tail) / 2 + new Vector2(10 * Gfx.S, -18 * Gfx.S);
            Raylib.DrawSplineSegmentBezierQuadratic(tail, mid, tip, 4 * Gfx.S, red);
            var dir = Vector2.Normalize(tip - mid); var side = new Vector2(-dir.Y, dir.X);
            DrawTri(tip + dir * 6 * Gfx.S, tip - dir * 18 * Gfx.S + side * 11 * Gfx.S, tip - dir * 18 * Gfx.S - side * 11 * Gfx.S, red);
        }

        // ---- footer: button hints
        Hint(new Vector2(W - 430 * Gfx.S, H - 50 * Gfx.S), new Color(60, 140, 230, 255), "Selecionar");
        Hint(new Vector2(W - 230 * Gfx.S, H - 50 * Gfx.S), new Color(220, 60, 50, 255), "Voltar");

        // toast for disabled items
        double td = t - _toastAt;
        if (td < 1.8)
        {
            float a = (float)Math.Clamp(Math.Min(td * 6, (1.8 - td) * 3), 0, 1);
            float shake = td < 0.3 ? MathF.Sin((float)td * 80) * 6 * Gfx.S : 0;
            var p = new Vector2(W - 180 * Gfx.S + shake, H * 0.84f);
            Raylib.DrawTextPro(Gfx.Hand, _toast, p, Raylib.MeasureTextEx(Gfx.Hand, _toast, 30 * Gfx.S, 0) / 2, -4, 30 * Gfx.S, 0, new Color(185, 20, 20, (int)(255 * a)));
        }

        // ---- input
        if (Input.Up) { sel = (sel + n - 1) % n; Audio.PlaySfx(Sfx.Click); }
        if (Input.Down) { sel = (sel + 1) % n; Audio.PlaySfx(Sfx.Click); }
        if (Input.Confirm)
        {
            if (Items[sel].Enabled) { Audio.PlaySfx(Sfx.Select); return sel; }
            Audio.PlaySfx(Sfx.Back); _toastAt = t; _toast = "Em breve!";
        }
        return -1;
    }

    static void DrawTri(Vector2 a, Vector2 b, Vector2 c, Color col) { Raylib.DrawTriangle(a, b, c, col); Raylib.DrawTriangle(a, c, b, col); }
    static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col) { DrawTri(a, b, c, col); DrawTri(a, c, d, col); }

    /// <summary>Parchment ribbon with folded, notched tails and hand-lettered text.</summary>
    static void DrawBanner(Vector2 c, float w, float h, float rotDeg, string text, bool enabled, bool selected, double t)
    {
        float rot = rotDeg * MathF.PI / 180;
        Vector2 R(float x, float y) { float cs = MathF.Cos(rot), sn = MathF.Sin(rot); return c + new Vector2(x * cs - y * sn, x * sn + y * cs); }
        var ink = new Color(25, 18, 12, enabled ? 255 : 170);
        var face = enabled ? new Color(236, 222, 188, 255) : new Color(150, 138, 116, 255);
        var back = enabled ? new Color(196, 176, 136, 255) : new Color(118, 106, 88, 255);
        float hw = w / 2, hh = h / 2, tail = h * 0.9f, drop = h * 0.28f;
        // tails (behind), with a V notch
        foreach (int s in new[] { -1, 1 })
        {
            var a = R(s * (hw - tail * 0.4f), -hh + drop); var b = R(s * (hw + tail * 0.75f), -hh + drop);
            var notch = R(s * (hw + tail * 0.45f), drop); var d = R(s * (hw + tail * 0.75f), hh + drop); var e = R(s * (hw - tail * 0.4f), hh + drop);
            Quad(a, b, notch, e, back); DrawTri(e, notch, d, back);
            Raylib.DrawLineEx(a, b, 2.4f * Gfx.S, ink); Raylib.DrawLineEx(b, notch, 2.4f * Gfx.S, ink); Raylib.DrawLineEx(notch, d, 2.4f * Gfx.S, ink); Raylib.DrawLineEx(d, e, 2.4f * Gfx.S, ink);
            // fold shadow
            DrawTri(R(s * hw, hh), R(s * (hw - tail * 0.4f), hh + drop), R(s * (hw - tail * 0.4f), hh), new Color(60, 45, 30, 200));
        }
        // wavy main body
        int seg = 14;
        var top = new Vector2[seg + 1]; var bot = new Vector2[seg + 1];
        for (int i = 0; i <= seg; i++)
        {
            float x = -hw + w * i / seg;
            float wave = MathF.Sin(i / (float)seg * MathF.PI * 2 + (float)t * 1.6f) * 3.2f * Gfx.S;
            top[i] = R(x, -hh + wave); bot[i] = R(x, hh + wave);
        }
        for (int i = 0; i < seg; i++) Quad(top[i], top[i + 1], bot[i + 1], bot[i], face);
        for (int i = 0; i < seg; i++)
        {
            Raylib.DrawLineEx(top[i], top[i + 1], 2.8f * Gfx.S, ink);
            Raylib.DrawLineEx(bot[i], bot[i + 1], 2.8f * Gfx.S, ink);
        }
        Raylib.DrawLineEx(top[0], bot[0], 2.8f * Gfx.S, ink); Raylib.DrawLineEx(top[seg], bot[seg], 2.8f * Gfx.S, ink);
        // inner hatching for an inked look
        for (int i = 1; i < seg; i += 3) Raylib.DrawLineEx(Vector2.Lerp(top[i], bot[i], 0.82f), Vector2.Lerp(top[i], bot[i], 0.95f), 1.2f * Gfx.S, Gfx.A(ink, 0.35f));
        // label
        float size = MathF.Min(h * 0.62f, w * 1.6f / MathF.Max(8, text.Length));
        var m = Raylib.MeasureTextEx(Gfx.Hand, text, size, 0);
        Raylib.DrawTextPro(Gfx.Hand, text, R(0, -h * 0.03f), m / 2, rotDeg, size, 0, enabled ? new Color(20, 14, 10, 255) : new Color(45, 38, 30, 200));
        if (!enabled)
        {
            // "EM BREVE" rubber stamp
            var sp = R(w * 0.32f, -h * 0.55f);
            float ss = 15 * Gfx.S;
            var sm = Raylib.MeasureTextEx(Gfx.Bold, "EM BREVE", ss, 1);
            Raylib.DrawRectanglePro(new Rectangle(sp.X, sp.Y, sm.X + 12 * Gfx.S, sm.Y + 6 * Gfx.S), (sm + new Vector2(12, 6) * Gfx.S) / 2, rotDeg - 8, new Color(170, 20, 20, 210));
            Raylib.DrawTextPro(Gfx.Bold, "EM BREVE", sp, sm / 2, rotDeg - 8, ss, 1, new Color(250, 235, 220, 255));
        }
    }

    static void Hint(Vector2 p, Color c, string label)
    {
        var m = Raylib.MeasureTextEx(Gfx.Hand, label, 24 * Gfx.S, 0);
        var r = new Rectangle(p.X - 8 * Gfx.S, p.Y - 20 * Gfx.S, m.X + 62 * Gfx.S, 40 * Gfx.S);
        Raylib.DrawRectangleRounded(r, 1, 12, new Color(235, 228, 215, 230));
        Raylib.DrawRectangleRoundedLinesEx(r, 1, 12, 2 * Gfx.S, new Color(90, 85, 80, 255));
        var gc = new Vector2(p.X + 16 * Gfx.S, p.Y);
        Raylib.DrawCircleV(gc, 13 * Gfx.S, Gfx.Bright(c, 0.6f));
        Raylib.DrawCircleGradient(gc - new Vector2(3, 3) * Gfx.S, 11 * Gfx.S, Gfx.Bright(c, 1.5f), c);
        Raylib.DrawTextEx(Gfx.Hand, label, new Vector2(p.X + 38 * Gfx.S, p.Y - m.Y / 2), 24 * Gfx.S, 0, new Color(30, 25, 20, 255));
    }
}
