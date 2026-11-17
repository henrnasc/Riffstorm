using System.Numerics;
using Raylib_cs;

namespace Riffstorm;

/// <summary>
/// Battle mode (online): item boxes ride on some notes (same song times for everyone). Hit one and a roulette gives you
/// a random item - better ones the further behind you are. Attacks go to the player just above you in the scoreboard
/// ("Raio" always hits the leader); the rest help yourself.
/// </summary>
public static partial class Game
{
    public enum Item
    {
        Inverter, Turbo, Neblina, Sumico, Corda, Cores, Bebada, Apagao, Fantasmas, LadraoEnergia, Trava, Raio,   // attacks
        Escudo, Espelho, Dobro, Protegida, Torcida, Ima,                                                          // for you
    }

    public static readonly string[] ItemNames =
    {
        "Pista invertida", "Turbo", "Neblina", "Sumiço", "Corda arrebentada", "Cores trocadas", "Pista bêbada", "Apagão",
        "Notas fantasmas", "Ladrão de energia", "Multiplicador travado", "Raio no líder",
        "Escudo", "Espelho", "Dobro", "Sequência protegida", "Torcida", "Ímã",
    };

    static readonly Color[] ItemColors =
    {
        new(150, 90, 230, 255), new(255, 140, 30, 255), new(170, 180, 190, 255), new(90, 90, 120, 255), new(200, 60, 50, 255),
        new(240, 90, 200, 255), new(120, 200, 80, 255), new(40, 40, 50, 255), new(180, 220, 255, 255), new(60, 160, 255, 255),
        new(130, 130, 140, 255), new(255, 230, 40, 255),
        new(80, 200, 255, 255), new(220, 230, 240, 255), new(255, 200, 60, 255), new(60, 210, 140, 255), new(255, 120, 120, 255), new(230, 60, 80, 255),
    };

    static bool IsAttack(Item i) => i <= Item.Raio;

    /// <summary>Seconds an effect lasts on its target.</summary>
    static double Duration(Item i) => i switch
    {
        Item.Inverter or Item.Turbo or Item.Neblina or Item.Sumico or Item.Cores or Item.Fantasmas or Item.Trava or Item.Dobro => 10,
        Item.Bebada => 8, Item.Apagao => 6, Item.Raio => 7, Item.Corda => 14, _ => 0,
    };

    // ------------------------------------------------------------------ state
    static bool _battle, _battleMode, _autoBattle, _autoItem;
    static int _testFx = -1;          // tests: apply this effect on yourself at the start
    static int _battleSeed;
    static Item? _slot;                       // the item you hold
    static double _rollUntil = -1;            // roulette spinning until (local clock)
    static Item _rollResult;
    static readonly Dictionary<Item, double> _fx = new();     // effects on you: until (song time)
    static bool _shield, _mirror;
    static int _brokenLane = -1, _repairs;
    static int[] _perm;
    static double _nextFake;
    static double _hwBaseSpeed;

    static void ResetBattle()
    {
        _slot = null; _rollUntil = -1; _fx.Clear(); _shield = _mirror = false; _brokenLane = -1; _repairs = 0; _perm = null; _nextFake = 0;
        _hw.Mirror = false; _hw.ColorPerm = null; _hw.HideNear = 0; _hw.Sway = 0; _hw.Fakes.Clear();
    }

    /// <summary>Item boxes every 18-28 s of the song (same times for everyone), on the first note at or after each time.</summary>
    static void PlaceBoxes(Player p, int seed)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("RIFF_BOXSEED"), out var fixedSeed)) seed = fixedSeed;   // tests
        var rng = new Random(seed);
        double end = p.Chart.LastNoteTime - 6;
        for (double bt = 12 + rng.NextDouble() * 6; bt < end; bt += 18 + rng.NextDouble() * 10)
        {
            var n = p.Notes.FirstOrDefault(x => x.Time >= bt);
            if (n != null) n.Box = true;
        }
        var log = Environment.GetEnvironmentVariable("RIFF_BOXLOG");      // tests: where the boxes landed
        if (log != null) File.WriteAllLines(log, p.Notes.Where(x => x.Box).Select(x => x.Time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)));
    }

    static bool Active(Item i) => _fx.TryGetValue(i, out var until) && _t < until;

    // ------------------------------------------------------------------ per frame (before the rules run)
    static void BattleInput(Player p)
    {
        // a broken string: that fret doesn't work until it's pressed 5 times
        if (_brokenLane >= 0)
        {
            if (Input.FretPressed[_brokenLane] && ++_repairs >= 5) { _brokenLane = -1; Feed("Corda consertada!", new Color(120, 220, 120, 255)); Audio.PlaySfx(Sfx.Select); }
            else if (!Active(Item.Corda)) _brokenLane = -1;
            if (_brokenLane >= 0) { Input.Fret[_brokenLane] = false; Input.FretPressed[_brokenLane] = false; }
        }
    }

    /// <summary>After the rules: boxes, the item button, effect timers and how they change the highway/player.</summary>
    static void BattleUpdate(Player p)
    {
        // item boxes you hit
        for (int i = Math.Max(0, p.Next - 8); i < p.Next && i < p.Notes.Count; i++)
        {
            var n = p.Notes[i];
            if (!n.Box || n.BoxTaken || !n.Hit) continue;
            n.BoxTaken = true;
            if (_slot == null && _rollUntil < 0) { _rollUntil = Raylib.GetTime() + 1.3; _rollResult = RollItem(); Audio.PlaySfx(Sfx.StarReady, 0.6f); }
        }
        if (_rollUntil > 0 && Raylib.GetTime() >= _rollUntil) { _rollUntil = -1; _slot = _rollResult; Audio.PlaySfx(Sfx.Select); }

        if (_slot is Item it && (Input.Hit(Act.UseItem) || _autoItem) && !_ended) UseItem(it, p);
        if (_testFx >= 0 && _t > 1) { var fx = _testFx; _testFx = -1; OnAttack(new Msg { T = "atk", Id = Net.MyId, To = Net.MyId, Diff = fx }); }

        // effects on you
        _hw.Mirror = Active(Item.Inverter);
        _hw.Speed = (float)(_hwBaseSpeed * (Active(Item.Turbo) || Active(Item.Raio) ? 1.5 : 1));
        _hw.HideNear = Active(Item.Sumico) ? 3.2f : 0;
        _hw.Sway = Active(Item.Bebada) ? 1 : 0;
        if (Active(Item.Cores)) _hw.ColorPerm ??= _perm; else _hw.ColorPerm = null;
        p.MultLock = Active(Item.Trava);
        p.MultDouble = Active(Item.Dobro);
        // decoy notes on lanes that are free at that moment
        if (Active(Item.Fantasmas) && _t >= _nextFake)
        {
            _nextFake = _t + 0.35 + Random.Shared.NextDouble() * 0.4;
            double at = _t + 2.2;
            int lane = Random.Shared.Next(5);
            bool busy = p.Notes.Skip(p.Next).TakeWhile(n => n.Time < at + 0.3).Any(n => Math.Abs(n.Time - at) < 0.25 && (n.Mask & (1 << lane)) != 0);
            if (!busy) _hw.Fakes.Add((lane, at));
        }
        _hw.Fakes.RemoveAll(f => f.time < _t - 0.3);
        if (p.ShieldUsed) { p.ShieldUsed = false; Feed("Sequência protegida! O erro não contou.", new Color(60, 210, 140, 255)); }
    }

    /// <summary>Item odds by position: the leader mostly gets small helps; the last place gets the strong stuff.</summary>
    static Item RollItem()
    {
        var order = Net.Players.Select(np => (np.Id, s: LiveScore(np))).OrderByDescending(x => x.s).ToList();
        int n = Math.Max(1, order.Count);
        int rank = Math.Max(0, order.FindIndex(x => x.Id == Net.MyId));
        float behind = n == 1 ? 0.5f : rank / (float)(n - 1);              // 0 = leader, 1 = last
        var w = new float[ItemNames.Length];
        for (int i = 0; i < w.Length; i++) w[i] = 1;
        foreach (var a in new[] { Item.Inverter, Item.Turbo, Item.Neblina, Item.Sumico, Item.Corda, Item.Cores, Item.Bebada, Item.Apagao, Item.Fantasmas, Item.Trava })
            w[(int)a] = 0.6f + behind * 1.4f;
        w[(int)Item.Raio] = behind < 0.5f ? 0 : (behind - 0.4f) * 3;
        w[(int)Item.Ima] = rank == 0 ? 0 : 0.6f + behind;
        w[(int)Item.LadraoEnergia] = 0.5f + behind;
        w[(int)Item.Escudo] = 1.6f - behind * 0.8f;
        w[(int)Item.Espelho] = 1.2f - behind * 0.6f;
        w[(int)Item.Dobro] = 0.8f + behind * 0.8f;
        w[(int)Item.Protegida] = 1.2f;
        w[(int)Item.Torcida] = _player != null && _player.Meter < 0.4f ? 2.5f : 0.6f;
        if (n == 1) { w[(int)Item.Raio] = 0; w[(int)Item.Ima] = 0; }        // alone in the room: nothing to attack
        float r = (float)Random.Shared.NextDouble() * w.Sum();
        for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0) return (Item)i; }
        return Item.Escudo;
    }

    /// <summary>Target of an attack: the player just above you (the 2nd place when you lead); Raio: the leader.</summary>
    static int TargetFor(Item it)
    {
        var order = Net.Players.Select(np => (np.Id, s: LiveScore(np))).OrderByDescending(x => x.s).ToList();
        int me = order.FindIndex(x => x.Id == Net.MyId);
        if (order.Count < 2) return -1;
        if (it == Item.Raio) return order[0].Id != Net.MyId ? order[0].Id : order[1].Id;
        return me > 0 ? order[me - 1].Id : order[1].Id;
    }

    static void UseItem(Item it, Player p)
    {
        _slot = null;
        Audio.PlaySfx(Sfx.StarDeploy, 0.7f);
        if (IsAttack(it) || it == Item.Ima)
        {
            int to = TargetFor(it);
            if (to < 0) return;
            int amount = 0;
            if (it == Item.Ima) amount = (int)Math.Min(4000, LiveScore(Net.Players.First(x => x.Id == to)) * 0.06);
            Net.Send(new Msg { T = "atk", Diff = (int)it, To = to, Amount = amount });
            Feed($"Você usou {ItemNames[(int)it]} em {NameOf(to)}!", ItemColors[(int)it]);
            return;
        }
        Feed($"Você usou {ItemNames[(int)it]}!", ItemColors[(int)it]);
        switch (it)
        {
            case Item.Escudo: _shield = true; break;
            case Item.Espelho: _mirror = true; break;
            case Item.Dobro: _fx[Item.Dobro] = _t + Duration(Item.Dobro); break;
            case Item.Protegida: p.StreakShield = true; break;
            case Item.Torcida: if (!p.Failed) p.Meter = 1; break;
        }
    }

    /// <summary>An item someone used (relayed to everyone; only the target applies it).</summary>
    static void OnAttack(Msg m)
    {
        var it = (Item)Math.Clamp(m.Diff, 0, ItemNames.Length - 1);
        bool reflected = m.Ready;
        if (m.To != Net.MyId)
        {
            Feed($"{NameOf(m.Id)} usou {ItemNames[(int)it]} em {NameOf(m.To)}", ItemColors[(int)it]);
            return;
        }
        var p = _player;
        if (p == null || _screen != Screen.Play) return;
        if (_shield) { _shield = false; Feed($"Seu escudo bloqueou {ItemNames[(int)it]} de {NameOf(m.Id)}!", ItemColors[(int)Item.Escudo]); return; }
        if (_mirror && !reflected)
        {
            _mirror = false;
            Net.Send(new Msg { T = "atk", Diff = (int)it, To = m.Id, Ready = true, Amount = m.Amount });
            Feed($"Seu espelho devolveu {ItemNames[(int)it]} para {NameOf(m.Id)}!", ItemColors[(int)Item.Espelho]);
            return;
        }
        Audio.PlaySfx(Sfx.Miss, 0.7f);
        Announce(ItemNames[(int)it].ToUpperInvariant() + "!", ItemColors[(int)it]);
        Feed($"{NameOf(m.Id)} usou {ItemNames[(int)it]} em você!", ItemColors[(int)it]);
        switch (it)
        {
            case Item.LadraoEnergia: p.Energy *= 0.5f; break;
            case Item.Ima:
            {
                int take = (int)Math.Min(p.Score, Math.Max(0, m.Amount));
                p.Score -= take;
                Net.Send(new Msg { T = "gift", To = m.Id, Amount = take });
                break;
            }
            case Item.Corda: _brokenLane = Random.Shared.Next(5); _repairs = 0; _fx[it] = _t + Duration(it); break;
            case Item.Cores:
            {
                var perm = Enumerable.Range(0, 5).ToArray();
                while (perm.Select((v, k) => v == k).Any(b => b)) perm = perm.OrderBy(_ => Random.Shared.Next()).ToArray();
                _perm = perm; _hw.ColorPerm = null; _fx[it] = _t + Duration(it);
                break;
            }
            case Item.Raio: _fx[Item.Raio] = _t + Duration(it); _fx[Item.Neblina] = _t + Duration(it); break;
            default: _fx[it] = _t + Duration(it); break;
        }
    }

    static void OnGift(Msg m)
    {
        if (m.To != Net.MyId || _player == null) return;
        _player.Score += m.Amount;
        Feed($"Ímã: você pegou {m.Amount:N0} pontos de {NameOf(m.Id)}!".Replace(',', '.'), ItemColors[(int)Item.Ima]);
    }

    // ------------------------------------------------------------------ drawing
    /// <summary>Screen effects drawn over the highway (fog, blackout).</summary>
    static void DrawBattleOverlays(double t)
    {
        if (Active(Item.Neblina))
        {
            int h = (int)(Gfx.H * 0.62f);
            Raylib.DrawRectangleGradientV(0, 0, Gfx.W, h, new Color(170, 175, 185, 250), new Color(170, 175, 185, 0));
            for (int i = 0; i < 6; i++)
            {
                float x = (float)((t * (30 + i * 9) + i * 211) % (Gfx.W + 400)) - 200;
                Raylib.DrawCircleGradient(new Vector2(x, h * (0.3f + 0.1f * i)), 160 * Gfx.S, new Color(200, 205, 215, 140), new Color(200, 205, 215, 0));
            }
        }
        if (Active(Item.Apagao) && (t * 2.2 % 1) < 0.45)
            Raylib.DrawRectangle(0, 0, Gfx.W, Gfx.H, new Color(0, 0, 0, 240));
        if (_brokenLane >= 0)
        {
            string s = $"CORDA ARREBENTADA! Aperte o traste {Binds.Names[_brokenLane].ToUpperInvariant()} {5 - _repairs}x para consertar";
            Gfx.Shadowed(Gfx.Bold, s, Gfx.W / 2f, Gfx.H * 0.6f, 20, new Color(255, 170, 160, 255), 0.5f);
        }
    }

    /// <summary>Your item slot (with the roulette) and the effects on you, on the left side.</summary>
    static void DrawItemHud(double t)
    {
        float s = Gfx.S;
        var r = new Rectangle(28 * s, Gfx.H * 0.37f, 132 * s, 132 * s);
        Raylib.DrawRectangleRounded(new Rectangle(r.X + 3 * s, r.Y + 4 * s, r.Width, r.Height), 0.2f, 8, new Color(0, 0, 0, 120));
        Raylib.DrawRectangleRounded(r, 0.2f, 8, new Color(20, 16, 14, 215));
        Raylib.DrawRectangleRoundedLinesEx(r, 0.2f, 8, 3 * s, _slot != null ? Gfx.Lerp(new Color(255, 220, 90, 255), Color.White, (float)(0.5 + 0.5 * Math.Sin(t * 6))) : new Color(120, 110, 100, 255));
        Item? show = _slot;
        if (_rollUntil > 0) show = (Item)((int)(Raylib.GetTime() * 14) % ItemNames.Length);
        var c = new Vector2(r.X + r.Width / 2, r.Y + r.Height * 0.42f);
        if (show is Item it)
        {
            var col = ItemColors[(int)it];
            Raylib.DrawCircleV(c, 38 * s, Gfx.Bright(col, 0.5f));
            Raylib.DrawCircleGradient(c - new Vector2(6, 8) * s, 34 * s, Gfx.Bright(col, 1.3f), col);
            Gfx.Text(Gfx.Title, IsAttack(it) ? "!" : "+", c.X, c.Y - 26 * s, 40, Color.White, 0.5f);
            string nm = ItemNames[(int)it];
            float fs = 15; while (fs > 10 && Raylib.MeasureTextEx(Gfx.Bold, nm, fs * s, 0).X > r.Width - 10 * s) fs--;
            Gfx.Shadowed(Gfx.Bold, nm, c.X, r.Y + r.Height - 34 * s, fs, Color.White, 0.5f);
        }
        else
        {
            Gfx.Text(Gfx.Title, "?", c.X, c.Y - 30 * s, 52, new Color(120, 110, 100, 255), 0.5f);
            Gfx.Text(Gfx.Body, "Acerte uma caixa", c.X, r.Y + r.Height - 34 * s, 13, new Color(170, 160, 150, 255), 0.5f);
        }
        string key = Binds.KeyName(Cfg.KeyBinds[(int)Act.UseItem]), pad = Binds.PadName(Cfg.PadBinds[(int)Act.UseItem]);
        Gfx.Text(Gfx.Body, $"usar: {key} / {pad}", c.X, r.Y + r.Height - 16 * s, 12, new Color(220, 210, 195, 220), 0.5f);

        // effects on you + shields
        float y = r.Y + r.Height + 10 * s;
        void Line(string txt, Color col, float k)
        {
            var m = Raylib.MeasureTextEx(Gfx.Bold, txt, 14 * s, 0);
            Raylib.DrawRectangleRounded(new Rectangle(r.X, y, Math.Max(r.Width, m.X + 16 * s), 24 * s), 0.4f, 6, new Color(15, 12, 10, 190));
            if (k > 0) Raylib.DrawRectangle((int)r.X, (int)(y + 21 * s), (int)(r.Width * k), (int)(3 * s), col);
            Raylib.DrawTextEx(Gfx.Bold, txt, new Vector2(r.X + 8 * s, y + 4 * s), 14 * s, 0, col);
            y += 28 * s;
        }
        if (_shield) Line("Escudo pronto", ItemColors[(int)Item.Escudo], 0);
        if (_mirror) Line("Espelho pronto", ItemColors[(int)Item.Espelho], 0);
        if (_player?.StreakShield == true) Line("Sequência protegida", ItemColors[(int)Item.Protegida], 0);
        foreach (var (it2, until) in _fx)
            if (_t < until) Line(ItemNames[(int)it2], ItemColors[(int)it2], (float)Math.Clamp((until - _t) / Duration(it2), 0, 1));
    }
}
