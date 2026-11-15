using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>
/// Online duel (points): menu (name + create/join room), room lobby, the other players' highways in the background
/// during the song, the scoreboard on top and the final ranking.
/// </summary>
public static partial class Game
{
    static bool _online;                       // the current song is an online match
    static double _goAt = -1;                  // local clock time the song starts (count-in begins)
    static bool _sentLoaded;
    static int _sentNext; static double _nextState;
    static bool _quitAsk;

    // automation for multi-instance tests
    static bool _autoHost, _autoReady, _noIso; static string _autoJoin, _autoPick; static int _autoStart; static double _autoStartedAt = -9;

    public static readonly Color[] SlotColors =
    {
        new(60, 190, 70, 255), new(225, 55, 45, 255), new(240, 200, 40, 255), new(60, 140, 235, 255), new(245, 140, 30, 255), new(170, 80, 230, 255),
    };

    static Color SlotColor(int id)
    {
        int i = Net.Players.FindIndex(p => p.Id == id);
        return SlotColors[(i < 0 ? id : i) % SlotColors.Length];
    }

    // ================================================================= ghosts (the other players' highways)
    sealed class Ghost
    {
        public int Id; public string Name; public Player P; public Highway Hw;
        public RenderTexture2D Rt; public int RtW, RtH; public bool Done;
    }
    static readonly List<Ghost> _ghosts = new();

    static void ClearGhosts()
    {
        foreach (var g in _ghosts) if (g.RtW > 0) Raylib.UnloadRenderTexture(g.Rt);
        _ghosts.Clear();
    }

    static void CreateGhosts()
    {
        ClearGhosts();
        byte[] mid = File.ReadAllBytes(_song.ChartPath);
        foreach (var np in Net.Players)
        {
            if (np.Id == Net.MyId) continue;
            var chart = Chart.Parse(mid);                       // own copy: notes carry per-player state
            int d = Math.Clamp(np.Diff, 0, 3);
            if (chart.Notes[d].Count == 0) d = Enumerable.Range(0, 4).Reverse().FirstOrDefault(i => chart.Notes[i].Count > 0);
            _ghosts.Add(new Ghost
            {
                Id = np.Id, Name = np.Name, P = new Player(chart, d),
                Hw = new Highway { Ghost = true, Speed = 7.5f * Cfg.NoteSpeed * (d >= 2 ? 1.15f : 1f), Lefty = false },
            });
        }
    }

    static void ApplyGhost(Msg m)
    {
        var g = _ghosts.FirstOrDefault(x => x.Id == m.Id);
        if (g == null) return;
        var p = g.P;
        p.Score = m.Score; p.Streak = m.Streak; p.Energy = m.Energy; p.Meter = m.Meter; p.StarActive = m.Star; p.Failed = m.Failed;
        if (string.IsNullOrEmpty(m.Res)) return;
        for (int i = 0; i < m.Res.Length; i++)
        {
            int idx = m.From + i;
            if (idx < 0 || idx >= p.Notes.Count) continue;
            var n = p.Notes[idx];
            if (m.Res[i] == 'h' && !n.Hit)
            {
                n.Hit = true;
                if (n.Sustain && n.EndTime > _t) { n.Holding = true; n.HeldUntil = n.Time; }
                for (int l = 0; l < 5; l++) if ((n.Mask & (1 << l)) != 0) g.Hw.OnHit(l, n.Star || m.Star);
            }
            else if (m.Res[i] == 'm') n.Missed = true;
        }
        p.Next = Math.Max(p.Next, Math.Min(p.Notes.Count, m.From + m.Res.Length));
    }

    /// <summary>Background slots for up to 5 other players: (centre x, top, scale) as fractions of the screen.</summary>
    static readonly (float cx, float top, float sc)[] GhostSlots =
        { (0.165f, 0.30f, 0.42f), (0.835f, 0.30f, 0.42f), (0.32f, 0.15f, 0.30f), (0.68f, 0.15f, 0.30f), (0.5f, 0.07f, 0.24f) };
    static readonly int[] GhostDrawOrder = { 4, 2, 3, 0, 1 };

    static Rectangle GhostView(int slot)
    {
        var (cx, top, sc) = GhostSlots[slot];
        float w = Gfx.W * sc, h = Gfx.H * sc;
        return new Rectangle(Gfx.W * cx - w / 2, Gfx.H * top, w, h);
    }

    static void DrawGhosts(double t, float dt)
    {
        foreach (int slot in GhostDrawOrder)
        {
            if (slot >= _ghosts.Count) continue;
            var g = _ghosts[slot];
            if (!Net.Players.Any(p => p.Id == g.Id)) continue;          // left the room
            // sustains end by themselves on the ghost
            for (int i = Math.Max(0, g.P.Next - 8); i < Math.Min(g.P.Notes.Count, g.P.Next + 2); i++)
                if (g.P.Notes[i].Holding && t >= g.P.Notes[i].EndTime) g.P.Notes[i].Holding = false;

            var view = GhostView(slot);
            // rendered small and scaled up: a soft, slightly blurred look
            int rw = Math.Max(64, (int)(view.Width / 3.2f)), rh = Math.Max(36, (int)(view.Height / 3.2f));
            if (g.RtW != rw || g.RtH != rh)
            {
                if (g.RtW > 0) Raylib.UnloadRenderTexture(g.Rt);
                g.Rt = Raylib.LoadRenderTexture(rw, rh); g.RtW = rw; g.RtH = rh;
                Raylib.SetTextureFilter(g.Rt.Texture, TextureFilter.Bilinear);
            }
            Raylib.BeginTextureMode(g.Rt);
            Raylib.ClearBackground(new Color(0, 0, 0, 0));
            g.Hw.Draw(g.P, t, dt, new Rectangle(0, Gfx.H - rh, rw, rh), g.P.StarActive);
            Raylib.EndTextureMode();
            var tint = g.P.Failed ? new Color(150, 110, 110, 170) : new Color(205, 205, 215, 215);
            Raylib.DrawTexturePro(g.Rt.Texture, new Rectangle(0, 0, rw, -rh), view, Vector2.Zero, 0, tint);

            // name tag at the strike line
            var col = SlotColor(g.Id);
            string label = g.P.Failed ? g.Name + "  (vaiado)" : g.Name;
            float fs = 15 + 5 * GhostSlots[slot].sc;
            var m = Raylib.MeasureTextEx(Gfx.Bold, label, fs * Gfx.S, 0);
            var c = new Vector2(view.X + view.Width / 2, view.Y + view.Height * 0.16f);   // above the far end of the board
            var r = new Rectangle(c.X - m.X / 2 - 18 * Gfx.S, c.Y - m.Y / 2 - 4 * Gfx.S, m.X + 30 * Gfx.S, m.Y + 8 * Gfx.S);
            Raylib.DrawRectangleRounded(r, 0.5f, 8, new Color(15, 12, 10, 170));
            Raylib.DrawCircleV(new Vector2(r.X + 11 * Gfx.S, c.Y), 5 * Gfx.S, col);
            Raylib.DrawTextEx(Gfx.Bold, label, new Vector2(r.X + 22 * Gfx.S, c.Y - m.Y / 2), fs * Gfx.S, 0, new Color(240, 235, 225, 230));
        }
    }

    // ================================================================= scoreboard (top of the gameplay screen)
    static void DrawScoreboard()
    {
        var list = Net.Players;
        int n = list.Count;
        if (n == 0) return;
        var scores = list.Select(LiveScore).ToList();
        float gap = 10 * Gfx.S;
        float cw = Math.Min(215 * Gfx.S, (Gfx.W - 60 * Gfx.S - gap * (n - 1)) / (n + 0.15f));
        float total = cw * n + cw * 0.15f + gap * (n - 1), x = (Gfx.W - total) / 2, y = 10 * Gfx.S;
        for (int i = 0; i < n; i++)
        {
            var np = list[i];
            bool me = np.Id == Net.MyId;
            float w = me ? cw * 1.15f : cw, h = (me ? 66 : 56) * Gfx.S;
            var r = new Rectangle(x, y, w, h);
            var col = SlotColors[i % SlotColors.Length];
            int rank = 1 + scores.Count(s => s > scores[i]);
            bool failed = me ? _player?.Failed == true : _ghosts.FirstOrDefault(g => g.Id == np.Id)?.P.Failed == true;
            if (me)
            {
                Raylib.DrawRectangleRounded(new Rectangle(r.X + 3 * Gfx.S, r.Y + 4 * Gfx.S, r.Width, r.Height), 0.25f, 8, new Color(0, 0, 0, 120));
                Raylib.DrawRectangleRounded(r, 0.25f, 8, new Color(244, 234, 208, 250));
                Raylib.DrawRectangleRoundedLinesEx(r, 0.25f, 8, 3.5f * Gfx.S, Paper.Red);
            }
            else
            {
                Raylib.DrawRectangleRounded(r, 0.25f, 8, new Color(18, 14, 12, 185));
                Raylib.DrawRectangleRoundedLinesEx(r, 0.25f, 8, 1.5f * Gfx.S, Gfx.A(col, 0.8f));
            }
            var ink = me ? Paper.Ink : new Color(235, 228, 215, 235);
            // slot number in its colour
            var dc = new Vector2(r.X + 20 * Gfx.S, r.Y + h / 2);
            Raylib.DrawCircleV(dc, 13 * Gfx.S, col);
            Gfx.Text(Gfx.Bold, (i + 1).ToString(), dc.X, dc.Y - 9 * Gfx.S, 15, Color.White, 0.5f);
            float tx = r.X + 40 * Gfx.S, inner = w - 50 * Gfx.S;
            Gfx.Text(Gfx.Bold, Paper.Fit(np.Name, inner - 34 * Gfx.S, me ? 16 : 14, Gfx.Bold), tx, r.Y + 6 * Gfx.S, me ? 16 : 14, ink);
            Gfx.Text(Gfx.Title, scores[i].ToString("N0").Replace(',', '.'), tx, r.Y + (me ? 26 : 24) * Gfx.S, me ? 30 : 24, failed ? Gfx.A(ink, 0.5f) : ink);
            Gfx.Text(Gfx.Bold, rank + "º", r.X + w - 10 * Gfx.S, r.Y + 6 * Gfx.S, me ? 18 : 15, rank == 1 ? new Color(230, 160, 20, 255) : Gfx.A(ink, 0.7f), 1);
            if (failed) Gfx.Text(Gfx.Bold, "VAIADO", r.X + w - 10 * Gfx.S, r.Y + h - 22 * Gfx.S, 13, Paper.Red, 1);
            x += w + gap;
        }
    }

    static long LiveScore(NetPlayer np)
    {
        if (np.Id == Net.MyId) return _player?.Score ?? np.Score;
        var g = _ghosts.FirstOrDefault(x => x.Id == np.Id);
        return np.Done ? np.Score : Math.Max(np.Score, g?.P.Score ?? 0);
    }

    // ================================================================= per-frame network pump
    static void OnlineUpdate()
    {
        if (!Net.Active) return;
        foreach (var m in Net.Poll())
        {
            switch (m.T)
            {
                case "closed":
                    if (_screen is Screen.Play or Screen.Loading or Screen.OnlineResults) Audio.Stop();
                    _online = false; _quitAsk = false; ClearGhosts();
                    _pickMode = false; _rows = null;
                    Go(Screen.Online);
                    break;
                case "load":
                {
                    var s = Net.FindSong(m.Song);
                    var me = Net.Me;
                    _online = true; _goAt = -1; _sentLoaded = false; _quitAsk = false; _pickMode = false; _rows = null;
                    if (s == null || me == null) { Net.Send(new Msg { T = "done", Failed = true }); Go(Screen.OnlineResults); break; }
                    _previewFor = null;
                    StartSong(s, Math.Clamp(me.Diff, 0, 3));
                    break;
                }
                case "go": _goAt = Net.ToLocal(m.Time); break;
                case "st": ApplyGhost(m); break;
            }
        }
    }

    static void SendState()
    {
        var p = _player;
        if (p == null || Net.Clock < _nextState) return;
        _nextState = Net.Clock + 1 / 12.0;
        int to = Math.Min(p.Next, p.Notes.Count);
        string res = "";
        if (to > _sentNext)
        {
            var sb = new System.Text.StringBuilder(to - _sentNext);
            for (int i = _sentNext; i < to; i++) sb.Append(p.Notes[i].Hit ? 'h' : 'm');
            res = sb.ToString();
        }
        Net.Send(new Msg { T = "st", Score = p.Score, Streak = p.Streak, Energy = p.Energy, Meter = p.Meter, Star = p.StarActive, Failed = p.Failed, From = _sentNext, Res = res });
        _sentNext = Math.Max(_sentNext, to);
    }

    static void SendDone(bool quit)
    {
        var p = _player;
        if (p == null) { Net.Send(new Msg { T = "done", Failed = true }); return; }
        _nextState = 0; SendState();
        Net.Send(new Msg { T = "done", Score = p.Score, Stars = quit ? 0 : p.Stars, Hit = p.NotesHit, Total = p.NotesTotal, Best = p.BestStreak, Failed = p.Failed || quit });
    }

    /// <summary>Esc during an online song: the song keeps going for everyone; you may give up.</summary>
    static void OnlineQuitOverlay()
    {
        if (!_quitAsk) { if (Input.Pause && !_ended) { _quitAsk = true; Audio.PlaySfx(Sfx.Select); } return; }
        var card = new Rectangle((Gfx.W - 520 * S) / 2, Gfx.H * 0.36f, 520 * S, 170 * S);
        Paper.Card(card, -1, true, false);
        Paper.Text("Desistir da música?", Gfx.W / 2f, card.Y + 26 * S, 32, Paper.Red, 0.5f, -1);
        Paper.Text("A música continua para os outros jogadores.", Gfx.W / 2f, card.Y + 74 * S, 17, Paper.InkSoft, 0.5f, -1, Gfx.Body);
        Paper.Text("Confirmar = desistir      Voltar = continuar tocando", Gfx.W / 2f, card.Y + 112 * S, 18, Paper.Ink, 0.5f, -1);
        if (Input.Back || (Input.Pause && _screenT > 0)) { _quitAsk = false; return; }
        if (Input.Confirm)
        {
            _quitAsk = false; _ended = true;
            Audio.Stop();
            SendDone(true);
            Go(Screen.OnlineResults);
        }
    }

    // ================================================================= online menu
    static int _onSel;
    static string _codeText = "";
    static string _onStatus;
    static Task<string> _connectTask;
    static (System.Net.IPAddress ip, string via)? _addr;
    static double _addrAt = -99;

    static unsafe bool EditText(ref string s, int max, bool code)
    {
        bool changed = false;
        int ch;
        while ((ch = Raylib.GetCharPressed()) > 0)
        {
            char c = (char)ch;
            if (s.Length >= max || char.IsControl(c)) continue;
            if (code) { if (!(char.IsLetterOrDigit(c) || c is '.' or '-' or ':')) continue; c = char.ToUpperInvariant(c); }
            s += c; changed = true;
        }
        if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && s.Length > 0) { s = s[..^1]; changed = true; }
        if ((Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl)) && Raylib.IsKeyPressed(KeyboardKey.V))
        {
            sbyte* p = Raylib.GetClipboardText();
            if (p != null)
            {
                foreach (char c0 in new string(p).Trim())
                {
                    char c = code ? char.ToUpperInvariant(c0) : c0;
                    if (s.Length >= max || char.IsControl(c)) continue;
                    if (code && !(char.IsLetterOrDigit(c) || c is '.' or '-' or ':')) continue;
                    s += c; changed = true;
                }
            }
        }
        return changed;
    }

    static void OnlineMenuScreen()
    {
        double t = _screenT;
        PumpPreview();
        if (Raylib.GetTime() - _addrAt > 3) { _addr = Net.BestAddress(); _addrAt = Raylib.GetTime(); }
        Paper.Backdrop(t);
        Paper.Ribbon(new Vector2(250 * S, 62 * S), 360 * S, 62 * S, -2, "MULTIPLAYER ONLINE", true, false, t, 0.9f);

        // ---- left: the form
        var card = new Rectangle(50 * S, 120 * S, 620 * S, Gfx.H - 200 * S);
        Paper.Card(card, -0.4f, true, true);
        bool busy = _connectTask != null;
        string name = Cfg.PlayerName ?? "";
        float x0 = card.X + 50 * S;
        float[] ys = { card.Y + 40 * S, card.Y + 150 * S, card.Y + 250 * S, card.Y + 360 * S, card.Y + 440 * S };

        // 0 name
        Paper.Text("Seu nome (aparece no placar):", x0, ys[0], 20, Paper.InkSoft);
        var nameBox = new Rectangle(x0, ys[0] + 32 * S, 400 * S, 44 * S);
        DrawField(nameBox, name, _onSel == 0, t);
        // 1 create
        var c1 = new Vector2(x0 + 200 * S, ys[1] + 18 * S);
        Paper.Ribbon(c1, 330 * S, 52 * S, -1.5f, "CRIAR SALA", !busy, _onSel == 1, t);
        // 2 code
        Paper.Text("Código da sala do amigo:", x0, ys[2] - 10 * S, 20, Paper.InkSoft);
        var codeBox = new Rectangle(x0, ys[2] + 22 * S, 400 * S, 44 * S);
        DrawField(codeBox, _codeText, _onSel == 2, t, "ex.: 4F2-K9QA");
        // 3 join
        var c3 = new Vector2(x0 + 200 * S, ys[3] + 18 * S);
        Paper.Ribbon(c3, 330 * S, 52 * S, 1.5f, busy ? "CONECTANDO..." : "ENTRAR NA SALA", !busy, _onSel == 3, t);
        // 4 back
        var c4 = new Vector2(x0 + 200 * S, ys[4] + 18 * S);
        Paper.Ribbon(c4, 220 * S, 44 * S, -1, "VOLTAR", true, _onSel == 4, t);

        // selection marker
        Rectangle selR = _onSel switch
        {
            0 => nameBox, 2 => codeBox,
            1 => new Rectangle(c1.X - 190 * S, c1.Y - 34 * S, 380 * S, 68 * S),
            3 => new Rectangle(c3.X - 190 * S, c3.Y - 34 * S, 380 * S, 68 * S),
            _ => new Rectangle(c4.X - 135 * S, c4.Y - 30 * S, 270 * S, 60 * S),
        };
        Paper.Arrow(new Vector2(selR.X - 8 * S, selR.Y + selR.Height / 2), t, 40);

        string status = _onStatus ?? Net.Error;
        if (status != null) Paper.Text(Paper.Fit(status, card.Width + 520 * S, 17, Gfx.Body), card.X + 30 * S, card.Y + card.Height + 14 * S, 17, Paper.Red, 0, 0, Gfx.Body);

        // ---- right: how it works + Radmin status
        var info = new Rectangle(card.X + card.Width + 40 * S, 120 * S, Gfx.W - card.X - card.Width - 90 * S, 380 * S);
        Paper.Card(info, 0.8f, true, false, new Color(250, 238, 160, 255));
        float ix = info.X + 26 * S, iy = info.Y + 24 * S, iw = info.Width - 52 * S;
        Paper.Text("Como jogar com os amigos", ix, iy, 24, Paper.Red, 0, 0.8f); iy += 46 * S;
        string[] steps =
        {
            "1. Todos instalam o Radmin VPN e entram",
            "   na mesma rede dele.",
            "2. Um de vocês clica em CRIAR SALA e",
            "   passa o código para os outros.",
            "3. Os outros digitam o código e entram.",
            "4. Só aparecem as músicas que todos têm.",
        };
        foreach (var s in steps) { Paper.Text(s, ix, iy, 17, Paper.Ink, 0, 0.8f, Gfx.Body); iy += 27 * S; }
        iy += 14 * S;
        if (_addr is var (ip, via) && via == "Radmin VPN")
            Paper.Text($"Radmin VPN ligado  ({ip})", ix, iy, 18, new Color(30, 120, 50, 255), 0, 0.8f, Gfx.Bold);
        else
            Paper.Text("Radmin VPN não encontrado neste PC", ix, iy, 18, Paper.Red, 0, 0.8f, Gfx.Bold);
        iy += 34 * S;
        Paper.Text(Paper.Fit("Na 1ª vez o Windows pode pedir permissão de rede: permita.", iw, 15, Gfx.Body), ix, iy, 15, Paper.InkSoft, 0, 0.8f, Gfx.Body);

        Paper.Hints((Paper.Blue, "Confirmar"), (Paper.RedGem, "Voltar (Esc)"));

        // ---- connecting in the background
        if (_connectTask is { IsCompleted: true })
        {
            string err = _connectTask.Result; _connectTask = null;
            if (err == null) { Cfg.Save(); _onStatus = null; Go(Screen.Lobby); _lobbySel = 0; return; }
            _onStatus = err;
        }
        if (busy) return;

        // ---- input
        if (_onSel == 0 && EditText(ref name, 16, false)) { Cfg.PlayerName = name; _onStatus = null; }
        if (_onSel == 2 && EditText(ref _codeText, 21, true)) _onStatus = null;
        if (Input.Up) { _onSel = (_onSel + 4) % 5; Audio.PlaySfx(Sfx.Click); }
        if (Input.Down) { _onSel = (_onSel + 1) % 5; Audio.PlaySfx(Sfx.Click); }
        if (Input.Cancel) { Audio.PlaySfx(Sfx.Back); Net.Error = null; _onStatus = null; Go(Screen.Title); return; }
        if (Input.Confirm)
        {
            Net.Error = null;
            string nm = string.IsNullOrWhiteSpace(Cfg.PlayerName) ? "Jogador" : Cfg.PlayerName.Trim();
            Cfg.PlayerName = nm;
            switch (_onSel)
            {
                case 0: _onSel = 1; break;
                case 1:
                    Audio.PlaySfx(Sfx.Select);
                    _onStatus = "Abrindo a sala...";
                    _connectTask = Task.Run(() => Net.Host(nm, Cfg.LastDifficulty));
                    break;
                case 2: _onSel = 3; break;
                case 3:
                {
                    var ip2 = Net.ParseCode(_codeText);
                    if (ip2 == null) { _onStatus = "Código inválido. Ele tem 7 letras/números, tipo 4F2-K9QA."; Audio.PlaySfx(Sfx.Back); break; }
                    Audio.PlaySfx(Sfx.Select);
                    _onStatus = "Conectando...";
                    string code = _codeText;
                    _connectTask = Task.Run(() => Net.Join(ip2, nm, Cfg.LastDifficulty, code));
                    break;
                }
                case 4: Audio.PlaySfx(Sfx.Back); Go(Screen.Title); break;
            }
        }
    }

    static void DrawField(Rectangle r, string text, bool focus, double t, string placeholder = null)
    {
        Raylib.DrawRectangleRec(r, new Color(255, 252, 240, 255));
        Raylib.DrawRectangleLinesEx(r, 2 * S, focus ? Paper.Red : Paper.InkSoft);
        bool empty = string.IsNullOrEmpty(text);
        var m = Paper.Text(empty ? placeholder ?? "" : text, r.X + 12 * S, r.Y + 8 * S, 24, empty ? Gfx.A(Paper.InkSoft, 0.5f) : Paper.Ink);
        if (focus && (t * 2 % 1) < 0.6)
        {
            float cx = r.X + 14 * S + (empty ? 0 : m.X);
            Raylib.DrawLineEx(new Vector2(cx, r.Y + 8 * S), new Vector2(cx, r.Y + r.Height - 8 * S), 2 * S, Paper.Ink);
        }
    }

    // ================================================================= lobby
    static int _lobbySel;
    static string _lobbyPreviewKey;
    static bool _pickMode;

    static void LobbyScreen()
    {
        double t = _screenT;
        if (!Net.Active) { Go(Screen.Online); return; }
        var me = Net.Me;
        bool host = me?.Host == true;
        var song = Net.FindSong(Net.Song);

        // preview the chosen song
        if (Net.Song != _lobbyPreviewKey)
        {
            _lobbyPreviewKey = Net.Song;
            if (song != null) { Audio.Stop(); PlayPreview(song, 0); }
        }
        PumpPreview();

        Paper.Backdrop(t);
        Paper.Ribbon(new Vector2(185 * S, 56 * S), 280 * S, 58 * S, -2.5f, "SALA ONLINE", true, false, t);

        // ---- room code sticky note
        var note = new Rectangle(Gfx.W - 470 * S, 18 * S, 430 * S, 86 * S);
        Paper.Card(note, 1.2f, true, false, new Color(250, 238, 160, 255));
        Paper.Text("CÓDIGO DA SALA", note.X + 22 * S, note.Y + 10 * S, 16, Paper.InkSoft, 0, 1.2f, Gfx.Bold);
        Paper.Text(Net.RoomCode ?? "", note.X + 22 * S, note.Y + 30 * S, 40, Paper.Red, 0, 1.2f, Gfx.Title);
        string sub = host ? $"{Net.RoomVia} • {Net.RoomIp}" : $"conectado a {Net.RoomIp}";
        Paper.Text(sub, note.X + note.Width - 20 * S, note.Y + 16 * S, 14, Paper.InkSoft, 1, 1.2f, Gfx.Body);
        if (host && Net.RoomVia != "Radmin VPN") Paper.Text("Ligue o Radmin VPN!", note.X + note.Width - 20 * S, note.Y + 42 * S, 15, Paper.Red, 1, 1.2f, Gfx.Bold);

        // ---- players
        var page = new Rectangle(40 * S, 110 * S, 600 * S, Gfx.H - 176 * S);
        Paper.Card(page, 0, true, true);
        Paper.Text($"JOGADORES ({Net.Players.Count}/{NetServer.MaxPlayers})", page.X + 64 * S, page.Y + 30 * S, 22, Paper.Teal);
        float rowH = (page.Height - 90 * S) / NetServer.MaxPlayers;
        for (int i = 0; i < NetServer.MaxPlayers; i++)
        {
            float y = page.Y + 76 * S + i * rowH;
            var dc = new Vector2(page.X + 80 * S, y + rowH / 2 - 4 * S);
            if (i >= Net.Players.Count)
            {
                Raylib.DrawCircleLinesV(dc, 14 * S, Gfx.A(Paper.InkSoft, 0.4f));
                Paper.Text("- vago -", page.X + 108 * S, y + 4 * S, 20, Gfx.A(Paper.InkSoft, 0.45f));
                continue;
            }
            var p = Net.Players[i];
            Raylib.DrawCircleV(dc, 15 * S, SlotColors[i % SlotColors.Length]);
            Gfx.Text(Gfx.Bold, (i + 1).ToString(), dc.X, dc.Y - 10 * S, 17, Color.White, 0.5f);
            string nm = p.Name + (p.Id == Net.MyId ? "  (você)" : "");
            Paper.Text(Paper.Fit(nm, 250 * S, 22), page.X + 108 * S, y + 2 * S, 22, p.Id == Net.MyId ? Paper.Ink : Paper.InkSoft);
            Paper.Text(Chart.DiffNames[Math.Clamp(p.Diff, 0, 3)], page.X + 108 * S, y + 30 * S, 15, Paper.InkSoft, 0, 0, Gfx.Body);
            string st = p.Host ? "ANFITRIÃO" : p.Ready ? "PRONTO!" : "esperando...";
            var sc = p.Host ? Paper.Teal : p.Ready ? new Color(30, 130, 50, 255) : Gfx.A(Paper.InkSoft, 0.7f);
            Paper.Text(st, page.X + page.Width - 34 * S, y + 8 * S, 20, sc, 1);
        }

        // ---- right column: chosen song, difficulty, menu
        float colX = page.X + page.Width + 30 * S, colW = Gfx.W - colX - 30 * S, cx = colX + colW / 2;
        if (song != null)
        {
            Paper.Polaroid(song, new Vector2(cx - 140 * S, 225 * S), 140 * S, -3 + MathF.Sin((float)t * 0.8f) * 0.6f);
            float tx = cx - 40 * S;
            Paper.Text(Paper.Fit(song.Name, colX + colW - tx, 26), tx, 170 * S, 26, Paper.Ink);
            Paper.Text(Paper.Fit(song.Artist, colX + colW - tx, 19), tx, 206 * S, 19, Paper.Red);
            Paper.Text(SongDb.Games.FirstOrDefault(g => g.Id == song.Game)?.Name ?? "", tx, 236 * S, 15, Paper.InkSoft, 0, 0, Gfx.Body);
        }
        else
        {
            Paper.Text(host ? "Escolha a música da partida" : "O anfitrião está escolhendo a música...", cx, 190 * S, 22, Paper.InkSoft, 0.5f);
        }
        Paper.Text($"{Net.Common.Count} músicas que todos têm", cx, 300 * S, 15, Paper.InkSoft, 0.5f, 0, Gfx.Body);

        // difficulty (each player picks their own)
        int diff = Math.Clamp(me?.Diff ?? Cfg.LastDifficulty, 0, 3);
        float dy = 360 * S, dw = colW / 4;
        for (int d = 0; d < 4; d++)
        {
            var c = new Vector2(colX + dw * d + dw / 2, dy);
            bool on = d == diff;
            Paper.Ribbon(c, dw * 0.78f, 32 * S, d % 2 == 0 ? -2 : 2, Chart.DiffNames[d].ToUpperInvariant(), on, false, t + d, 1.1f);
            if (on) Paper.RedCircle(c, dw * 0.55f, 28 * S, t);
        }

        // menu
        bool allReady = Net.Players.Count > 0 && Net.Players.All(p => p.Host || p.Ready);
        string[] items = host
            ? new[] { "ESCOLHER MÚSICA", "COMEÇAR!", "SAIR DA SALA" }
            : new[] { me?.Ready == true ? "NÃO ESTOU PRONTO" : "ESTOU PRONTO!", "SAIR DA SALA" };
        _lobbySel = Math.Clamp(_lobbySel, 0, items.Length - 1);
        for (int i = 0; i < items.Length; i++)
        {
            var c = new Vector2(cx, 438 * S + i * 64 * S);
            bool enabled = !(host && i == 1 && (song == null || !allReady));
            Paper.Ribbon(c, 300 * S, 48 * S, i % 2 == 0 ? -1.2f : 1.2f, items[i], enabled, i == _lobbySel, t + i);
            if (i == _lobbySel) Paper.RedBox(new Rectangle(c.X - 175 * S, c.Y - 30 * S, 350 * S, 60 * S), t);
        }
        if (host && song != null && !allReady) Paper.Text("Esperando todos ficarem PRONTOS", cx, 438 * S + items.Length * 64 * S - 18 * S, 15, Paper.Red, 0.5f, 0, Gfx.Body);
        if (Net.Error != null) Paper.Text(Net.Error, cx, Gfx.H - 96 * S, 15, Paper.Red, 0.5f, 0, Gfx.Body);

        Paper.Hints((Paper.Blue, "Confirmar"), (Paper.YellowGem, "< > Dificuldade"), (Paper.RedGem, "Sair da sala"));

        // ---- automation (tests)
        if (_autoReady && me != null && !me.Host && !me.Ready && _screenT > 0.5) Net.Send(new Msg { T = "ready", Ready = true });
        if (host && _autoPick != null && Net.Song == null && _screenT > 0.5)
        {
            var ps = SongDb.Songs.FirstOrDefault(s => s.Key == _autoPick || s.Id == _autoPick);
            if (ps != null && Net.Common.Contains(Net.SongKey(ps))) Net.Send(new Msg { T = "pick", Song = Net.SongKey(ps) });
        }
        if (host && _autoStart > 0 && Net.Players.Count >= _autoStart && allReady && song != null && Net.Phase == "lobby" && Raylib.GetTime() - _autoStartedAt > 3)
        { _autoStartedAt = Raylib.GetTime(); Net.Send(new Msg { T = "start" }); }

        // ---- input
        if (Input.Left || Input.Right)
        {
            int nd = Math.Clamp(diff + (Input.Right ? 1 : -1), 0, 3);
            if (nd != diff) { Cfg.LastDifficulty = nd; Net.Send(new Msg { T = "diff", Diff = nd }); Audio.PlaySfx(Sfx.Click); if (me != null) me.Diff = nd; }
        }
        if (Input.Up) { _lobbySel = (_lobbySel + items.Length - 1) % items.Length; Audio.PlaySfx(Sfx.Click); }
        if (Input.Down) { _lobbySel = (_lobbySel + 1) % items.Length; Audio.PlaySfx(Sfx.Click); }
        bool leave = Input.Back;
        if (Input.Confirm)
        {
            string it = items[_lobbySel];
            if (it == "ESCOLHER MÚSICA")
            {
                if (Net.Common.Count == 0) { Audio.PlaySfx(Sfx.Back); }
                else { Audio.PlaySfx(Sfx.Select); _pickMode = true; _rows = null; _sel = -1; Go(Screen.Setlist); }
            }
            else if (it == "COMEÇAR!")
            {
                if (song != null && allReady) { Audio.PlaySfx(Sfx.Select); Net.Send(new Msg { T = "start" }); }
                else Audio.PlaySfx(Sfx.Back);
            }
            else if (it == "SAIR DA SALA") leave = true;
            else { Audio.PlaySfx(Sfx.Select); Net.Send(new Msg { T = "ready", Ready = me?.Ready != true }); }
        }
        if (leave)
        {
            Audio.PlaySfx(Sfx.Back);
            Net.Leave(); Net.Error = null;
            Audio.Stop(); _lobbyPreviewKey = null; _previewFor = null;
            Go(Screen.Online);
        }
    }

    // ================================================================= final ranking
    static void OnlineResultsScreen()
    {
        double t = _screenT;
        Paper.Backdrop(t);
        bool allDone = Net.Players.All(p => p.Done) && Net.Phase == "lobby";
        Paper.Ribbon(new Vector2(Gfx.W / 2f, 58 * S), 470 * S, 62 * S, -1.5f, allDone ? "RESULTADO DO DUELO" : "AGUARDANDO OS OUTROS...", true, false, t, 0.95f);

        var list = Net.Players.Select(p => (p, score: LiveScore(p))).OrderByDescending(x => x.score).ToList();
        var card = new Rectangle(90 * S, 120 * S, Gfx.W - 180 * S, Gfx.H - 196 * S);
        Paper.Card(card, 0.4f, true, true);
        float rowH = Math.Min(80 * S, (card.Height - 70 * S) / Math.Max(1, list.Count));
        float x0 = card.X + 60 * S;
        Paper.Text("NOTAS", card.X + card.Width - 320 * S, card.Y + 22 * S, 15, Paper.InkSoft, 0.5f, 0, Gfx.Bold);
        Paper.Text("SEQUÊNCIA", card.X + card.Width - 200 * S, card.Y + 22 * S, 15, Paper.InkSoft, 0.5f, 0, Gfx.Bold);
        for (int i = 0; i < list.Count; i++)
        {
            var (p, score) = list[i];
            float y = card.Y + 56 * S + i * rowH;
            bool me = p.Id == Net.MyId;
            int slot = Net.Players.IndexOf(p);
            if (me) Paper.RedBox(new Rectangle(card.X + 30 * S, y - 6 * S, card.Width - 60 * S, rowH - 6 * S), t, 2.4f);
            int rank = 1 + list.Count(x => x.score > score);
            Paper.Text(rank + "º", x0, y, 40, rank == 1 && allDone ? new Color(200, 130, 10, 255) : Paper.Ink, 0, 0, Gfx.Title);
            var dc = new Vector2(x0 + 90 * S, y + 22 * S);
            Raylib.DrawCircleV(dc, 13 * S, SlotColors[Math.Max(0, slot) % SlotColors.Length]);
            Paper.Text(Paper.Fit(p.Name, 300 * S, 28), x0 + 116 * S, y + 4 * S, 28, Paper.Ink);
            Paper.Text(score.ToString("N0").Replace(',', '.'), x0 + 480 * S, y + 2 * S, 34, Paper.Ink, 1, 0, Gfx.Title);
            if (p.Done)
            {
                if (p.Failed) Paper.Stamp("VAIADO", new Vector2(x0 + 560 * S, y + 22 * S), 15, -6, Paper.Red);
                else Paper.Stars(x0 + 500 * S, y + 10 * S, 20, p.Stars);
                Paper.Text(p.Total > 0 ? $"{p.Hit * 100 / p.Total}%" : "-", card.X + card.Width - 320 * S, y + 8 * S, 24, Paper.Ink, 0.5f);
                Paper.Text(p.Best.ToString(), card.X + card.Width - 200 * S, y + 8 * S, 24, Paper.Ink, 0.5f);
            }
            else Paper.Text("tocando...", card.X + card.Width - 300 * S, y + 8 * S, 20, Paper.InkSoft, 0.5f);
            if (rank == 1 && allDone && list.Count > 1) Paper.Stamp("VENCEDOR!", new Vector2(card.X + card.Width - 90 * S, y + 22 * S), 15, 8, new Color(200, 130, 10, 255));
        }

        if (allDone)
        {
            Paper.Hints((Paper.Blue, "Voltar para a sala"), (Paper.RedGem, "Sair da sala"));
            if (Input.Confirm) { Audio.PlaySfx(Sfx.Select); FinishOnlineMatch(); Go(Screen.Lobby); }
            else if (Input.Back) { Audio.PlaySfx(Sfx.Back); FinishOnlineMatch(); Net.Leave(); Go(Screen.Online); }
        }
        else Paper.Hints((Paper.RedGem, "Esperando o fim da música dos outros"));
    }

    static void FinishOnlineMatch()
    {
        _online = false; _quitAsk = false; ClearGhosts();
        _lobbyPreviewKey = null; _previewFor = null; Audio.Stop();
    }
}
