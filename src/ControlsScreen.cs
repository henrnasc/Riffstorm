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

    const int ListN = Binds.Count - 5;          // actions below the highway
    const int RestoreRow = ListN + 2;
    const int CRows = ListN + 3;                // 0 lane keys, 1 lane buttons, 2.. other actions, last: restore
    static int RowCols(int r) => r <= 1 ? 5 : r < RestoreRow ? 2 : 1;
    static (int act, bool pad) CellAt(int r, int c) => r <= 1 ? (c, r == 1) : r < RestoreRow ? (r + 3, c == 1) : (-1, false);

    static void ControlsScreen()
    {
        double t = _screenT;
        Binds.Ensure(Cfg);
        var kb = Cfg.KeyBinds; var pb = Cfg.PadBinds;

        // ---------------------------------------------------------- input
        if (Environment.GetEnvironmentVariable("RIFF_KEYLOG") is string kl)
            foreach (var key in AllKeys) if (Raylib.IsKeyPressed(key)) File.AppendAllText(kl, $"{Raylib.GetTime():0.00} tecla {key} conf={Input.Confirm} cap={_capAct} row={_cRow} col={_cCol}\n");
        if (_conf is { } cf) ConflictStep(kb, pb, cf);
        else if (_restoreAsk) RestoreStep();
        else if (_capAct >= 0) CaptureStep(kb, pb);
        else
        {
            if (Input.Up) { _cRow = (_cRow + CRows - 1) % CRows; Audio.PlaySfx(Sfx.Click); }
            if (Input.Down) { _cRow = (_cRow + 1) % CRows; Audio.PlaySfx(Sfx.Click); }
            if (Input.Left) { _cCol = Math.Max(0, Math.Min(_cCol, RowCols(_cRow) - 1) - 1); Audio.PlaySfx(Sfx.Click); }
            if (Input.Right) { _cCol = Math.Min(RowCols(_cRow) - 1, _cCol + 1); Audio.PlaySfx(Sfx.Click); }
            // mouse: hovering selects a cell, clicking activates it
            bool click = false;
            var mp = Raylib.GetMousePosition();
            foreach (var (r, c, rect) in _cellRects)
                if (Raylib.CheckCollisionPointRec(mp, rect))
                {
                    if (Raylib.GetMouseDelta() != Vector2.Zero || Raylib.IsMouseButtonPressed(MouseButton.Left)) { _cRow = r; _cCol = c; }
                    if (Raylib.IsMouseButtonPressed(MouseButton.Left)) click = true;
                }
            int col = Math.Min(_cCol, RowCols(_cRow) - 1);
            var (act, isPad) = CellAt(_cRow, col);
            bool restoreKey = Raylib.IsKeyPressed(KeyboardKey.F2);
            if (restoreKey) act = -1;
            if (Input.Confirm || click || restoreKey)
            {
                Audio.PlaySfx(Sfx.Select);
                if (act < 0) _restoreAsk = true;
                else
                {
                    _capAct = act; _capPad = isPad; _capAt = Raylib.GetTime();
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
        _cellRects.Clear();
        // ---------------------------------------------------------- draw
        Paper.Backdrop(t);
        Paper.Ribbon(new Vector2(200 * S, 56 * S), 300 * S, 58 * S, -2.5f, "CONTROLES", true, false, t);
        Paper.Text(Input.Pad >= 0 ? "Controle conectado: " + Input.PadName : "Nenhum controle conectado (só teclado)",
            Gfx.W - 40 * S, 44 * S, 16, Paper.InkSoft, 1, 0, Gfx.Body);

        // the highway sketch
        float cx = Gfx.W / 2f, y0 = 84 * S, y1 = 232 * S, topW = 300 * S, botW = 640 * S;
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
            var gc = new Vector2(bl.X + laneW * (l + 0.5f), y1 - 26 * S);
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
        var card = new Rectangle(cx - 400 * S, y1 + 94 * S, 800 * S, Gfx.H - (y1 + 94 * S) - 62 * S);
        Paper.Card(card, 0.3f, true, true);
        float rowH = (card.Height - 40 * S) / (ListN + 1);
        float colK = card.X + 380 * S, colP = card.X + 590 * S, cw = 196 * S;
        Paper.Text("TECLADO", colK + cw / 2, card.Y + 10 * S, 15, Paper.Teal, 0.5f, 0, Gfx.Bold);
        Paper.Text("CONTROLE", colP + cw / 2, card.Y + 10 * S, 15, Paper.Teal, 0.5f, 0, Gfx.Bold);
        for (int i = 0; i < ListN; i++)
        {
            int act = i + 5, row = i + 2;
            float y = card.Y + 30 * S + i * rowH;
            Paper.Text(Binds.Names[act], card.X + 40 * S, y + 1 * S, 16, Input.ActDown[act] ? Paper.Red : Paper.Ink);
            Cell(new Rectangle(colK, y, cw, rowH - 4 * S), act, false, row, 0, t, null);
            Cell(new Rectangle(colP, y, cw, rowH - 4 * S), act, true, row, 1, t, null);
        }
        {
            float y = card.Y + 30 * S + ListN * rowH;
            var c = new Vector2(card.X + card.Width / 2, y + rowH / 2 + 2 * S);
            bool on = _cRow == RestoreRow;
            Paper.Ribbon(c, 290 * S, rowH + 4 * S, -1, "RESTAURAR PADRÃO (F2)", true, on, t, 0.9f);
            _cellRects.Add((RestoreRow, 0, new Rectangle(c.X - 170 * S, c.Y - rowH / 2 - 4 * S, 340 * S, rowH + 8 * S)));
            if (on && _capAct < 0) Paper.RedBox(new Rectangle(c.X - 180 * S, c.Y - rowH / 2 - 6 * S, 360 * S, rowH + 12 * S), t, 2.4f);
        }

        // dialogs: waiting for the new key/button, key already in use, restore defaults
        if (_capAct >= 0)
        {
            string what = _capPad ? "Aperte o botão no CONTROLE" : "Aperte a TECLA";
            if (_capPad && _capAct == (int)Act.Whammy) what = "Mexa a alavanca / analógico no CONTROLE";
            if (_capPad && Input.Pad < 0) what = "Nenhum controle conectado";
            Dialog($"{Binds.Names[_capAct]}: {what}", "Esc cancela", t);
        }
        else if (_conf is { } cd)
        {
            string name = cd.pad ? Binds.PadName(cd.code) : Binds.KeyName(cd.key);
            Dialog($"\"{name}\" já é usado em \"{Binds.Names[cd.other]}\".",
                   $"Enter = usar em \"{Binds.Names[cd.act]}\" mesmo assim (\"{Binds.Names[cd.other]}\" fica sem)      Esc = cancelar", t);
        }
        else if (_restoreAsk)
            Dialog("Voltar todos os botões para o padrão?", "Enter = sim      Esc = não", t);

        if (_cNote != null && Raylib.GetTime() - _cNoteAt < 3)
            Paper.Text(_cNote, cx, card.Y - 22 * S, 17, Paper.Red, 0.5f, 0, Gfx.Bold);

        Paper.Hints((Paper.Blue, "Mudar (Enter / clique)"), (Paper.YellowGem, "Delete: apagar"), (Paper.RedGem, "Voltar"));
    }

    static readonly List<(int row, int col, Rectangle r)> _cellRects = new();
    static (int act, bool pad, int key, string code, int other)? _conf;
    static bool _restoreAsk;
    static double _dlgAt;

    static readonly KeyboardKey[] AllKeys = Enum.GetValues<KeyboardKey>()
        .Where(k => k is not (KeyboardKey.Null or KeyboardKey.Escape or KeyboardKey.Back or KeyboardKey.Menu or KeyboardKey.VolumeUp or KeyboardKey.VolumeDown)).ToArray();

    static void Dialog(string line1, string line2, double t)
    {
        float cx = Gfx.W / 2f;
        float w = Math.Max(Raylib.MeasureTextEx(Gfx.Hand, line1, 24 * S, 0).X, Raylib.MeasureTextEx(Gfx.Body, line2, 16 * S, 0).X) + 80 * S;
        var r = new Rectangle(cx - w / 2, 140 * S, w, 104 * S);
        Raylib.DrawRectangle(0, 0, Gfx.W, Gfx.H, new Color(0, 0, 0, 90));
        Paper.Card(r, -1, true, false, new Color(250, 238, 160, 255));
        Paper.Text(line1, cx, r.Y + 22 * S, 24, Paper.Red, 0.5f, -1);
        Paper.Text(line2, cx, r.Y + 64 * S, 16, Paper.Ink, 0.5f, -1, Gfx.Body);
    }

    static void Note(string s) { _cNote = s; _cNoteAt = Raylib.GetTime(); }

    static void Cell(Rectangle r, int act, bool pad, int row, int col, double t, Color? tint)
    {
        _cellRects.Add((row, col, r));
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
        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.GetTime() - _capAt > 15) { _capAct = -1; Audio.PlaySfx(Sfx.Back); return; }
        if (!_capPad)
        {
            // scan every key (the per-frame key queue is already read by the input system)
            foreach (var key in AllKeys)
            {
                if (!Raylib.IsKeyPressed(key)) continue;
                Assign(kb, pb, false, (int)key, null);
                return;
            }
        }
        else
        {
            string code = Binds.CapturePad(Input.Pad, _capAct == (int)Act.Whammy);
            if (code != null) Assign(kb, pb, true, 0, code);
        }
    }

    /// <summary>Sets the new binding, or asks first when another action already uses it.</summary>
    static void Assign(int[] kb, string[] pb, bool pad, int key, string code)
    {
        int act = _capAct; _capAct = -1;
        for (int a = 0; a < Binds.Count; a++)
        {
            if (a == act) continue;
            if (pad ? pb[a] == code : kb[a] == key)
            {
                _conf = (act, pad, key, code, a); _dlgAt = Raylib.GetTime();
                Audio.PlaySfx(Sfx.Click);
                return;
            }
        }
        if (pad) pb[act] = code; else kb[act] = key;
        Cfg.Save(); Audio.PlaySfx(Sfx.Select);
    }

    static void ConflictStep(int[] kb, string[] pb, (int act, bool pad, int key, string code, int other) c)
    {
        if (Raylib.GetTime() - _dlgAt < 0.15) return;
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || Raylib.IsMouseButtonPressed(MouseButton.Left)
            || (Input.Pad >= 0 && Raylib.IsGamepadButtonPressed(Input.Pad, GamepadButton.RightFaceDown)))
        {
            if (c.pad) { pb[c.other] = ""; pb[c.act] = c.code; } else { kb[c.other] = 0; kb[c.act] = c.key; }
            Cfg.Save(); Audio.PlaySfx(Sfx.Select);
            Note($"\"{Binds.Names[c.other]}\" ficou sem {(c.pad ? "botão" : "tecla")}.");
            _conf = null;
        }
        else if (Input.Cancel || Raylib.IsMouseButtonPressed(MouseButton.Right)) { _conf = null; Audio.PlaySfx(Sfx.Back); }
    }

    static void RestoreStep()
    {
        if (Input.Confirm || Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Cfg.KeyBinds = Binds.DefaultKeys(); Cfg.PadBinds = Binds.DefaultPad(Cfg.Controller); Cfg.Save();
            Audio.PlaySfx(Sfx.Select); Note("Botões padrão restaurados.");
            _restoreAsk = false;
        }
        else if (Input.Cancel || Raylib.IsMouseButtonPressed(MouseButton.Right)) { _restoreAsk = false; Audio.PlaySfx(Sfx.Back); }
    }
}