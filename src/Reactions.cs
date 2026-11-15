using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>
/// Online fun during the song: reactions (fire, laugh, skull, applause) that float over the sender's highway on
/// everyone's screen, and an automatic feed of what happens ("Bruno: 100 notas seguidas!", "Carla foi vaiada!",
/// "Diego assumiu a liderança!").
/// </summary>
public static partial class Game
{
    public static readonly string[] ReactionNames = { "Fogo", "Risada", "Caveira", "Palmas" };

    sealed class Floater { public int Id, Kind; public double At; public float Dx; }
    static readonly List<Floater> _floaters = new();
    static double _lastReact = -9;

    sealed class FeedItem { public string Text; public Color Col; public double At; }
    static readonly List<FeedItem> _feed = new();
    static int _leaderId = -1, _leaderCand = -1; static double _leaderSince;

    static void ResetOnlineFun() { _floaters.Clear(); _feed.Clear(); _leaderId = _leaderCand = -1; }

    static void Feed(string text, Color col)
    {
        _feed.Insert(0, new FeedItem { Text = text, Col = col, At = Raylib.GetTime() });
        if (Environment.GetEnvironmentVariable("RIFF_FEEDLOG") is string fl) try { File.AppendAllText(fl, $"{_t:0.0}s  {text}\n"); } catch { }
        if (_feed.Count > 6) _feed.RemoveAt(_feed.Count - 1);
    }

    static string NameOf(int id) => id == Net.MyId ? "Você" : Net.Players.FirstOrDefault(p => p.Id == id)?.Name ?? "?";

    // ------------------------------------------------------------------ reactions
    static bool _autoReact;
    static void ReactionInput()
    {
        if (_autoReact && Raylib.GetTime() - _lastReact > 1.3) { _lastReact = Raylib.GetTime(); int rk = Random.Shared.Next(4); Net.Send(new Msg { T = "react", Diff = rk }); AddReaction(Net.MyId, rk); }
        for (int k = 0; k < 4; k++)
        {
            if (!Input.Hit(Act.React1 + k) || Raylib.GetTime() - _lastReact < 0.35) continue;
            _lastReact = Raylib.GetTime();
            Net.Send(new Msg { T = "react", Diff = k });
            AddReaction(Net.MyId, k);
        }
    }

    static void AddReaction(int id, int kind)
    {
        _floaters.Add(new Floater { Id = id, Kind = Math.Clamp(kind, 0, 3), At = Raylib.GetTime(), Dx = (float)(Random.Shared.NextDouble() - 0.5) });
        if (_floaters.Count > 40) _floaters.RemoveAt(0);
    }

    /// <summary>Where a player's reactions rise from: over their highway (or yours, in the middle).</summary>
    static Vector2 ReactionOrigin(int id)
    {
        if (id == Net.MyId) return new Vector2(Gfx.W / 2f, Gfx.H * 0.42f);
        int slot = _ghosts.FindIndex(g => g.Id == id);
        if (slot < 0 || slot >= GhostSlots.Length) return new Vector2(Gfx.W / 2f, Gfx.H * 0.3f);
        var v = GhostView(slot);
        return new Vector2(v.X + v.Width / 2, v.Y + v.Height * 0.55f);
    }

    static void DrawReactions()
    {
        double now = Raylib.GetTime();
        _floaters.RemoveAll(f => now - f.At > 1.9);
        foreach (var f in _floaters)
        {
            float a = (float)((now - f.At) / 1.9);
            var o = ReactionOrigin(f.Id);
            float size = (f.Id == Net.MyId ? 46 : 34) * S * (a < 0.12f ? 0.6f + a / 0.12f * 0.55f : 1.15f - (a - 0.12f) * 0.2f);
            var c = o + new Vector2(f.Dx * 70 * S + MathF.Sin(a * 9 + f.Dx * 5) * 10 * S, -a * 150 * S);
            float alpha = a < 0.7f ? 1 : 1 - (a - 0.7f) / 0.3f;
            DrawReactionIcon(f.Kind, c, size, alpha);
            if (a < 0.6f)
            {
                string who = NameOf(f.Id);
                var m = Raylib.MeasureTextEx(Gfx.Bold, who, 13 * S, 0);
                Raylib.DrawTextEx(Gfx.Bold, who, new Vector2(c.X - m.X / 2, c.Y + size * 0.62f), 13 * S, 0, Gfx.A(Color.White, alpha * 0.85f));
            }
        }
    }

    /// <summary>Hand-made reaction icons (no emoji font needed).</summary>
    public static void DrawReactionIcon(int kind, Vector2 c, float size, float alpha)
    {
        float r = size / 2;
        Color A(Color col) => Gfx.A(col, alpha);
        Raylib.DrawCircleV(c + new Vector2(2, 3) * S, r * 1.05f, A(new Color(0, 0, 0, 90)));
        switch (kind)
        {
            case 0: // fire
            {
                var o = new Color(255, 120, 20, 255); var y = new Color(255, 215, 60, 255);
                for (int i = -2; i <= 2; i++)
                {
                    float h = r * (1.6f - Math.Abs(i) * 0.3f + 0.15f * MathF.Sin((float)Raylib.GetTime() * 14 + i));
                    var b = c + new Vector2(i * r * 0.32f, r * 0.7f);
                    Raylib.DrawTriangle(b + new Vector2(-r * 0.32f, 0), b + new Vector2(r * 0.32f, 0), b + new Vector2(0, -h), A(o));
                    Raylib.DrawTriangle(b + new Vector2(-r * 0.32f, 0), b + new Vector2(0, -h), b + new Vector2(r * 0.32f, 0), A(o));
                }
                Raylib.DrawCircleV(c + new Vector2(0, r * 0.35f), r * 0.55f, A(o));
                Raylib.DrawCircleV(c + new Vector2(0, r * 0.45f), r * 0.32f, A(y));
                break;
            }
            case 1: // laughing face
            {
                Raylib.DrawCircleV(c, r, A(new Color(255, 205, 50, 255)));
                Raylib.DrawRing(c, r * 0.92f, r, 0, 360, 32, A(new Color(200, 140, 20, 255)));
                var ink = A(new Color(70, 40, 20, 255));
                Raylib.DrawLineEx(c + new Vector2(-r * 0.5f, -r * 0.3f), c + new Vector2(-r * 0.2f, -r * 0.15f), 3 * S, ink);
                Raylib.DrawLineEx(c + new Vector2(-r * 0.5f, -r * 0.0f), c + new Vector2(-r * 0.2f, -r * 0.15f), 3 * S, ink);
                Raylib.DrawLineEx(c + new Vector2(r * 0.5f, -r * 0.3f), c + new Vector2(r * 0.2f, -r * 0.15f), 3 * S, ink);
                Raylib.DrawLineEx(c + new Vector2(r * 0.5f, -r * 0.0f), c + new Vector2(r * 0.2f, -r * 0.15f), 3 * S, ink);
                Raylib.DrawCircleSector(c + new Vector2(0, r * 0.15f), r * 0.55f, 0, 180, 20, ink);
                Raylib.DrawCircleSector(c + new Vector2(0, r * 0.15f), r * 0.4f, 20, 160, 20, A(new Color(230, 80, 80, 255)));
                Raylib.DrawCircleV(c + new Vector2(-r * 0.75f, -r * 0.05f), r * 0.14f, A(new Color(120, 200, 255, 255)));   // tears
                Raylib.DrawCircleV(c + new Vector2(r * 0.75f, -r * 0.05f), r * 0.14f, A(new Color(120, 200, 255, 255)));
                break;
            }
            case 2: // skull
            {
                var bone = A(new Color(240, 236, 225, 255)); var ink = A(new Color(30, 25, 25, 255));
                Raylib.DrawCircleV(c - new Vector2(0, r * 0.12f), r * 0.85f, bone);
                Raylib.DrawRectangleRec(new Rectangle(c.X - r * 0.45f, c.Y + r * 0.35f, r * 0.9f, r * 0.5f), bone);
                Raylib.DrawCircleV(c + new Vector2(-r * 0.33f, -r * 0.1f), r * 0.24f, ink);
                Raylib.DrawCircleV(c + new Vector2(r * 0.33f, -r * 0.1f), r * 0.24f, ink);
                Raylib.DrawTriangle(c + new Vector2(0, r * 0.12f), c + new Vector2(-r * 0.1f, r * 0.32f), c + new Vector2(r * 0.1f, r * 0.32f), ink);
                for (int i = -1; i <= 1; i++) Raylib.DrawLineEx(c + new Vector2(i * r * 0.2f, r * 0.5f), c + new Vector2(i * r * 0.2f, r * 0.85f), 2 * S, ink);
                break;
            }
            default: // applause: two hands + motion marks
            {
                var skin = A(new Color(250, 200, 140, 255)); var ink = A(new Color(120, 70, 30, 255));
                float sw = MathF.Sin((float)Raylib.GetTime() * 18) * r * 0.08f;
                Raylib.DrawEllipse((int)(c.X - r * 0.28f - sw), (int)(c.Y + r * 0.1f), r * 0.42f, r * 0.7f, skin);
                Raylib.DrawEllipse((int)(c.X + r * 0.28f + sw), (int)(c.Y + r * 0.1f), r * 0.42f, r * 0.7f, skin);
                Raylib.DrawEllipseLines((int)(c.X - r * 0.28f - sw), (int)(c.Y + r * 0.1f), r * 0.42f, r * 0.7f, ink);
                Raylib.DrawEllipseLines((int)(c.X + r * 0.28f + sw), (int)(c.Y + r * 0.1f), r * 0.42f, r * 0.7f, ink);
                var gold = A(new Color(255, 210, 60, 255));
                Raylib.DrawLineEx(c + new Vector2(-r * 0.95f, -r * 0.6f), c + new Vector2(-r * 0.7f, -r * 0.45f), 3 * S, gold);
                Raylib.DrawLineEx(c + new Vector2(0, -r * 1.0f), c + new Vector2(0, -r * 0.75f), 3 * S, gold);
                Raylib.DrawLineEx(c + new Vector2(r * 0.95f, -r * 0.6f), c + new Vector2(r * 0.7f, -r * 0.45f), 3 * S, gold);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ automatic announcements
    static readonly int[] StreakCalls = { 50, 100, 200, 300, 400, 500, 750, 1000 };

    /// <summary>Called with a player's previous and new live state (from their "st" messages).</summary>
    static void AnnounceChanges(int id, int oldStreak, int newStreak, bool oldStar, bool newStar, bool oldFailed, bool newFailed)
    {
        var col = SlotColor(id);
        string n = NameOf(id);
        foreach (int m in StreakCalls)
            if (oldStreak < m && newStreak >= m) Feed($"{n}: {m} notas seguidas!", col);
        if (!oldStar && newStar) Feed($"{n} ativou o Modo Fúria!", new Color(120, 200, 255, 255));
        if (!oldFailed && newFailed) Feed($"{n} foi vaiado(a)!", new Color(255, 90, 80, 255));
    }

    /// <summary>Watches who is first; announces a new leader once they hold it for a moment.</summary>
    static void LeaderWatch(double t)
    {
        if (t < 4 || Net.Players.Count < 2) return;
        var best = Net.Players.Select(p => (p.Id, s: LiveScore(p))).OrderByDescending(x => x.s).ToList();
        if (best[0].s <= 0 || best.Count > 1 && best[0].s == best[1].s) return;
        int lead = best[0].Id;
        if (lead != _leaderCand) { _leaderCand = lead; _leaderSince = Raylib.GetTime(); return; }
        if (lead != _leaderId && Raylib.GetTime() - _leaderSince > 1.0)
        {
            bool first = _leaderId == -1;
            _leaderId = lead;
            if (!first) Feed(lead == Net.MyId ? "Você assumiu a liderança!" : $"{NameOf(lead)} assumiu a liderança!", new Color(255, 200, 60, 255));
        }
    }

    static void DrawFeed()
    {
        double now = Raylib.GetTime();
        _feed.RemoveAll(f => now - f.At > 6);
        float y = 86 * S;
        foreach (var f in _feed.Take(4))
        {
            float a = (float)Math.Clamp(Math.Min((now - f.At) * 6, (6 - (now - f.At)) * 2), 0, 1);
            var m = Raylib.MeasureTextEx(Gfx.Bold, f.Text, 17 * S, 0);
            var r = new Rectangle(16 * S - (1 - a) * 40 * S, y, m.X + 34 * S, m.Y + 10 * S);
            Raylib.DrawRectangleRounded(r, 0.4f, 8, Gfx.A(new Color(15, 12, 10, 200), a));
            Raylib.DrawCircleV(new Vector2(r.X + 13 * S, r.Y + r.Height / 2), 5 * S, Gfx.A(f.Col, a));
            Raylib.DrawTextEx(Gfx.Bold, f.Text, new Vector2(r.X + 24 * S, r.Y + 5 * S), 17 * S, 0, Gfx.A(new Color(245, 238, 225, 255), a));
            y += r.Height + 6 * S;
        }
    }
}
