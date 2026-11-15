using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>Settings > Pista: pick Riffstorm's own board or an original highway read from one of your discs.</summary>
public static partial class Game
{
    static int _hwTab = -1, _hwSel;
    static List<Highways.Entry> _hwAll;

    static string HighwayLabel()
    {
        if (string.IsNullOrEmpty(Cfg.Highway)) return "Riffstorm";
        var p = Cfg.Highway.Split('/');
        var g = SongDb.Games.FirstOrDefault(x => x.Id == p[0]);
        return p.Length == 2 ? $"{(g != null ? TabName(g) : p[0])}: {Pretty(p[1])}" : Cfg.Highway;
    }

    static string Pretty(string n) => string.Join(' ', n.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    static void HighwayPickScreen()
    {
        double t = _screenT;
        _hwAll ??= Highways.All(Cfg.LibraryPath ?? SongDb.DefaultRoot);
        // tabs: "Riffstorm" + every game that has highways
        var games = SongDb.Games.Where(g => _hwAll.Any(e => e.Game == g.Id)).ToList();
        int tabs = games.Count + 1;
        if (_hwTab < 0)
        {
            // open on the one in use
            var cur = Cfg.Highway?.Split('/');
            _hwTab = cur is { Length: 2 } ? games.FindIndex(g => g.Id == cur[0]) + 1 : 0;
            if (_hwTab < 0) _hwTab = 0;
            var list0 = _hwTab == 0 ? null : _hwAll.Where(e => e.Game == games[_hwTab - 1].Id).ToList();
            _hwSel = list0 == null ? 0 : Math.Max(0, list0.FindIndex(e => e.Key == Cfg.Highway));
        }
        _hwTab = Math.Clamp(_hwTab, 0, tabs - 1);
        var list = _hwTab == 0 ? new List<Highways.Entry>() : _hwAll.Where(e => e.Game == games[_hwTab - 1].Id).ToList();
        int count = Math.Max(1, list.Count);
        _hwSel = Math.Clamp(_hwSel, 0, count - 1);

        // ---- input
        int pg = (Input.NextPage ? 1 : 0) - (Input.PrevPage ? 1 : 0);
        if (pg != 0) { _hwTab = (_hwTab + pg + tabs) % tabs; _hwSel = 0; Audio.PlaySfx(Sfx.Click); return; }
        const int Cols = 8;
        if (Input.Right) { _hwSel = Math.Min(count - 1, _hwSel + 1); Audio.PlaySfx(Sfx.Click); }
        if (Input.Left) { _hwSel = Math.Max(0, _hwSel - 1); Audio.PlaySfx(Sfx.Click); }
        if (Input.Down) { _hwSel = Math.Min(count - 1, _hwSel + Cols); Audio.PlaySfx(Sfx.Click); }
        if (Input.Up) { _hwSel = Math.Max(0, _hwSel - Cols); Audio.PlaySfx(Sfx.Click); }
        string chosen = _hwTab == 0 ? null : list.Count > 0 ? list[_hwSel].Key : null;

        // ---- draw
        Paper.Backdrop(t);
        Paper.Ribbon(new Vector2(170 * S, 56 * S), 250 * S, 58 * S, -2.5f, "PISTA", true, false, t);
        // tabs
        float x0 = 320 * S, x1 = Gfx.W - 40 * S, tw = (x1 - x0) / tabs;
        for (int i = 0; i < tabs; i++)
        {
            bool on = i == _hwTab;
            var c = new Vector2(x0 + tw * i + tw / 2, 54 * S + (on ? 0 : 4 * S));
            string name = i == 0 ? "RIFFSTORM" : TabName(games[i - 1]);
            Paper.Ribbon(c, Math.Min(tw * 0.86f, 210 * S) * (on ? 1.05f : 0.95f), (on ? 40 : 34) * S, i % 2 == 0 ? -2 : 2, name, on, false, t + i, on ? 1 : 0.9f);
            if (on) Paper.RedCircle(c, Math.Min(tw * 0.86f, 210 * S) * 0.58f, 30 * S, t);
        }

        var card = new Rectangle(40 * S, 104 * S, 800 * S, Gfx.H - 170 * S);
        Paper.Card(card, 0, true, false);
        if (_hwTab == 0)
        {
            Paper.Text("A pista própria do Riffstorm.", card.X + 40 * S, card.Y + 40 * S, 24, Paper.Ink);
            Paper.Text("As outras abas mostram as pistas originais lidas dos seus discos", card.X + 40 * S, card.Y + 84 * S, 17, Paper.InkSoft, 0, 0, Gfx.Body);
            Paper.Text("(elas são extraídas quando o disco é importado).", card.X + 40 * S, card.Y + 110 * S, 17, Paper.InkSoft, 0, 0, Gfx.Body);
            if (_hwAll.Count == 0) Paper.Text("Nenhuma pista original ainda: coloque um disco na pasta ISOs.", card.X + 40 * S, card.Y + 160 * S, 18, Paper.Red);
        }
        else
        {
            float cw = (card.Width - 60 * S) / Cols, ch = cw * 2 + 22 * S;
            int rows = (int)((card.Height - 30 * S) / ch);
            int firstRow = Math.Max(0, _hwSel / Cols - rows + 1);
            for (int i = firstRow * Cols; i < list.Count && i < (firstRow + rows) * Cols; i++)
            {
                int r = i / Cols - firstRow, c = i % Cols;
                var rect = new Rectangle(card.X + 30 * S + c * cw + 6 * S, card.Y + 18 * S + r * ch, cw - 12 * S, (cw - 12 * S) * 2);
                if (Highways.Thumb(list[i]) is Texture2D tx)
                    Raylib.DrawTexturePro(tx, new Rectangle(0, 0, tx.Width, tx.Height), rect, Vector2.Zero, 0, Color.White);
                Raylib.DrawRectangleLinesEx(rect, 1.5f * S, Paper.InkSoft);
                Paper.Text(Paper.Fit(Pretty(list[i].Name), cw - 4 * S, 13, Gfx.Body), rect.X + rect.Width / 2, rect.Y + rect.Height + 3 * S, 13, Paper.Ink, 0.5f, 0, Gfx.Body);
                if (list[i].Key == Cfg.Highway) Paper.Stamp("EM USO", new Vector2(rect.X + rect.Width / 2, rect.Y + 16 * S), 11, -6, Paper.Red);
                if (i == _hwSel) Paper.RedBox(new Rectangle(rect.X - 5 * S, rect.Y - 5 * S, rect.Width + 10 * S, rect.Height + 24 * S), t, 2.6f);
            }
        }

        // big scrolling preview on the right
        var pv = new Rectangle(card.X + card.Width + 40 * S, 110 * S, 0, Gfx.H - 236 * S);
        pv.Width = Math.Min(pv.Height / 2, Gfx.W - pv.X - 40 * S);
        pv.X += (Gfx.W - pv.X - 40 * S - pv.Width) / 2;
        Raylib.DrawRectangleRec(new Rectangle(pv.X - 6 * S, pv.Y - 6 * S, pv.Width + 12 * S, pv.Height + 12 * S), new Color(20, 16, 12, 230));
        var sel = _hwTab == 0 || list.Count == 0 ? null : list[_hwSel];
        if (sel != null && Highways.Thumb(sel) is Texture2D big)
        {
            float scroll = (float)(t * 0.12 % 1.0) * big.Height;
            Raylib.SetTextureWrap(big, TextureWrap.Repeat);
            Raylib.DrawTexturePro(big, new Rectangle(0, -scroll, big.Width, big.Height * (pv.Height / pv.Width / 2)), pv, Vector2.Zero, 0, Color.White);
        }
        else
        {
            Raylib.DrawRectangleGradientV((int)pv.X, (int)pv.Y, (int)pv.Width, (int)pv.Height, new Color(40, 38, 42, 255), new Color(14, 13, 15, 255));
            Gfx.Text(Gfx.Title, "RIFFSTORM", pv.X + pv.Width / 2, pv.Y + pv.Height / 2 - 14 * S, 22, new Color(200, 190, 170, 160), 0.5f);
        }
        Paper.Text(sel != null ? Pretty(sel.Name) : "Riffstorm", pv.X + pv.Width / 2, pv.Y + pv.Height + 14 * S, 20, Paper.Ink, 0.5f);

        Paper.Hints((Paper.Blue, "Usar esta"), (Paper.GreenGem, "L1 / R1 Jogo"), (Paper.RedGem, "Voltar"));

        if (Input.Confirm)
        {
            Cfg.Highway = chosen; Cfg.Save(); Audio.PlaySfx(Sfx.Select);
        }
        else if (Input.Back) { Audio.PlaySfx(Sfx.Back); _hwAll = null; Go(Screen.Settings); }
    }
}
