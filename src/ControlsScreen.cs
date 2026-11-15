using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>
/// Settings > Controles: a drawn highway with a keyboard and a controller button under each lane, and below it the
/// list of the other actions. Pick a cell, confirm, then press the new key/button.
/// </summary>
public static partial class Game
{
    static int _cRow, _cCol;
    static int _capAct = -1; static bool _capPad; static double _capAt;
    static string _cNote; static double _cNoteAt = -9;

    const int CRows = 10;                       // 0 lane keys, 1 lane buttons, 2..8 other actions, 9 restore
    static int RowCols(int r) => r <= 1 ? 5 : r <= 8 ? 2 : 1;
    static (int act, bool pad) CellAt(int r, int c) => r <= 1 ? (c, r == 1) : r <= 8 ? (r + 3, c == 1) : (-1, false);

    static void ControlsScreen()
    {
        double t = _screenT;
        Binds.Ensure(Cfg);
        var kb = Cfg.KeyBinds; var pb = Cfg.PadBinds;

        // ---------------------------------------------------------- input
        if (_capAct >= 0) CaptureStep(kb, pb);
        else
        {
            if (Input.Up) { _cRow = (_cRow + CRows - 1) % CRows; Audio.PlaySfx(Sfx.Click); }
            if (Input.Down) { _cRow = (_cRow + 1) % CRows; Audio.PlaySfx(Sfx.Click); }
            if (Input.Left) { _cCol = Math.Max(0, Math.Min(_cCol, RowCols(_cRow) - 1) - 1); Audio.PlaySfx(Sfx.Click); }
            if (Input.Right) { _cCol = Math.Min(RowCols(_cRow) - 1, _cCol + 1); Audio.PlaySfx(Sfx.Click); }
            int col = Math.Min(_cCol, RowCols(_cRow) - 1);
            var (act, isPad) = CellAt(_cRow, col);
            if (Input.Confirm)
            {
                Audio.PlaySfx(Sfx.Select);
                if (act < 0)
                {
                    Cfg.KeyBinds = Binds.DefaultKeys(); Cfg.PadBinds = Binds.DefaultPad(Cfg.Controller); Cfg.Save();
                    Note("Botões padrão restaurados.");
                }
                else
                {
                    _capAct = act; _capPad = isPad; _capAt = Raylib.GetTime();
                    while (Raylib.GetKeyPressed() != 0) { }
                    if (isPad) Binds.BeginPadCapture(Input.Pad);
                }
            }
            else if (act >= 0 && Raylib.IsKeyPressed(KeyboardKey.Delete))
            {
                if (isPad) pb[act] = ""; else kb[act] = 0;
                Cfg.Save(); Audio.PlaySfx(Sfx.Back);
            }
            else if (Input.Back) { Audio.PlaySfx(Sfx.Back); Cfg.Save(); Go(Screen.Settings); return; }
        }

        // ---------------------------------------------------------- draw
        Paper.Backdrop(t);
        Paper.Ribbon(new Vector2(200 * S, 56 * S), 300 * S, 58 * S, -2.5f, "CONTROLES", true, false, t);
        Paper.Text(Input.Pad >= 0 ? "Controle conectado: " + Input.PadName : "Nenhum controle conectado (só teclado)",
            Gfx.W - 40 * S, 44 * S, 16, Paper.InkSoft, 1, 0, Gfx.Body);

        // the highway sketch
        float cx = Gfx.W / 2f, y0 = 92 * S, y1 = 282 * S, topW = 300 * S, botW = 640 * S;
        var tl = new Vector2(cx - topW / 2, y0); var tr = new Vector2(cx + topW / 2, y0);
        var bl = new Vector2(cx - botW / 2, y1); var br = new Vector2(cx + botW / 2, y1);
        Raylib.DrawTriangle(tl, bl, br, new Color(38, 32, 28, 235)); Raylib.DrawTriangle(tl, br, tr, new Color(38, 32, 28, 235));
        for (int i = 1; i < 5; i++)
        {
            float k = i / 5f;
            Raylib.DrawLineEx(Vector2.Lerp(tl, tr, k), Vector2.Lerp(bl, br, k), 1.5f * S, new Color(200, 190, 170, 120));
        }
        for (int b = 1; b <= 3; b++)
        {
            float k = (float)((b / 4.0 + t * 0.35) % 1.0);
            var a = Vector2.Lerp(tl, bl, k); var c = Vector2.Lerp(tr, br, k);
            Raylib.DrawLineEx(a, c, 1.2f * S, new Color(220, 210, 190, (int)(60 * k)));
        }
        Raylib.DrawLineEx(tl, bl, 3 * S, Paper.Ink); Raylib.DrawLineEx(tr, br, 3 * S, Paper.Ink);
        Raylib.DrawLineEx(tl, tr, 2 * S, Paper.Ink); Raylib.DrawLineEx(bl, br, 3 * S, Paper.Ink);
        float laneW = botW / 5;
        for (int l = 0; l < 5; l++)
        {
            var gc = new Vector2(bl.X + laneW * (l + 0.5f), y1 - 30 * S);
            bool down = Input.ActDown[l];
            Raylib.DrawCircleV(gc, 24 * S, Paper.Ink);
            Raylib.DrawCircleV(gc, 20 * S, down ? Gfx.Bright(Gfx.Fret[l], 1.25f) : Gfx.Lerp(Gfx.Fret[l], new Color(40, 34, 30, 255), 0.45f));
            if (down) Raylib.DrawCircleGradient(gc, 46 * S, Gfx.A(Gfx.Fret[l], 0.45f), new Color(0, 0, 0, 0));
        }

        // a keyboard and a controller cell under each lane
        Paper.Text("Teclado", bl.X - 16 * S, y1 + 16 * S, 18, Paper.InkSoft, 1);
        Paper.Text("Controle", bl.X - 16 * S, y1 + 54 * S, 18, Paper.InkSoft, 1);
        for (int l = 0; l < 5; l++)
        {
            float x = bl.X + laneW * l + 6 * S;
            Cell(new Rectangle(x, y1 + 12 * S, laneW - 12 * S, 32 * S), l, false, 0, l, t, Gfx.Fret[l]);
            Cell(new Rectangle(x, y1 + 50 * S, laneW - 12 * S, 32 * S), l, true, 1, l, t, Gfx.Fret[l]);
        }

        // the other actions
        var card = new Rectangle(cx - 400 * S, y1 + 102 * S, 800 * S, Gfx.H - (y1 + 102 * S) - 64 * S);
        Paper.Card(card, 0.3f, true, true);
        float rowH = (card.Height - 44 * S) / 8;
        float colK = card.X + 380 * S, colP = card.X + 590 * S, cw = 196 * S;
        Paper.Text("TECLADO", colK + cw / 2, card.Y + 10 * S, 15, Paper.Teal, 0.5f, 0, Gfx.Bold);
        Paper.Text("CONTROLE", colP + cw / 2, card.Y + 10 * S, 15, Paper.Teal, 0.5f, 0, Gfx.Bold);
        for (int i = 0; i < 7; i++)
        {
            int act = i + 5, row = i + 2;
            float y = card.Y + 34 * S + i * rowH;
            Paper.Text(Binds.Names[act], card.X + 40 * S, y + 3 * S, 19, Input.ActDown[act] ? Paper.Red : Paper.Ink);
            Cell(new Rectangle(colK, y, cw, rowH - 4 * S), act, false, row, 0, t, null);
            Cell(new Rectangle(colP, y, cw, rowH - 4 * S), act, true, row, 1, t, null);
        }
        {
            float y = card.Y + 34 * S + 7 * rowH;
            var c = new Vector2(card.X + card.Width / 2, y + rowH / 2);
            Paper.Text("Restaurar botões padrão", c.X, y + 3 * S, 19, _cRow == 9 ? Paper.Red : Paper.InkSoft, 0.5f);
            if (_cRow == 9 && _capAct < 0) Paper.RedBox(new Rectangle(c.X - 150 * S, y - 2 * S, 300 * S, rowH), t, 2.4f);
        }

        // prompt while waiting for the new key/button
        if (_capAct >= 0)
        {
            string what = _capPad ? "Aperte o botão no CONTROLE" : "Aperte a TECLA";
            if (_capPad && _capAct == (int)Act.Whammy) what = "Mexa a alavanca / analógico no CONTROLE";
            if (_capPad && Input.Pad < 0) what = "Nenhum controle conectado";
            var r = new Rectangle(cx - 330 * S, 150 * S, 660 * S, 96 * S);
            Paper.Card(r, -1, true, false, new Color(250, 238, 160, 255));
            Paper.Text($"{Binds.Names[_capAct]}: {what}", cx, r.Y + 20 * S, 24, Paper.Red, 0.5f, -1);
            Paper.Text("Esc cancela", cx, r.Y + 58 * S, 16, Paper.InkSoft, 0.5f, -1, Gfx.Body);
        }
        if (_cNote != null && Raylib.GetTime() - _cNoteAt < 3)
            Paper.Text(_cNote, cx, Gfx.H - 58 * S, 17, Paper.Teal, 0.5f, 0, Gfx.Bold);

        Paper.Hints((Paper.Blue, "Mudar botão"), (Paper.YellowGem, "Delete: apagar"), (Paper.RedGem, "Voltar"));
    }

    static void Note(string s) { _cNote = s; _cNoteAt = Raylib.GetTime(); }

    static void Cell(Rectangle r, int act, bool pad, int row, int col, double t, Color? tint)
    {
        bool sel = _cRow == row && Math.Min(_cCol, RowCols(row) - 1) == col;
        bool cap = _capAct == act && _capPad == pad;
        string txt = cap ? "..." : pad ? Binds.PadName(Cfg.PadBinds[act]) : Binds.KeyName(Cfg.KeyBinds[act]);
        Raylib.DrawRectangleRec(r, cap ? new Color(255, 236, 160, 255) : new Color(255, 251, 238, 240));
        if (tint is Color tc) Raylib.DrawRectangle((int)r.X, (int)r.Y, (int)(5 * S), (int)r.Height, tc);
        Raylib.DrawRectangleLinesEx(r, 1.5f * S, Paper.InkSoft);
        float size = 17;
        while (size > 11 && Raylib.MeasureTextEx(Gfx.Hand, txt, size * S, 0).X > r.Width - 12 * S) size -= 1;
        Paper.Text(txt, r.X + r.Width / 2, r.Y + (r.Height - size * S) / 2 - 1 * S, size, txt == "-" ? Gfx.A(Paper.InkSoft, 0.5f) : Paper.Ink, 0.5f);
        if (sel || cap) Paper.RedBox(new Rectangle(r.X - 3 * S, r.Y - 3 * S, r.Width + 6 * S, r.Height + 6 * S), t, 2.4f);
    }

    static void CaptureStep(int[] kb, string[] pb)
    {
        if (Raylib.GetTime() - _capAt < 0.15) return;
        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.GetTime() - _capAt > 10) { _capAct = -1; Audio.PlaySfx(Sfx.Back); return; }
        if (!_capPad)
        {
            int k;
            while ((k = Raylib.GetKeyPressed()) != 0)
            {
                if (k == (int)KeyboardKey.Escape) continue;
                for (int a = 0; a < Binds.Count; a++)
                    if (a != _capAct && kb[a] == k && SameGroup(a, _capAct)) { kb[a] = 0; Note($"\"{Binds.KeyName(k)}\" saiu de {Binds.Names[a]}."); }
                kb[_capAct] = k;
                Finish();
                return;
            }
        }
        else
        {
            string code = Binds.CapturePad(Input.Pad, _capAct == (int)Act.Whammy);
            if (code == null) return;
            for (int a = 0; a < Binds.Count; a++)
                if (a != _capAct && pb[a] == code && SameGroup(a, _capAct)) { pb[a] = ""; Note($"\"{Binds.PadName(code)}\" saiu de {Binds.Names[a]}."); }
            pb[_capAct] = code;
            Finish();
        }
    }

    // gameplay actions and list navigation may share a button (e.g. L1 is a fret and "previous game")
    static bool SameGroup(int a, int b) => (a >= (int)Act.PrevGame) == (b >= (int)Act.PrevGame);

    static void Finish()
    {
        _capAct = -1; Cfg.Save(); Audio.PlaySfx(Sfx.Select);
    }
}
