using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Riffstorm;

/// <summary>Menu screens in the sketchbook style of the title screen.</summary>
public static partial class Game
{
    static float S => Gfx.S;

    // ================================================================= library / discs
    /// <summary>Loads the library and starts watching the ISOs folder; an empty library shows the Discs screen.</summary>
    static void OpenLibrary()
    {
        Cfg.LibraryPath ??= SongDb.DefaultRoot;
        SongDb.Load(Cfg.LibraryPath);
        if (!_noIso) IsoWatcher.Start(Cfg);
        if (SongDb.Songs.Count == 0) { Go(Screen.Boot); return; }
        EnterGame();
    }

    static void EnterGame()
    {
        if (_autoHost || _autoJoin != null)
        {
            string err = _autoHost ? Net.Host(Cfg.PlayerName, Cfg.LastDifficulty) : Net.Join(Net.ParseCode(_autoJoin), Cfg.PlayerName, Cfg.LastDifficulty, _autoJoin);
            if (err != null) { _onStatus = err; Go(Screen.Online); } else Go(Screen.Lobby);
        }
        else if (_demoSong != null)
        {
            var s = SongDb.Songs.FirstOrDefault(x => x.Id == _demoSong || x.Key == _demoSong) ?? SongDb.Songs[0];
            StartSong(s, _demoDiff);
        }
        else if (_shotScreen is "setlist" or "Setlist") Go(Screen.Setlist);
        else if (_shotScreen is "settings" or "Settings") Go(Screen.Settings);
        else if (_shotScreen is "discs" or "Discs") Go(Screen.Discs);
        else Go(Screen.Title);
    }

    /// <summary>Called every frame from the main loop: picks up discs imported in the background.</summary>
    static void PollLibrary()
    {
        if (_screen is Screen.Play or Screen.Loading) return;
        if (!IsoWatcher.TakeLibraryChanged()) return;
        SongDb.Load(Cfg.LibraryPath);
        _rows = null; _sel = -1;
        if (_screen == Screen.Boot && SongDb.Songs.Count > 0) EnterGame();
    }

    static unsafe void HandleDrop()
    {
        if (!Raylib.IsFileDropped()) return;
        var list = Raylib.LoadDroppedFiles();
        string p = list.Count > 0 ? Marshal.PtrToStringUTF8((IntPtr)list.Paths[0]) : null;
        Raylib.UnloadDroppedFiles(list);
        if (p != null) { IsoWatcher.Offer(p); if (_screen != Screen.Play) Go(Screen.Discs); }
    }

    /// <summary>Small status note while a disc is being verified/imported in the background.</summary>
    static void DrawBusyNote()
    {
        var b = IsoWatcher.Busy;
        if (b == null || _screen is Screen.Play or Screen.Boot or Screen.Discs) return;
        var m = Raylib.MeasureTextEx(Gfx.Hand, b, 17 * S, 0);
        var r = new Rectangle(18 * S, Gfx.H - 52 * S, m.X + 30 * S, 34 * S);
        Raylib.DrawRectangleRounded(r, 0.4f, 8, new Color(30, 22, 15, 200));
        Raylib.DrawTextEx(Gfx.Hand, b, new Vector2(r.X + 15 * S, r.Y + 6 * S), 17 * S, 0, new Color(250, 235, 205, 255));
    }

    static void DiscsScreen()
    {
        bool empty = SongDb.Songs.Count == 0;
        Paper.Backdrop(_screenT);
        Paper.Ribbon(new Vector2(250 * S, 62 * S), 330 * S, 62 * S, -2, "DISCOS", true, false, _screenT);
        Paper.Text("Coloque a ISO na pasta abaixo – ela é verificada e importada sozinha.", 500 * S, 40 * S, 19, Paper.Ink);
        Paper.Text(IsoWatcher.Folder ?? "", 500 * S, 70 * S, 16, Paper.Teal, 0, 0, Gfx.Bold);

        var card = new Rectangle(50 * S, 120 * S, Gfx.W - 100 * S, Gfx.H - 210 * S);
        Paper.Card(card, -0.4f, true, true);
        float x0 = card.X + 70 * S, y = card.Y + 34 * S;
        Paper.Text("Discos compatíveis (mesma versão usada no projeto)", x0, y, 22, Paper.Ink); y += 46 * S;
        var entries = IsoWatcher.Entries;
        foreach (var kg in Importer.Known)
        {
            var e = entries.FirstOrDefault(x => x.Game == kg);
            bool inLib = SongDb.Games.Any(g => g.Id == kg.Id);
            string state = inLib ? "Na biblioteca" : e != null ? IsoWatcher.Label(e.State) : "Não encontrado na pasta";
            var col = inLib ? new Color(30, 120, 50, 255) : e == null ? Paper.InkSoft : e.State is IsoWatcher.State.Importing or IsoWatcher.State.Checking ? Paper.Teal : e.State == IsoWatcher.State.NotYetSupported ? new Color(160, 100, 20, 255) : Paper.Red;
            Paper.Text($"{kg.Name}  ({kg.Platform})", x0, y, 22, Paper.Ink);
            Paper.Text(state, card.X + card.Width - 60 * S, y, 22, col, 1);
            string detail = e != null && !inLib ? e.Detail : inLib ? $"{SongDb.Games.First(g => g.Id == kg.Id).TierSongs.Sum(t => t.Count) + SongDb.Games.First(g => g.Id == kg.Id).BonusSongs.Count} músicas" : (kg.Supported ? "" : "Importação deste jogo ainda vai ser feita");
            Paper.Text(detail, x0 + 20 * S, y + 28 * S, 16, Paper.InkSoft, 0, 0, Gfx.Body);
            if (e != null && e.State is IsoWatcher.State.Importing or IsoWatcher.State.Checking)
            {
                var bar = new Rectangle(card.X + card.Width - 360 * S, y + 32 * S, 300 * S, 10 * S);
                Raylib.DrawRectangleRec(bar, new Color(200, 185, 150, 255));
                Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * e.Progress, bar.Height), Paper.Teal);
                Raylib.DrawRectangleLinesEx(bar, 1.5f * S, Paper.Ink);
            }
            y += 68 * S;
        }
        var bad = entries.Where(x => x.State == IsoWatcher.State.Incompatible).ToList();
        if (bad.Count > 0)
        {
            Paper.Text("Arquivos que não são compatíveis:", x0, y + 4 * S, 19, Paper.Red); y += 34 * S;
            foreach (var e in bad.Take(3)) { Paper.Text($"{e.File} – {e.Detail}", x0 + 20 * S, y, 15, Paper.InkSoft, 0, 0, Gfx.Body); y += 24 * S; }
        }
        if (empty) Paper.Stamp("SEM MÚSICAS AINDA", new Vector2(card.X + card.Width - 190 * S, card.Y + 12 * S), 18, 6, Paper.Red);

        Paper.Hints((Paper.Blue, "Abrir pasta ISOs"), (Paper.RedGem, empty ? "Sair" : "Voltar"));
        if (Input.Confirm) IsoWatcher.OpenFolder();
        if (Input.Back) { if (empty) _quit = true; else Go(_screen == Screen.Discs ? Screen.Settings : Screen.Title); }
    }

    // ================================================================= setlist
    static void SetlistScreen(float dt)
    {
        PumpPreview();
        if (_rows == null) BuildRows();
        if (_rows.Count == 0) { Go(Screen.Boot); return; }
        if (_diff < 0) _diff = Cfg.LastDifficulty;
        if (_sel < 0 || _sel >= _rows.Count || _rows[_sel].Song == null)
        {
            _sel = -1;
            if (_selByGame.TryGetValue(SongDb.Games[_gameTab].Id, out var lastId)) _sel = _rows.FindIndex(r => r.Song?.Id == lastId);
            if (_sel < 0) _sel = _rows.FindIndex(r => r.Song != null);
            if (_sel < 0) { _rows = null; _gameTab = (_gameTab + 1) % SongDb.Games.Count; return; }   // empty game: skip it
        }

        // ---- L1 / R1: previous / next game (wraps around)
        int pg = (Input.NextPage ? 1 : 0) - (Input.PrevPage ? 1 : 0);
        if (pg != 0 && SongDb.Games.Count > 1)
        {
            _selByGame[SongDb.Games[_gameTab].Id] = _rows[_sel].Song.Id;
            _gameTab = (_gameTab + pg +SongDb.Games.Count) % SongDb.Games.Count;
            _rows = null; _sel = -1; _scroll = 0; _tabChangedAt = Raylib.GetTime();
            _selChangedAt = Raylib.GetTime(); Audio.PlaySfx(Sfx.Click, 0.8f);
            BuildRows();
            if (_selByGame.TryGetValue(SongDb.Games[_gameTab].Id, out var lid)) _sel = _rows.FindIndex(r => r.Song?.Id == lid);
            if (_sel < 0) _sel = _rows.FindIndex(r => r.Song != null);
            if (_sel < 0) return;
            _scroll = Math.Max(0, _sel * 34 * S - (Gfx.H - 222 * S) / 2 + 34 * S);
        }

        int move = (Input.Down ? 1 : 0) - (Input.Up ? 1 : 0);
        if (move != 0)
        {
            int n = _sel;
            do { n = (n + move + _rows.Count) % _rows.Count; } while (_rows[n].Song == null);
            _sel = n; _selChangedAt = Raylib.GetTime(); Audio.PlaySfx(Sfx.Click, 0.6f);
        }
        if (Input.Left) { _diff = Math.Max(0, _diff - 1); Audio.PlaySfx(Sfx.Click); }
        if (Input.Right) { _diff = Math.Min(3, _diff + 1); Audio.PlaySfx(Sfx.Click); }
        var song = _rows[_sel].Song;
        if (_previewFor != song && Raylib.GetTime() - _selChangedAt > 0.35) { Audio.Stop(); PlayPreview(song, 0); }
        if (_pickMode)
        {
            if (Input.Confirm) { Audio.PlaySfx(Sfx.Select); Net.Send(new Msg { T = "pick", Song = Net.SongKey(song) }); }
            if (Input.Confirm || Input.Back)
            {
                if (Input.Back) Audio.PlaySfx(Sfx.Back);
                _pickMode = false; _rows = null; _sel = -1; _lobbyPreviewKey = null; _previewFor = null;
                Go(Screen.Lobby); return;
            }
        }
        else
        {
            if (Input.Confirm) { Audio.PlaySfx(Sfx.Select); Cfg.LastDifficulty = _diff; StartSong(song, _diff); return; }
            if (Input.Back) { Audio.PlaySfx(Sfx.Back); Go(Screen.Title); _titleSong = null; return; }
        }

        double t = _screenT;
        Paper.Backdrop(t);
        Paper.Ribbon(new Vector2(160 * S, 56 * S), 230 * S, 56 * S, -2.5f, "SETLIST", true, false, t);
        DrawGameTabs(t);

        // ---- notebook page with the songs (slides in a little when the game changes)
        float slide = (float)Math.Max(0, 1 - (Raylib.GetTime() - _tabChangedAt) / 0.18);
        var page = new Rectangle(40 * S, 104 * S + slide * 14 * S, 650 * S, Gfx.H - 170 * S);
        Paper.Card(page, 0, true, true);
        float rowH = 34 * S;
        float listTop = page.Y + 40 * S, listH = page.Height - 52 * S;
        float target = _sel * rowH - listH / 2 + rowH;
        _scroll += (target - _scroll) * Math.Min(1, dt * 12);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _rows.Count * rowH - listH));
        Raylib.BeginScissorMode((int)(page.X + 4 * S), (int)(page.Y + 30 * S), (int)(page.Width - 8 * S), (int)(page.Height - 36 * S));
        for (int i = 0; i < _rows.Count; i++)
        {
            float y = listTop + i * rowH - _scroll;
            if (y < page.Y - rowH || y > page.Y + page.Height) continue;
            var r = _rows[i];
            if (r.Header != null)
            {
                var hc = r.IsGame ? Paper.Teal : Paper.Red;
                var m = Paper.Text(r.Header.ToUpperInvariant(), page.X + 64 * S, y - 2 * S, r.IsGame ? 24 : 21, hc);
                Raylib.DrawLineEx(new Vector2(page.X + 62 * S, y + m.Y - 4 * S), new Vector2(page.X + 70 * S + m.X, y + m.Y - 7 * S), 2 * S, Gfx.A(hc, 0.6f));
                continue;
            }
            bool sel = i == _sel;
            Paper.Text(Paper.Fit(r.Song.Name, 380 * S, 21), page.X + 84 * S, y, 21, sel ? Paper.Ink : Paper.InkSoft);
            Cfg.BestStars.TryGetValue(Key(r.Song, _diff), out int st);
            if (st > 0) Paper.Stars(page.X + page.Width - 28 * S, y + 6 * S, 15, st, 5, 1);
            if (sel)
            {
                Paper.RedBox(new Rectangle(page.X + 74 * S, y - 2 * S, page.Width - 96 * S, rowH - 2 * S), t, 2.6f);
                Paper.Arrow(new Vector2(page.X + 70 * S, y + rowH / 2), t, 44);
            }
        }
        Raylib.EndScissorMode();

        // ---- the selected song: polaroid + hand-written notes
        float cx = 980 * S * (Gfx.W / (1280f * S));
        cx = page.X + page.Width + (Gfx.W - page.X - page.Width) / 2;
        Paper.Polaroid(song, new Vector2(cx, 220 * S), 200 * S, 3 + MathF.Sin((float)t * 0.8f) * 0.6f);
        float ty = 380 * S;
        Paper.Text(Paper.Fit(song.Name, Gfx.W - cx - 30 * S + (cx - page.X - page.Width) - 30 * S, 30), cx, ty, 30, Paper.Ink, 0.5f);
        Paper.Text(song.Artist, cx, ty + 42 * S, 21, Paper.Red, 0.5f);
        var gname = SongDb.Games.FirstOrDefault(g => g.Id == song.Game)?.Name ?? "";
        Paper.Text($"{gname}{(song.Bpm > 0 ? $"  •  {song.Bpm} BPM" : "")}", cx, ty + 74 * S, 16, Paper.InkSoft, 0.5f, 0, Gfx.Body);
        Cfg.Best.TryGetValue(Key(song, _diff), out int best);
        Cfg.BestStars.TryGetValue(Key(song, _diff), out int bestSt);
        Paper.Text(best > 0 ? $"Recorde: {best:N0}".Replace(',', '.') : "Ainda não tocada", cx, ty + 102 * S, 20, Paper.Ink, 0.5f);
        if (bestSt > 0) Paper.Stars(cx, ty + 134 * S, 22, bestSt, 5, 0.5f);

        // ---- difficulty: four small ribbons, the chosen one circled in red
        float dy = Gfx.H - 128 * S, dw = (Gfx.W - page.X - page.Width - 50 * S) / 4;
        for (int d = 0; d < 4; d++)
        {
            var c = new Vector2(page.X + page.Width + 30 * S + dw * d + dw / 2, dy);
            bool on = d == _diff;
            Paper.Ribbon(c, dw * 0.78f, 34 * S, (d % 2 == 0 ? -2 : 2), Chart.DiffNames[d].ToUpperInvariant(), on, false, t + d, 1.1f);
            if (on) Paper.RedCircle(c, dw * 0.55f, 30 * S, t);
        }
        if (_pickMode)
            Paper.Hints((Paper.Blue, "Escolher esta"), (Paper.GreenGem, "L1 / R1 Jogo"), (Paper.RedGem, "Voltar para a sala"));
        else if (SongDb.Games.Count > 1)
            Paper.Hints((Paper.Blue, "Tocar"), (Paper.YellowGem, "< > Dificuldade"), (Paper.GreenGem, "L1 / R1 Jogo"), (Paper.RedGem, "Voltar"));
        else
            Paper.Hints((Paper.Blue, "Tocar"), (Paper.YellowGem, "< > Dificuldade"), (Paper.RedGem, "Voltar"));
    }

    static double _tabChangedAt = -9;

    /// <summary>One small ribbon per imported game across the top; the open one is circled in red. L1 ◄ … ► R1.</summary>
    static void DrawGameTabs(double t)
    {
        var games = SongDb.Games;
        if (games.Count == 0) return;
        float x0 = 330 * S, x1 = Gfx.W - 40 * S, y = 54 * S;
        bool many = games.Count > 1;
        if (many)
        {
            Paper.Text("L1", x0, y - 11 * S, 20, Paper.InkSoft, 0, 0, Gfx.Bold);
            Paper.Text("R1", x1, y - 11 * S, 20, Paper.InkSoft, 1, 0, Gfx.Bold);
            x0 += 56 * S; x1 -= 64 * S;
        }
        float w = (x1 - x0) / games.Count;
        for (int i = 0; i < games.Count; i++)
        {
            bool on = i == _gameTab;
            var c = new Vector2(x0 + w * i + w / 2, y + (on ? 0 : 4 * S));
            float rw = Math.Min(w * 0.76f, 230 * S);
            Paper.Ribbon(c, rw * (on ? 1.06f : 0.94f), (on ? 42 : 36) * S, (i % 2 == 0 ? -2 : 2), TabName(games[i]), on, false, t + i, on ? 1.05f : 0.95f);
            if (on) Paper.RedCircle(c, rw * 0.6f, 32 * S, t);
        }
    }

    // ================================================================= loading
    static void LoadingScreen()
    {
        double t = _screenT;
        Paper.Backdrop(t);
        Paper.Polaroid(_song, new Vector2(Gfx.W / 2f, 210 * S), 220 * S, -3 + MathF.Sin((float)t * 1.5f) * 1.5f);
        Paper.Ribbon(new Vector2(Gfx.W / 2f, 420 * S), 330 * S, 58 * S, 1.5f, "CARREGANDO...", true, false, t);
        Paper.Text(_song.Name, Gfx.W / 2f, 470 * S, 30, Paper.Ink, 0.5f);
        Paper.Text(_song.Artist, Gfx.W / 2f, 510 * S, 21, Paper.Red, 0.5f);
        var note = new Rectangle((Gfx.W - 860 * S) / 2, 560 * S, 860 * S, 80 * S);
        Paper.Card(note, -0.6f, true, false, new Color(250, 238, 160, 255));
        Paper.Text("Dica: " + _tip, Gfx.W / 2f, note.Y + 26 * S, 17, Paper.Ink, 0.5f, -0.6f);

        if (_online)
        {
            if (_loadTask.IsFaulted)
            {
                Paper.Text("Falha ao carregar: " + _loadTask.Exception?.InnerException?.Message, Gfx.W / 2f, 660 * S, 17, Paper.Red, 0.5f);
                if (!_sentLoaded) { _sentLoaded = true; Net.Send(new Msg { T = "done", Failed = true }); }
                if (Input.Back || Input.Confirm) Go(Screen.OnlineResults);
                return;
            }
            if (_loadTask.IsCompletedSuccessfully && !_sentLoaded && _screenT > 0.6) { _sentLoaded = true; Net.Send(new Msg { T = "loaded" }); }
            if (_sentLoaded)
            {
                int waiting = Net.Players.Count(p => !p.Loaded);
                Paper.Text(waiting > 0 ? $"Esperando {waiting} jogador(es) carregar..." : "Todos prontos!", Gfx.W / 2f, 660 * S, 20, Paper.Teal, 0.5f);
            }
            if (_sentLoaded && _goAt > 0 && Net.Clock >= _goAt)
            {
                var (pcm2, chart2) = _loadTask.Result;
                BeginPlay(pcm2, chart2);
            }
            return;
        }
        if (_loadTask.IsFaulted)
        {
            Paper.Text("Falha ao carregar: " + _loadTask.Exception?.InnerException?.Message, Gfx.W / 2f, 660 * S, 17, Paper.Red, 0.5f);
            if (Input.Back || Input.Confirm) Go(Screen.Setlist);
            return;
        }
        if (_loadTask.IsCompletedSuccessfully && _screenT > 0.6)
        {
            var (pcm, chart) = _loadTask.Result;
            BeginPlay(pcm, chart);
        }
    }

    // ================================================================= pause (over the gameplay)
    static void PauseMenu()
    {
        Raylib.DrawRectangle(0, 0, Gfx.W, Gfx.H, new Color(0, 0, 0, 150));
        var card = new Rectangle((Gfx.W - 470 * S) / 2, (Gfx.H - 360 * S) / 2, 470 * S, 360 * S);
        Paper.Card(card, -1, true, false);
        Paper.Text("PAUSA", Gfx.W / 2f, card.Y + 28 * S, 40, Paper.Red, 0.5f, -1);
        for (int i = 0; i < PauseItems.Length; i++)
        {
            var c = new Vector2(Gfx.W / 2f, card.Y + (130 + i * 76) * S);
            Paper.Ribbon(c, 300 * S, 52 * S, i % 2 == 0 ? -1.5f : 1.5f, PauseItems[i], true, i == _pauseSel, _screenT + i);
            if (i == _pauseSel) Paper.RedBox(new Rectangle(c.X - 170 * S, c.Y - 34 * S, 340 * S, 68 * S), _screenT);
        }
        if (Input.Up) { _pauseSel = (_pauseSel + 2) % 3; Audio.PlaySfx(Sfx.Click); }
        if (Input.Down) { _pauseSel = (_pauseSel + 1) % 3; Audio.PlaySfx(Sfx.Click); }
        if (Input.Pause && _screenT > 0) { Resume(); return; }
        if (Input.Back) { Resume(); return; }
        if (Input.Confirm)
        {
            Audio.PlaySfx(Sfx.Select);
            if (_pauseSel == 0) Resume();
            else if (_pauseSel == 1) { StartSong(_song, _playDiff); }
            else { Audio.Stop(); Go(Screen.Setlist); _previewFor = null; }
        }
    }

    // ================================================================= results
    static void ResultsScreen()
    {
        var p = _player;
        double t = _screenT;
        Paper.Backdrop(t);
        Paper.Ribbon(new Vector2(Gfx.W / 2f, 62 * S), 470 * S, 66 * S, -1.5f, p.Failed ? "VAIADO!" : "MÚSICA COMPLETA", true, false, t);

        Paper.Polaroid(_song, new Vector2(260 * S, 330 * S), 250 * S, -4 + MathF.Sin((float)t * 0.7f) * 0.8f);
        Paper.Text(Paper.Fit(_song.Name, 420 * S, 26), 260 * S, 520 * S, 26, Paper.Ink, 0.5f);
        Paper.Text($"{_song.Artist}  •  {Chart.DiffNames[_playDiff]}", 260 * S, 556 * S, 18, Paper.Red, 0.5f);

        var card = new Rectangle(520 * S, 130 * S, Gfx.W - 580 * S, 450 * S);
        Paper.Card(card, 1, true, true);
        float cx = card.X + card.Width / 2;
        int shownStars = (int)Math.Min(_stars, t * 2.5);
        Paper.Stars(cx, card.Y + 40 * S, 52, shownStars, 5, 0.5f);
        double sc = Math.Min(1, t / 1.2) * p.Score;
        Paper.Text(((long)sc).ToString("N0").Replace(',', '.'), cx, card.Y + 112 * S, 76, Paper.Ink, 0.5f, 1, Gfx.Title);
        Raylib.DrawLineEx(new Vector2(cx - 180 * S, card.Y + 206 * S), new Vector2(cx + 190 * S, card.Y + 200 * S), 3 * S, Gfx.A(Paper.Red, 0.7f));
        int pct = p.NotesTotal > 0 ? (int)Math.Round(100.0 * p.NotesHit / p.NotesTotal) : 0;
        Paper.Text($"Notas acertadas: {p.NotesHit} / {p.NotesTotal}  ({pct}%)", card.X + 70 * S, card.Y + 240 * S, 22, Paper.Ink, 0, 1);
        Paper.Text($"Maior sequência: {p.BestStreak}", card.X + 70 * S, card.Y + 280 * S, 22, Paper.Ink, 0, 1);
        if (_newRecord) Paper.Stamp("NOVO RECORDE!", new Vector2(card.X + card.Width - 150 * S, card.Y + card.Height - 70 * S), 22, -10 + MathF.Sin((float)t * 3) * 2, Paper.Red);

        Paper.Hints((Paper.Blue, "Continuar"), (Paper.YellowGem, "Tocar de novo"));
        if (t > 0.8)
        {
            if (Input.Confirm || Input.Back) { Audio.PlaySfx(Sfx.Select); Go(Screen.Setlist); _previewFor = null; }
            else if (Raylib.IsKeyPressed(KeyboardKey.R) || (Input.Pad >= 0 && Raylib.IsGamepadButtonPressed(Input.Pad, GamepadButton.RightFaceUp))) StartSong(_song, _playDiff);
        }
    }

    // ================================================================= settings
    static int _setSel;
    static void SettingsScreen()
    {
        PumpPreview();
        var items = new List<(string label, string value, Action<int> change, Action enter)>
        {
            ("Tipo de controle", Cfg.Controller == 0 ? "Controle (estilo PS2)" : "Guitarra", d => Cfg.Controller = 1 - Cfg.Controller, null),
            ("Discos (pasta ISOs)", ">>", null, () => Go(Screen.Discs)),
            ("Palhetada", Cfg.AutoStrum ? "Automática" : "Obrigatória (PS2)", d => Cfg.AutoStrum = !Cfg.AutoStrum, null),
            ("Atraso de áudio", $"{Cfg.AudioOffsetMs} ms", d => Cfg.AudioOffsetMs = Math.Clamp(Cfg.AudioOffsetMs + d * 5, -300, 500), null),
            ("Calibrar atraso de áudio", ">>", null, () => { StartCalibration(); }),
            ("Velocidade das notas", $"{Cfg.NoteSpeed:0.0}x", d => Cfg.NoteSpeed = MathF.Round(Math.Clamp(Cfg.NoteSpeed + d * 0.1f, 0.5f, 2.5f), 1), null),
            ("Modo canhoto", Cfg.Lefty ? "Ligado" : "Desligado", d => Cfg.Lefty = !Cfg.Lefty, null),
            ("Sem falha (não é vaiado)", Cfg.NoFail ? "Ligado" : "Desligado", d => Cfg.NoFail = !Cfg.NoFail, null),
            ("Volume da música", $"{(int)Math.Round(Cfg.MusicVol * 100)}%", d => { Cfg.MusicVol = Math.Clamp(Cfg.MusicVol + d * 0.05f, 0, 1); ApplyVolumes(); }, null),
            ("Volume dos efeitos", $"{(int)Math.Round(Cfg.SfxVol * 100)}%", d => { Cfg.SfxVol = Math.Clamp(Cfg.SfxVol + d * 0.05f, 0, 1); ApplyVolumes(); Audio.PlaySfx(Sfx.Click); }, null),
            ("Tela cheia (F11)", Cfg.Fullscreen ? "Ligado" : "Desligado", d => { Raylib.ToggleBorderlessWindowed(); Cfg.Fullscreen = !Cfg.Fullscreen; }, null),
            ("Voltar", "", null, () => { Cfg.Save(); Go(Screen.Title); }),
        };
        if (Input.Up) { _setSel = (_setSel + items.Count - 1) % items.Count; Audio.PlaySfx(Sfx.Click); }
        if (Input.Down) { _setSel = (_setSel + 1) % items.Count; Audio.PlaySfx(Sfx.Click); }
        var it = items[_setSel];
        if (it.change != null && (Input.Left || Input.Right)) { it.change(Input.Right ? 1 : -1); Audio.PlaySfx(Sfx.Click); }
        if (Input.Confirm) { if (it.enter != null) { Audio.PlaySfx(Sfx.Select); it.enter(); return; } else it.change?.Invoke(1); }
        if (Input.Back) { Audio.PlaySfx(Sfx.Back); Cfg.Save(); Go(Screen.Title); return; }

        double t = _screenT;
        Paper.Backdrop(t);
        Paper.Ribbon(new Vector2(270 * S, 56 * S), 400 * S, 60 * S, -2, "CONFIGURAÇÕES", true, false, t);
        var page = new Rectangle(40 * S, 104 * S, 720 * S, Gfx.H - 170 * S);
        Paper.Card(page, 0, true, true);
        float rowH = (page.Height - 56 * S) / items.Count;
        for (int i = 0; i < items.Count; i++)
        {
            float y = page.Y + 40 * S + i * rowH;
            bool sel = i == _setSel;
            Paper.Text(items[i].label, page.X + 70 * S, y, 20, sel ? Paper.Ink : Paper.InkSoft);
            string v = items[i].value;
            if (sel && items[i].change != null) v = "<  " + v + "  >";
            Paper.Text(v, page.X + page.Width - 30 * S, y, 20, sel ? Paper.Red : Paper.Teal, 1);
            if (sel) { Paper.RedBox(new Rectangle(page.X + 60 * S, y - 3 * S, page.Width - 80 * S, rowH - 2 * S), t, 2.6f); Paper.Arrow(new Vector2(page.X + 56 * S, y + rowH / 2), t, 40); }
        }

        // controls on a sticky note
        var note = new Rectangle(800 * S, 110 * S, Gfx.W - 840 * S, 470 * S);
        Paper.Card(note, 2, true, false, new Color(250, 238, 160, 255));
        float nx = note.X + 26 * S;
        Paper.Text("CONTROLES", note.X + note.Width / 2, note.Y + 18 * S, 26, Paper.Red, 0.5f, 2);
        string[] help = Cfg.Controller == 0
            ? new[] { "Trastes (verde -> laranja):", "  L2  L1  R1  R2  X", "  (Xbox: LT LB RB RT A)", "Palhetada: D-pad cima/baixo", "Fúria: Triângulo/Y ou Select", "Alavanca: analógico direito", "Pausa: Start", "Teclado: A S D F G", "Palhetada: Enter / setas", "Fúria: Espaço  •  Pausa: Esc" }
            : new[] { "Trastes: verde vermelho", "  amarelo azul laranja", "  (A B Y X LB)", "Palhetada: barra (D-pad)", "Alavanca: analógico direito", "Fúria: Select ou incline", "Pausa: Start", "Teclado: A S D F G", "Palhetada: Enter / setas" };
        for (int i = 0; i < help.Length; i++)
            Paper.Text(help[i], nx, note.Y + (66 + i * 32) * S, 17, Paper.Ink, 0, 2);
        for (int l = 0; l < 5; l++)
        {
            var c = new Vector2(note.X + note.Width / 2 + (l - 2) * 46 * S, note.Y + note.Height - 40 * S);
            Raylib.DrawCircleV(c, 17 * S, Paper.Ink);
            Raylib.DrawCircleV(c, 14 * S, Input.Fret[l] ? Gfx.Bright(Gfx.Fret[l], 1.2f) : Gfx.Lerp(Gfx.Fret[l], new Color(240, 230, 190, 255), 0.6f));
        }
        Paper.Text(Input.Pad >= 0 ? "Conectado: " + Input.PadName : "Nenhum controle (usando teclado)", note.X + note.Width / 2, note.Y + note.Height + 14 * S, 15, Paper.InkSoft, 0.5f, 0, Gfx.Body);
        Paper.Hints((Paper.Blue, "Escolher"), (Paper.YellowGem, "< > Mudar"), (Paper.RedGem, "Voltar"));
    }

    // ================================================================= calibration
    static void CalibrateScreen()
    {
        double t = _screenT;
        Paper.Backdrop(t);
        Paper.Ribbon(new Vector2(Gfx.W / 2f, 62 * S), 420 * S, 62 * S, -1.5f, "CALIBRAR ÁUDIO", true, false, t);
        double at = Audio.Time;
        var card = new Rectangle((Gfx.W - 880 * S) / 2, 140 * S, 880 * S, 420 * S);
        Paper.Card(card, -0.5f, true, true);
        Paper.Text("Palhete (ou aperte Enter) no ritmo dos cliques que você OUVE.", Gfx.W / 2f, card.Y + 40 * S, 21, Paper.Ink, 0.5f);
        Paper.Text("Não olhe para a tela – siga só o som. São 12 batidas.", Gfx.W / 2f, card.Y + 76 * S, 18, Paper.InkSoft, 0.5f);
        if (Input.Strum || Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            double nearest = Math.Round(at / CalBeat) * CalBeat;
            double off = at - nearest;
            if (Math.Abs(off) < 0.25 && at > 0.3) _calHits.Add(off);
        }
        for (int i = 0; i < 12; i++)
        {
            var c = new Vector2(Gfx.W / 2f + (i - 5.5f) * 56 * S, card.Y + 170 * S);
            if (i < _calHits.Count) Gfx.StarShape(c, 20 * S, Paper.Red);
            else { Raylib.DrawCircleLinesV(c, 16 * S, Paper.InkSoft); Raylib.DrawCircleLinesV(c, 15 * S, Paper.InkSoft); }
        }
        if (_calHits.Count >= 12)
        {
            var sorted = _calHits.OrderBy(v => v).ToList();
            int ms = (int)Math.Round(sorted[sorted.Count / 2] * 1000);
            Paper.Text($"Atraso medido: {ms} ms", Gfx.W / 2f, card.Y + 240 * S, 40, Paper.Ink, 0.5f);
            Paper.Hints((Paper.Blue, "Salvar"), (Paper.RedGem, "Cancelar"));
            if (Input.Confirm && !Input.Strum) { Cfg.AudioOffsetMs = Math.Clamp(ms, -300, 500); Cfg.Save(); Audio.Stop(); Go(Screen.Settings); return; }
        }
        else
        {
            Paper.Text($"Atraso atual: {Cfg.AudioOffsetMs} ms", Gfx.W / 2f, card.Y + 250 * S, 22, Paper.InkSoft, 0.5f);
            Paper.Hints((Paper.RedGem, "Cancelar"));
        }
        if (Input.Back || at > 39) { Audio.Stop(); Go(Screen.Settings); }
    }
}
