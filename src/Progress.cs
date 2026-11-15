using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

public static partial class Game
{
    /// <summary>Vertical bar on the right that fills up as the song goes on (single player and online).</summary>
    static void DrawProgress(double t, Player p)
    {
        double end = Math.Max(p.Chart.LastNoteTime + 1.5, Math.Min(Audio.Length, p.Chart.LastNoteTime + 12));
        if (end <= 0) return;
        float k = (float)Math.Clamp(t / end, 0, 1);
        float s = Gfx.S;
        float x = Gfx.W - 34 * s, top = (_online ? 100 : 74) * s, bottom = Gfx.H * 0.6f, w = 14 * s;
        var tube = new Rectangle(x - w / 2, top, w, bottom - top);
        // glass tube
        Raylib.DrawRectangleRounded(new Rectangle(tube.X - 3 * s, tube.Y - 3 * s, tube.Width + 6 * s, tube.Height + 6 * s), 1, 10, new Color(190, 190, 200, 200));
        Raylib.DrawRectangleRounded(tube, 1, 10, new Color(10, 12, 20, 230));
        // fill from the bottom up
        float fh = (tube.Height - 4 * s) * k;
        if (fh > 1)
        {
            var fill = new Rectangle(tube.X + 2 * s, tube.Y + tube.Height - 2 * s - fh, tube.Width - 4 * s, fh);
            var c1 = p.StarActive ? Gfx.StarBlue : new Color(255, 150, 30, 255);
            var c2 = p.StarActive ? new Color(200, 245, 255, 255) : new Color(255, 225, 110, 255);
            Raylib.DrawRectangleGradientV((int)fill.X, (int)fill.Y, (int)fill.Width, (int)fill.Height, c2, c1);
            Raylib.DrawCircleGradient(new Vector2(x, fill.Y), 12 * s, Gfx.A(c2, 0.6f), new Color(0, 0, 0, 0));
        }
        // quarter marks
        for (int i = 1; i < 4; i++)
        {
            float yy = tube.Y + tube.Height * (1 - i / 4f);
            Raylib.DrawLineEx(new Vector2(tube.X - 5 * s, yy), new Vector2(tube.X, yy), 2 * s, new Color(220, 220, 230, 160));
        }
        // time left
        double left = Math.Max(0, end - Math.Max(0, t));
        string txt = $"{(int)left / 60}:{(int)left % 60:00}";
        var m = Raylib.MeasureTextEx(Gfx.Bold, txt, 15 * s, 0);
        Gfx.Shadowed(Gfx.Bold, txt, x, top - 26 * s, 15, new Color(240, 235, 225, 230), 0.5f);
    }
}
