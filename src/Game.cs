using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Riffstorm;

public enum Screen { Boot, Discs, Title, Setlist, Loading, Play, Results, Settings, Calibrate, Online, Lobby, OnlineResults, Controls, HighwayPick }

public static partial class Game
{
    public static Settings Cfg;
    static Screen _screen = Screen.Boot;
    static double _screenT;            // time since screen entered
    static string _bootError;

    // test automation (--demo / --shot)
    static string _demoSong; static double _demoSeek = -2; static double _shotAt = -1; static string _shotFile; static bool _bot; static int _demoDiff = 3;
    static string _shotScreen;

    public static void Run(string[] args)
    {
        Cfg = Settings.Load();
        Updater.CleanupOld();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--lib": Cfg.LibraryPath = args[++i]; break;
                case "--demo": _demoSong = args[++i]; break;
                case "--seek": _demoSeek = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--diff": _demoDiff = int.Parse(args[++i]); break;
                case "--bot": _bot = true; break;
                case "--tab": _gameTab = int.Parse(args[++i]); break;
                case "--name": Cfg.PlayerName = args[++i]; break;
                case "--host": _autoHost = true; break;
                case "--join": _autoJoin = args[++i]; break;
                case "--autopick": _autoPick = args[++i]; break;
                case "--autostart": _autoStart = int.Parse(args[++i]); break;
                case "--autoready": _autoReady = true; break;
                case "--no-iso": _noIso = true; break;
                case "--autoupdate": _autoUpdate = true; break;
                case "--autoreact": _autoReact = true; break;
                case "--loopback": NetServer.LoopbackOnly = true; break;
                case "--shot": _shotScreen = args[++i]; _shotAt = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); _shotFile = args[++i]; break;
            }
        }

        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.InitWindow(1280, 720, "Riffstorm");
        Raylib.SetWindowMinSize(800, 450);
        Raylib.SetExitKey(KeyboardKey.Null);
        Gfx.Init();
        R3.Init();
        Audio.Init();
        Sfx.Build();
        ApplyVolumes();
        if (Cfg.Fullscreen) Raylib.ToggleBorderlessWindowed();

        OpenLibrary();

        double shotClock = 0;
        while (!Raylib.WindowShouldClose())
        {
            float dt = Math.Min(Raylib.GetFrameTime(), 0.1f);
            _screenT += dt; shotClock += dt;
            Gfx.UpdateScale();
            Input.Update(Cfg);
            if (Raylib.IsKeyPressed(KeyboardKey.F11)) { Raylib.ToggleBorderlessWindowed(); Cfg.Fullscreen = !Cfg.Fullscreen; Cfg.Save(); }
            HandleDrop();
            PollLibrary();
            OnlineUpdate();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);
            switch (_screen)
            {
                case Screen.Boot: case Screen.Discs: DiscsScreen(); break;
                case Screen.Title: TitleScreen(dt); break;
                case Screen.Setlist: SetlistScreen(dt); break;
                case Screen.Loading: LoadingScreen(); break;
                case Screen.Play: PlayScreen(dt); break;
                case Screen.Results: ResultsScreen(); break;
                case Screen.Settings: SettingsScreen(); break;
                case Screen.Calibrate: CalibrateScreen(); break;
                case Screen.Online: OnlineMenuScreen(); break;
                case Screen.Lobby: LobbyScreen(); break;
                case Screen.OnlineResults: OnlineResultsScreen(); break;
                case Screen.Controls: ControlsScreen(); break;
                case Screen.HighwayPick: HighwayPickScreen(); break;
            }
            DrawBusyNote();
            DrawFade(dt);
            Raylib.EndDrawing();

            if (_shotAt >= 0 && _screen.ToString().Equals(_shotScreen, StringComparison.OrdinalIgnoreCase) && _screenT >= _shotAt)
            {
                Raylib.TakeScreenshot(_shotFile);
                break;
            }
            if (_quit) break;
        }
        Net.Leave();
        Cfg.Save();
        Audio.Shutdown();
        Raylib.CloseWindow();
    }

    static bool _quit;
    static float _fade = 1;
    static void DrawFade(float dt)
    {
        _fade = Math.Max(0, _fade - dt * 3);
        if (_fade > 0) Raylib.DrawRectangle(0, 0, Gfx.W, Gfx.H, new Color(0, 0, 0, (int)(255 * _fade)));
    }

    static void Go(Screen s)
    {
        _screen = s; _screenT = 0; _fade = 0.8f;
    }

    static void ApplyVolumes()
    {
        Audio.MusicVolume = Cfg.MusicVol;
        Audio.SfxVolume = Cfg.SfxVol;
    }

    // ================================================================= title
    static int _titleSel;
    static bool _titleMenu;
    static Song _titleSong;
    static readonly string[] TitleItems = { "TOCAR", "CONFIGURAÇÕES", "SAIR" };

    static void TitleScreen(float dt)
    {
        if (_titleSong == null && SongDb.Songs.Count > 0)
        {
            _titleSong = SongDb.Songs[new Random().Next(SongDb.Songs.Count)];
            PlayPreview(_titleSong, -1);
        }
        PumpPreview();
        int choice = TitleMenu.Update(ref _titleSel, _screenT, dt);
        if (_titleSong != null)
        {
            string np = $"Tocando: {_titleSong.Name} - {_titleSong.Artist}";
            var m = Raylib.MeasureTextEx(Gfx.Hand, np, 18 * Gfx.S, 0);
            var r = new Rectangle(Gfx.W - m.X - 40 * Gfx.S, 12 * Gfx.S, m.X + 24 * Gfx.S, m.Y + 8 * Gfx.S);
            Raylib.DrawRectangleRounded(r, 0.5f, 8, new Color(20, 14, 10, 150));
            Raylib.DrawTextEx(Gfx.Hand, np, new Vector2(r.X + 12 * Gfx.S, r.Y + 4 * Gfx.S), 18 * Gfx.S, 0, new Color(245, 230, 200, 235));
        }
        UpdateButton();
        switch (choice)
        {
            case 0: Go(Screen.Setlist); _previewFor = null; _pickMode = false; _rows = null; break;
            case 2: Go(Screen.Online); _onSel = 0; _onStatus = null; break;
            case 3: Go(Screen.Settings); _setSel = 0; break;
            case 4: _quit = true; break;
        }
    }

    // ================================================================= update button (title, bottom-left)
    static bool _updateChecked, _autoUpdate;

    static void UpdateButton()
    {
        if (!_updateChecked) { _updateChecked = true; Updater.CheckAsync(); }
        var st = Updater.State;
        if (st == Updater.St.Ready) { if (Updater.Restart()) { _quit = true; return; } st = Updater.State; }
        string ver = "v" + Updater.CurrentText;
        string txt = st switch
        {
            Updater.St.Checking => "Procurando atualizações...",
            Updater.St.UpToDate => $"Você tem a versão mais nova ({ver})",
            Updater.St.Available => $"Nova versão {Updater.Latest}! Clique para atualizar",
            Updater.St.Downloading => $"Baixando atualização... {(int)(Updater.Progress * 100)}%",
            Updater.St.Installing => "Instalando...",
            Updater.St.Error => Updater.Error ?? "Erro",
            _ => $"Verificar atualizações ({ver})",
        };
        float s = Gfx.S;
        var m = Raylib.MeasureTextEx(Gfx.Hand, txt, 19 * s, 0);
        var r = new Rectangle(22 * s, Gfx.H - 70 * s, Math.Min(Gfx.W * 0.5f, m.X + 64 * s), 42 * s);
        var mouse = Raylib.GetMousePosition();
        bool hover = Raylib.CheckCollisionPointRec(mouse, r);
        bool avail = st == Updater.St.Available;
        float pulse = avail ? 0.5f + 0.5f * MathF.Sin((float)_screenT * 5) : 0;
        Raylib.DrawRectangleRounded(new Rectangle(r.X + 3 * s, r.Y + 4 * s, r.Width, r.Height), 0.5f, 10, new Color(0, 0, 0, 90));
        Raylib.DrawRectangleRounded(r, 0.5f, 10, avail ? Gfx.Lerp(new Color(250, 236, 200, 245), new Color(255, 215, 140, 255), pulse) : hover ? new Color(250, 244, 228, 245) : new Color(235, 228, 215, 230));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.5f, 10, (avail ? 3 : 2) * s, avail ? Paper.Red : new Color(90, 85, 80, 255));
        // circular-arrows icon
        var ic = new Vector2(r.X + 22 * s, r.Y + r.Height / 2);
        float spin = st is Updater.St.Checking or Updater.St.Downloading or Updater.St.Installing ? (float)_screenT * 300 : 0;
        Raylib.DrawRing(ic, 8 * s, 11 * s, spin + 20, spin + 160, 12, avail ? Paper.Red : Paper.Ink);
        Raylib.DrawRing(ic, 8 * s, 11 * s, spin + 200, spin + 340, 12, avail ? Paper.Red : Paper.Ink);
        Raylib.DrawTextEx(Gfx.Hand, txt, new Vector2(r.X + 42 * s, r.Y + (r.Height - m.Y) / 2), 19 * s, 0, st == Updater.St.Error ? Paper.Red : new Color(30, 25, 20, 255));
        if (st == Updater.St.Downloading)
        {
            var bar = new Rectangle(r.X + 42 * s, r.Y + r.Height - 8 * s, r.Width - 56 * s, 4 * s);
            Raylib.DrawRectangleRec(bar, new Color(200, 185, 150, 255));
            Raylib.DrawRectangleRec(new Rectangle(bar.X, bar.Y, bar.Width * Updater.Progress, bar.Height), Paper.Teal);
        }
        Raylib.DrawTextEx(Gfx.Body, "clique, F5 ou Select", new Vector2(r.X + 12 * s, r.Y + r.Height + 4 * s), 13 * s, 0, new Color(245, 235, 215, 200));

        bool click = (hover && Raylib.IsMouseButtonPressed(MouseButton.Left)) || Raylib.IsKeyPressed(KeyboardKey.F5)
                     || (Input.Pad >= 0 && Raylib.IsGamepadButtonPressed(Input.Pad, GamepadButton.MiddleLeft));
        if (!click) return;
        Audio.PlaySfx(Sfx.Select);
        if (avail) Updater.InstallAsync();
        else if (st is Updater.St.Idle or Updater.St.UpToDate or Updater.St.Error) Updater.CheckAsync();
    }

    // ================================================================= preview audio
    static Song _previewFor;
    static int _previewToken;
    static volatile short[][] _previewPcm;
    static int _previewPcmToken;

    static (float[] pans, float[] vols) _previewMix;

    static void PlayPreview(Song s, int token)
    {
        _previewFor = s;
        int my = ++_previewToken;
        Task.Run(() =>
        {
            try
            {
                double len = Math.Max(15, s.PreviewEnd - s.PreviewStart);
                var (pcm, pans, vols, _) = SongAudio.LoadPreview(s, len);
                _previewMix = (pans, vols);
                if (my != _previewToken) return;
                // fade edges for a clean loop
                int fl = Audio.Rate / 2;
                foreach (var c in pcm)
                    for (int i = 0; i < fl && i < c.Length; i++)
                    {
                        float k = i / (float)fl;
                        c[i] = (short)(c[i] * k); c[^(i + 1)] = (short)(c[^(i + 1)] * k);
                    }
                _previewPcmToken = my;
                _previewPcm = pcm;
            }
            catch { }
        });
    }

    static void PumpPreview()
    {
        var pcm = _previewPcm;
        if (pcm != null && _previewPcmToken == _previewToken && _previewFor != null)
        {
            _previewPcm = null;
            Audio.Loop = true;
            var mix = _previewMix;
            Audio.SetSong(pcm, mix.pans ?? _previewFor.Pans, mix.vols ?? _previewFor.Vols, new bool[pcm.Length], 0);
        }
    }

    // ================================================================= setlist
    sealed class Row { public string Header; public Song Song; public int Tier; public bool IsGame; }
    static List<Row> _rows;
    static int _sel = -1;
    static float _scroll;
    static double _selChangedAt;
    static int _diff = -1;

    static int _gameTab;                                   // which game's setlist page is open
    static readonly Dictionary<string, string> _selByGame = new();

    /// <summary>Short tab label for a game.</summary>
    static string TabName(GameEntry g) => g.Id switch
    {
        "gh1" => "GH 1", "gh2" => "GH 2", "gh80s" => "ROCKS THE 80s", "gh3" => "GH 3", "gha" => "AEROSMITH", "ghm" => "METALLICA",
        _ => g.Name.ToUpperInvariant(),
    };

    static void BuildRows()
    {
        _rows = new();
        if (SongDb.Games.Count == 0) return;
        _gameTab = Math.Clamp(_gameTab, 0, SongDb.Games.Count - 1);
        foreach (var g in new[] { SongDb.Games[_gameTab] })
        {
            _rows.Add(new Row { Header = g.Name.ToUpperInvariant(), Tier = -1, IsGame = true });
            // online song picker: only the songs every player in the room has
            bool Keep(Song s) => !_pickMode || Net.Common.Contains(Net.SongKey(s));
            for (int t = 0; t < g.TierSongs.Count; t++)
            {
                var list = g.TierSongs[t].Where(Keep).ToList();
                if (list.Count == 0) continue;
                _rows.Add(new Row { Header = $"{t + 1}. {g.Tiers[t].Name}", Tier = t });
                foreach (var s in list) _rows.Add(new Row { Song = s, Tier = t });
            }
            var bonus = g.BonusSongs.Where(Keep).ToList();
            if (bonus.Count > 0)
            {
                _rows.Add(new Row { Header = "Faixas Bônus", Tier = 6 });
                foreach (var s in bonus) _rows.Add(new Row { Song = s, Tier = 6 });
            }
        }
    }

    static string Key(Song s, int d) => s.Key + "#" + d;

    // ================================================================= loading
    static Song _song;
    static int _playDiff;
    static Task<(short[][] pcm, Chart chart)> _loadTask;
    static readonly string[] Tips =
    {
        "Notas com o centro branco são hammer-ons: dá pra tocar sem palhetar se você acertou a anterior.",
        "Acerte todas as notas em estrela de uma frase para encher a Energia.",
        "Com metade da Energia cheia, aperte Triângulo/Y (ou incline a guitarra) para ativar o MODO FÚRIA.",
        "No Modo Fúria o multiplicador dobra – até 8x!",
        "Segure as notas longas até o fim do rastro para ganhar pontos extras.",
        "Se o ponteiro do público entrar no vermelho, cuidado: você pode ser vaiado para fora do palco.",
        "Errou o tempo? Ajuste o atraso de áudio em Configurações → Calibrar.",
        "Você pode segurar trastes mais baixos junto com a nota certa – só o mais alto conta.",
    };
    static string _tip;

    static void StartSong(Song s, int diff)
    {
        Audio.Stop(); Audio.Loop = false;
        _song = s; _playDiff = diff;
        _tip = Tips[new Random().Next(Tips.Length)];
        _loadTask = Task.Run(() =>
        {
            var chart = Chart.Parse(File.ReadAllBytes(s.ChartPath));
            var pcm = SongAudio.Load(s);
            return (pcm, chart);
        });
        Go(Screen.Loading);
    }

    // ================================================================= gameplay
    static double _fpsAcc; static int _fpsN; static bool _fpsDone;
    static Player _player;
    static Highway _hw;
    static double _t, _lastT;
    static bool _paused, _ended, _failedShown;
    static int _pauseSel;
    static double _endAt;
    static double _announceAt = -10; static string _announce; static Color _announceCol;
    static int _lastStreakCall;
    static Bot _botPlayer;
    static bool _strumFlash;
    static short[][] _pcm; static Chart _chart;

    static void BeginPlay(short[][] pcm, Chart chart)
    {
        _pcm = pcm; _chart = chart;
        _player = new Player(chart, _playDiff);
        Highway.Surface = Highways.Current(Cfg);
        _hw = new Highway { Speed = 7.5f * Cfg.NoteSpeed * (_playDiff >= 2 ? 1.15f : 1f), Lefty = Cfg.Lefty };
        _paused = _ended = _failedShown = false; _pauseSel = 0;
        _lastStreakCall = 0; _announceAt = -10;
        _botPlayer = _bot ? new Bot() : null;
        double start = _demoSong != null || (_online && _demoSeek > 0) ? _demoSeek : -2.0;
        _sentNext = 0; _nextState = 0; _quitAsk = false;
        if (_online) CreateGhosts(); else ClearGhosts();
        ResetOnlineFun();
        if (start > 0)
        {
            // demo seek: mark earlier notes as already passed
            while (_player.Next < _player.Notes.Count && _player.Notes[_player.Next].Time < start) { _player.Notes[_player.Next].Hit = true; _player.Next++; }
        }
        Audio.Loop = false;
        Audio.SetSong(pcm, _song.Pans, _song.Vols, _song.Guitar, start);
        _t = start; _lastT = start;
        Go(Screen.Play);
        _fade = 0;
    }

    static void PlayScreen(float dt)
    {
        var p = _player;
        // ----- clock: follow the audio clock, smoothed
        double audioT = Audio.Time - Cfg.AudioOffsetMs / 1000.0;
        if (!_paused)
        {
            _t += dt;
            double err = audioT - _t;
            if (Math.Abs(err) > 0.08) _t = audioT; else _t += err * Math.Min(1, dt * 8);
        }
        double t = _t;
        double gdt = Math.Max(0, t - _lastT); _lastT = t;
        if (Environment.GetEnvironmentVariable("RIFF_FPSLOG") is string fl)
        {
            _fpsAcc += dt; _fpsN++;
            if (_screenT > 6 && !_fpsDone) { _fpsDone = true; File.WriteAllText(fl, $"avg frame {_fpsAcc / _fpsN * 1000:0.00} ms ({_fpsN / _fpsAcc:0} fps)"); }
        }

        // ----- input & rules
        if (!_paused && !_ended)
        {
            if (_botPlayer != null) _botPlayer.Drive(p, t);
            bool fretChange = false;
            for (int i = 0; i < 5; i++) fretChange |= Input.FretPressed[i] || Input.FretReleased[i];
            int held = Player.Held();
            bool anyPress = false;
            for (int i = 0; i < 5; i++) anyPress |= Input.FretPressed[i];
            float wh = Input.WhammyActive ? Input.Whammy : 0;
            p.Update(t, gdt, Input.Strum, fretChange, held, wh, Input.StarPower, Cfg.NoFail || _bot, Cfg.AutoStrum && !_bot, anyPress);
            Audio.WhammyTarget = p.HoldingAny ? wh : 0;
            _hw.Whammy = p.HoldingAny ? wh : 0;
            _hw.EnergyFilling = p.WhammyFilling;
            _strumFlash = Input.Strum;

            foreach (var (lane, _, star) in p.HitFx) _hw.OnHit(lane, star || p.StarActive);
            p.HitFx.Clear();
            for (int i = Math.Max(0, p.Next - 4); i < Math.Min(p.Notes.Count, p.Next + 1); i++)
                if (p.Notes[i].Holding)
                    for (int l = 0; l < 5; l++) if ((p.Notes[i].Mask & (1 << l)) != 0) _hw.OnSustainSpark(l, p.StarActive);
            if (p.MissFx.Count > 0) { Audio.PlaySfx(Sfx.Miss, 0.45f, 0); p.MissFx.Clear(); }
            if (p.JustReadyStar) { Audio.PlaySfx(Sfx.StarReady, 0.7f); Announce("FÚRIA PRONTA!", Gfx.StarBlue); }
            if (p.JustDeployed) { Audio.PlaySfx(Sfx.StarDeploy, 0.8f); Announce("MODO FÚRIA!", Gfx.StarBlue); }
            int call = p.Streak / 50 * 50;
            if (call >= 50 && call != _lastStreakCall) { _lastStreakCall = call; Announce($"{call} NOTAS SEGUIDAS!", new Color(255, 200, 60, 255)); }
            if (p.Streak < 50) _lastStreakCall = 0;

            if (p.Failed && !_failedShown)
            {
                _failedShown = true; _ended = true; _endAt = _screenT + 3.5;
                Audio.FadeOut(); Audio.PlaySfx(Sfx.Fail, 0.9f);
            }
            double songEnd = Math.Max(p.Chart.LastNoteTime + 1.5, Math.Min(Audio.Length, p.Chart.LastNoteTime + 12));
            if (!_ended && t > songEnd)
            {
                _ended = true; _endAt = _screenT + 2.5; Audio.FadeOut(); Audio.PlaySfx(Sfx.Applause, 0.8f);
                Announce("MÚSICA COMPLETA!", new Color(255, 230, 120, 255));
            }
        }
        if (_paused || _ended) Audio.WhammyTarget = 0;
        if (_online && Net.Active) SendState();
        if (_ended && _screenT > _endAt) { FinishSong(); return; }

        // ----- draw
        float beatLen = (float)p.Chart.BeatLengthAt(Math.Max(0, t));
        float beat = (float)Frac(BeatPhase(p.Chart, t));
        Covers.Backdrop(_song, t, beat, p.StarActive, p.Meter < 0.2f || p.Failed);
        if (_online) DrawGhosts(t, dt);
        var view = new Rectangle(0, 0, Gfx.W, Gfx.H);
        _hw.Draw(p, t, dt, view, p.StarActive);
        Highway.DrawHud(p, view, t);
        if (_online)
        {
            DrawScoreboard();
            LeaderWatch(t);
            DrawFeed();
            DrawReactions();
            if (!_ended) ReactionInput();
        }
        DrawProgress(t, p);

        // intro card
        if (t < 3.0)
        {
            float a = (float)Math.Clamp(Math.Min(_screenT * 3, (3.0 - t) * 2), 0, 1);
            float ccs = 120 * Gfx.S, iy = _online ? 96 * Gfx.S : 0;
            Covers.Draw(_song, new Rectangle(40 * Gfx.S, 40 * Gfx.S + iy, ccs, ccs), Gfx.A(Color.White, a));
            float tx0 = 40 * Gfx.S + ccs + 18 * Gfx.S;
            Gfx.Fancy(Gfx.Title, _song.Name.ToUpperInvariant(), tx0, 44 * Gfx.S + iy, 46, Color.White, new Color(170, 180, 200, 255), 0, 3, null, a);
            Gfx.Shadowed(Gfx.Bold, _song.Artist, tx0 + 4 * Gfx.S, 104 * Gfx.S + iy, 22, Gfx.A(new Color(255, 210, 120, 255), a));
            Gfx.Shadowed(Gfx.Body, Chart.DiffNames[_playDiff], tx0 + 4 * Gfx.S, 136 * Gfx.S + iy, 18, Gfx.A(Color.White, a * 0.8f));
            string how = Cfg.AutoStrum ? "Aperte o traste quando a nota chegar na linha" : "Segure o traste e PALHETE (Enter / setas / D-pad) quando a nota chegar";
            Gfx.Shadowed(Gfx.Bold, how, Gfx.W / 2f, Gfx.H * 0.52f, 22, Gfx.A(new Color(255, 230, 150, 255), a), 0.5f);
        }
        if (t < 0.6)
        {
            // count-in: numbers punch in with a ring, then "VAI!"
            double ct = -t;
            int n = (int)Math.Ceiling(ct);
            float fr = (float)(ct - Math.Floor(ct));          // 1 → 0 within each second
            if (ct <= 0) { n = 0; fr = (float)(1 + ct / 0.6); }
            float pop = 1 + MathF.Pow(fr, 3) * 0.8f;
            float a = Math.Clamp(fr * 3, 0, 1);
            var c = new Vector2(Gfx.W / 2f, Gfx.H * 0.33f);
            Raylib.DrawRing(c, 70 * Gfx.S * pop, 78 * Gfx.S * pop, 0, 360 * (1 - fr), 48, Gfx.A(new Color(255, 160, 30, 255), a));
            string txt = n > 0 ? n.ToString() : "VAI!";
            float size = (n > 0 ? 110 : 90) * pop;
            Gfx.Fancy(Gfx.Title, txt, c.X, c.Y - size * Gfx.S * 0.6f, size, new Color(255, 250, 200, 255), new Color(230, 50, 10, 255), 0.5f, 5, null, a);
        }
        // announcements
        double ad = _screenT - _announceAt;
        if (ad < 1.6)
        {
            float a = (float)Math.Clamp(Math.Min(ad * 6, (1.6 - ad) * 3), 0, 1);
            float sc = 1 + (float)Math.Max(0, 0.2 - ad) * 2;
            Gfx.Fancy(Gfx.Title, _announce, Gfx.W / 2f, Gfx.H * 0.22f, 54 * sc, Gfx.Bright(_announceCol, 1.3f), Gfx.Bright(_announceCol, 0.6f), 0.5f, 4, null, a);
        }
        if (p.Failed)
        {
            Raylib.DrawRectangle(0, 0, Gfx.W, Gfx.H, new Color(60, 0, 0, 120));
            Gfx.Fancy(Gfx.Title, "VOCÊ FOI VAIADO!", Gfx.W / 2f, Gfx.H * 0.35f, 80, new Color(255, 200, 180, 255), new Color(200, 20, 10, 255), 0.5f, 5);
        }
        if (_bot) Gfx.Shadowed(Gfx.Bold, "AUTOPLAY", Gfx.W - 20 * Gfx.S, 16 * Gfx.S, 18, new Color(120, 220, 255, 255), 1);

        // ----- pause (online: the song can't stop for the others; you can only give up)
        if (_online) { OnlineQuitOverlay(); return; }
        if (!_ended && Input.Pause && !_paused) { _paused = true; Audio.Paused = true; _pauseSel = 0; Audio.PlaySfx(Sfx.Select); }
        else if (_paused) PauseMenu();
    }

    static readonly string[] PauseItems = { "CONTINUAR", "REINICIAR", "SAIR DA MÚSICA" };
    static void Resume() { _paused = false; Audio.Paused = false; }

    static void Announce(string text, Color c) { _announce = text; _announceCol = c; _announceAt = _screenT; }

    static double BeatPhase(Chart c, double t)
    {
        if (t < 0) return 0;
        // find last beat before t
        var beats = c.Beats;
        int lo = 0, hi = beats.Count - 1;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (beats[mid].Time <= t) lo = mid; else hi = mid - 1; }
        double len = lo + 1 < beats.Count ? beats[lo + 1].Time - beats[lo].Time : 0.5;
        return lo + (t - beats[lo].Time) / len;
    }
    static double Frac(double x) => x - Math.Floor(x);

    // ================================================================= results
    static bool _newRecord;
    static int _stars;

    static void FinishSong()
    {
        Audio.Stop();
        var p = _player;
        _stars = p.Stars;
        _newRecord = false;
        if (!p.Failed && !_bot && _demoSong == null)
        {
            string k = Key(_song, _playDiff);
            Cfg.Best.TryGetValue(k, out int best);
            if (p.Score > best) { Cfg.Best[k] = (int)p.Score; _newRecord = true; }
            Cfg.BestStars.TryGetValue(k, out int bs);
            if (_stars > bs) Cfg.BestStars[k] = _stars;
            Cfg.Save();
        }
        if (_online) { SendDone(false); Go(Screen.OnlineResults); return; }
        Go(Screen.Results);
    }

    // ================================================================= settings
    // ================================================================= calibration
    static readonly List<double> _calHits = new();
    const double CalBeat = 0.6;
    static double _calT;

    static void StartCalibration()
    {
        _calHits.Clear();
        int len = (int)(Audio.Rate * 40);
        var l = new short[len]; var r = new short[len];
        for (double bt = 0; bt < 40; bt += CalBeat)
        {
            int st = (int)(bt * Audio.Rate);
            for (int i = 0; i < 2200 && st + i < len; i++)
            {
                double tt = i / (double)Audio.Rate;
                short v = (short)(Math.Sin(2 * Math.PI * 1500 * tt) * Math.Exp(-tt * 60) * 26000);
                l[st + i] = v; r[st + i] = v;
            }
        }
        Audio.Loop = false;
        Audio.SetSong(new[] { l, r }, new[] { -1f, 1f }, new[] { 1f, 1f }, new bool[2], -1.0);
        _calT = -1;
        Go(Screen.Calibrate);
    }

}

/// <summary>Autoplay used for automated testing of the rules and renderer.</summary>
public sealed class Bot
{
    double _lastStrum = -1;
    public void Drive(Player p, double t)
    {
        Note n = null;
        for (int i = p.Next; i < p.Notes.Count; i++) if (!p.Notes[i].Hit) { n = p.Notes[i]; break; }
        Note holding = null;
        for (int i = Math.Max(0, p.Next - 6); i < p.Next && i < p.Notes.Count; i++) if (p.Notes[i].Holding) holding = p.Notes[i];
        int mask = holding?.Mask ?? 0;
        bool strum = false;
        if (n != null && n.Time - t < 0.012)
        {
            mask = n.Mask;
            if (n.Time - t <= 0.004 && _lastStrum < n.Time) { strum = true; _lastStrum = n.Time; }
        }
        for (int i = 0; i < 5; i++)
        {
            bool down = (mask & (1 << i)) != 0;
            Input.FretPressed[i] = down && !Input.Fret[i];
            Input.FretReleased[i] = !down && Input.Fret[i];
            Input.Fret[i] = down;
        }
        Input.Strum = strum;
        Input.StarPower = p.Energy >= 0.5f && !p.StarActive;
        Input.Whammy = holding != null ? 0.5f + 0.5f * MathF.Sin((float)t * 12) : 0;
        Input.WhammyActive = holding != null;
    }
}
