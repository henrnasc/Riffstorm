using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

public sealed partial class Highway
{
    // ------------------------------------------------------------------ HUD
    public static void DrawHud(Player p, Rectangle view, double t, bool showMeters = true)
    {
        float s = Gfx.S * Math.Min(1, view.Width / Gfx.W * 1.6f);
        float cx = view.X + view.Width / 2;
        float baseY = view.Y + view.Height;
        float k = s / Gfx.S;  // text size factor

        // ================= score plaque + multiplier badge (left)
        float px = cx - 630 * s + 16 * s, py = baseY - 175 * s;
        float pw = 230 * s, ph = 150 * s;
        Gfx.Panel(px, py, pw, ph, 0.92f, new Color(55, 45, 40, 255));
        // odometer score
        string sc = p.Score.ToString().PadLeft(7, '0');
        float dw = 26 * s, dh = 40 * s, dx0 = px + pw / 2 - dw * 3.5f, dy0 = py + 18 * s;
        for (int i = 0; i < 7; i++)
        {
            var r = new Rectangle(dx0 + i * dw + 1.5f * s, dy0, dw - 3 * s, dh);
            Raylib.DrawRectangleRec(r, new Color(12, 12, 14, 255));
            Raylib.DrawRectangleGradientV((int)r.X, (int)r.Y, (int)r.Width, (int)(r.Height / 2), new Color(60, 60, 66, 255), new Color(20, 20, 22, 255));
            bool lead = i < 7 - Math.Max(1, p.Score.ToString().Length);
            Gfx.Text(Gfx.Title, sc[i].ToString(), r.X + r.Width / 2, r.Y + 2 * s, 34 * k, lead ? new Color(70, 70, 75, 255) : new Color(255, 245, 225, 255), 0.5f);
            Raylib.DrawRectangle((int)r.X, (int)(r.Y + r.Height / 2), (int)r.Width, 1, new Color(0, 0, 0, 200));
        }
        // streak bulbs
        int bm = p.BaseMultiplier;
        int lit = bm >= 4 ? 10 : (int)(p.Streak % p.StreakStep * 10 / p.StreakStep);
        for (int i = 0; i < 10; i++)
        {
            var c = new Vector2(px + 26 * s + i * 19.5f * s, py + 82 * s);
            bool on = i < lit;
            var bc = p.StarActive ? Gfx.StarBlue : new Color(255, 170, 30, 255);
            Raylib.DrawCircleV(c, 7.5f * s, new Color(20, 20, 20, 255));
            Raylib.DrawCircleV(c, 6 * s, on ? bc : new Color(60, 45, 30, 255));
            if (on) { Raylib.DrawCircleV(c - new Vector2(2 * s, 2 * s), 2 * s, new Color(255, 255, 230, 220)); Raylib.DrawCircleGradient(c, 14 * s, Gfx.A(bc, 0.35f), new Color(0, 0, 0, 0)); }
        }
        Gfx.Shadowed(Gfx.Bold, p.Streak > 0 ? $"SEQUÊNCIA {p.Streak}" : "", px + pw / 2, py + 100 * s, 16 * k, new Color(255, 225, 160, 255), 0.5f);
        // multiplier badge
        var bc2 = new Vector2(px + pw + 50 * s, py + ph / 2 + 4 * s);
        var mc = p.StarActive ? Gfx.StarBlue : bm switch { 1 => new Color(200, 200, 210, 255), 2 => new Color(255, 200, 40, 255), 3 => new Color(70, 230, 70, 255), _ => new Color(225, 90, 255, 255) };
        if (bm >= 4 || p.StarActive)
        {
            // flaming badge
            Raylib.BeginBlendMode(BlendMode.Additive);
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * MathF.Tau + (float)t * 2.5f;
                float fl = 0.75f + 0.25f * MathF.Sin((float)t * 13 + i * 2);
                var tip = bc2 + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 62 * s * fl;
                Raylib.DrawTriangle(bc2 + new Vector2(MathF.Cos(a + 0.35f), MathF.Sin(a + 0.35f)) * 40 * s, tip, bc2 + new Vector2(MathF.Cos(a - 0.35f), MathF.Sin(a - 0.35f)) * 40 * s, Gfx.A(p.StarActive ? Gfx.StarBlue : new Color(255, 120, 20, 255), 0.55f));
            }
            Raylib.EndBlendMode();
        }
        Raylib.DrawCircleV(bc2 + new Vector2(3 * s, 5 * s), 46 * s, new Color(0, 0, 0, 120));
        Raylib.DrawCircleV(bc2, 46 * s, new Color(200, 200, 210, 255));
        Raylib.DrawCircleV(bc2, 42 * s, new Color(90, 90, 100, 255));
        Raylib.DrawCircleV(bc2, 38 * s, Gfx.Bright(mc, 0.35f));
        Raylib.DrawCircleGradient(bc2 - new Vector2(0, 8 * s), 34 * s, Gfx.Bright(mc, 0.8f), Gfx.Bright(mc, 0.25f));
        Gfx.Fancy(Gfx.Title, p.Multiplier + "x", bc2.X, bc2.Y - 24 * s, 44 * k, Color.White, Gfx.Bright(mc, 1.2f), 0.5f, 2.5f);

        if (!showMeters) return;

        // ================= crowd meter: amp-style VU gauge with bulbs (right)
        var gc = new Vector2(cx + 505 * s, baseY - 58 * s);
        float R = 82 * s;
        Raylib.DrawCircleSector(gc + new Vector2(4 * s, 6 * s), R + 16 * s, 180, 360, 40, new Color(0, 0, 0, 120));
        Raylib.DrawCircleSector(gc, R + 16 * s, 180, 360, 40, new Color(30, 30, 34, 255));
        Raylib.DrawRing(gc, R + 9 * s, R + 15 * s, 180, 360, 40, new Color(200, 200, 210, 255));
        Raylib.DrawCircleSector(gc, R + 8 * s, 180, 360, 40, new Color(235, 225, 190, 255));            // cream dial face
        float m = Math.Clamp(p.Meter, 0, 1);
        Raylib.DrawRing(gc, R * 0.7f, R * 0.95f, 180, 240, 14, new Color(210, 30, 25, 255));
        Raylib.DrawRing(gc, R * 0.7f, R * 0.95f, 240, 300, 14, new Color(250, 200, 20, 255));
        Raylib.DrawRing(gc, R * 0.7f, R * 0.95f, 300, 360, 14, new Color(40, 190, 50, 255));
        for (int i = 0; i <= 12; i++)
        {
            float a = MathF.PI + i / 12f * MathF.PI;
            var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
            Raylib.DrawLineEx(gc + d * R * 0.6f, gc + d * R * (i % 3 == 0 ? 0.7f : 0.66f), 2 * s, new Color(40, 30, 20, 255));
        }
        Gfx.Text(Gfx.Title, "PÚBLICO", gc.X, gc.Y - R * 0.48f, 15 * k, new Color(60, 40, 30, 255), 0.5f);
        // zone bulbs on top
        int zone = m < 0.333f ? 0 : m < 0.666f ? 1 : 2;
        Color[] zc = { new(255, 50, 30, 255), new(255, 210, 30, 255), new(60, 240, 60, 255) };
        for (int i = 0; i < 3; i++)
        {
            float a = MathF.PI + (i + 0.5f) / 3f * MathF.PI;
            var bp = gc + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (R + 30 * s);
            bool on = i == zone && (zone != 0 || (t * 4 % 1) < 0.6);
            Raylib.DrawCircleV(bp, 10 * s, new Color(25, 25, 28, 255));
            Raylib.DrawCircleV(bp, 8 * s, on ? zc[i] : Gfx.Bright(zc[i], 0.25f));
            if (on) Raylib.DrawCircleGradient(bp, 26 * s, Gfx.A(zc[i], 0.5f), new Color(0, 0, 0, 0));
        }
        float ang = MathF.PI + m * MathF.PI;
        var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        Raylib.DrawLineEx(gc + new Vector2(2 * s, 2 * s), gc + new Vector2(2 * s, 2 * s) + dir * R * 0.92f, 5 * s, new Color(0, 0, 0, 90));
        Raylib.DrawLineEx(gc, gc + dir * R * 0.92f, 4 * s, new Color(180, 20, 15, 255));
        Raylib.DrawCircleV(gc, 11 * s, new Color(40, 40, 44, 255));
        Raylib.DrawCircleV(gc - new Vector2(2 * s, 2 * s), 5 * s, new Color(200, 200, 210, 255));

        // ================= star energy: glass tube with liquid
        float tx = cx + 410 * s, ty = baseY - 215 * s, tw = 190 * s, th = 24 * s;
        Raylib.DrawRectangleRounded(new Rectangle(tx - 4 * s, ty - 4 * s, tw + 8 * s, th + 8 * s), 0.7f, 10, new Color(190, 190, 200, 255));
        Raylib.DrawRectangleRounded(new Rectangle(tx, ty, tw, th), 0.7f, 10, new Color(8, 12, 22, 255));
        float e = Math.Clamp(p.Energy, 0, 1);
        if (e > 0)
        {
            var ec = p.StarActive ? new Color(170, 245, 255, 255) : e >= 0.5f ? new Color(70, 200, 255, 255) : new Color(40, 110, 210, 255);
            var fill = new Rectangle(tx + 3 * s, ty + 3 * s, (tw - 6 * s) * e, th - 6 * s);
            Raylib.DrawRectangleRounded(fill, 0.7f, 10, ec);
            Raylib.DrawRectangleGradientV((int)fill.X, (int)fill.Y, (int)fill.Width, (int)(fill.Height / 2), new Color(255, 255, 255, 110), new Color(255, 255, 255, 0));
            // bubbles
            for (int i = 0; i < 6; i++)
            {
                float bx = fill.X + (float)((t * 40 + i * 37) % Math.Max(1, fill.Width));
                Raylib.DrawCircleV(new Vector2(bx, fill.Y + fill.Height * (0.3f + 0.4f * MathF.Sin(i * 3 + (float)t * 4) * 0.5f + 0.2f)), 2 * s, new Color(255, 255, 255, 120));
            }
        }
        if (p.WhammyFilling && e > 0)
            Raylib.DrawRectangleRoundedLinesEx(new Rectangle(tx - 7 * s, ty - 7 * s, tw + 14 * s, th + 14 * s), 0.7f, 10, 2.5f * s, new Color(150, 230, 255, (int)(150 + 100 * Math.Sin(t * 20))));
        Raylib.DrawLineEx(new Vector2(tx + tw / 2, ty), new Vector2(tx + tw / 2, ty + th), 2 * s, new Color(255, 255, 255, 150));
        Raylib.DrawRectangleRounded(new Rectangle(tx + 6 * s, ty + 3 * s, tw - 12 * s, 4 * s), 1, 6, new Color(255, 255, 255, 50));
        string lbl = p.StarActive ? "MODO FÚRIA!" : e >= 0.5f ? "FÚRIA PRONTA!" : "ENERGIA";
        var lc = e >= 0.5f && !p.StarActive && (t * 3 % 1) < 0.5 ? Gfx.StarBlue : new Color(210, 225, 255, 255);
        Gfx.Fancy(Gfx.Title, lbl, tx + tw / 2, ty - 30 * s, 22 * k, Color.White, lc, 0.5f, 2);
    }
}
