using System.Text;

namespace Riffstorm;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest") return SelfTest(args.Length > 1 ? args[1] : null);
        if (args.Length > 1 && args[0] == "--import") return ImportCli(args[1], args.Length > 2 ? args[2] : null);
        try
        {
            Game.Run(args);
            return 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), e.ToString());
            throw;
        }
    }

    /// <summary>Headless import: Riffstorm.exe --import &lt;iso or disc folder&gt; [library folder]</summary>
    static int ImportCli(string disc, string lib)
    {
        var cfg = Settings.Load();
        lib ??= cfg.LibraryPath ?? SongDb.DefaultRoot;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string sha;
        using (var fs = File.OpenRead(disc)) sha = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(fs)).ToLowerInvariant();
        var kg = Importer.BySha1(sha);
        var pr = new Importer.Progress();
        if (kg == null) pr.Error = "disco não compatível (SHA-1 " + sha + ")";
        else Importer.Import(disc, lib, kg, pr);
        var msg = pr.Error != null ? "ERRO: " + pr.Error : $"{pr.GameName}: {pr.Imported} músicas importadas em {sw.Elapsed.TotalSeconds:0.0}s -> {lib}";
        Console.WriteLine(msg);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "import.log"), msg);
        if (pr.Error == null) { cfg.LibraryPath = Path.GetFullPath(lib); cfg.Save(); }
        return pr.Error == null ? 0 : 1;
    }

    static int SelfTest(string lib)
    {
        var log = new StringBuilder();
        lib ??= Settings.Load().LibraryPath ?? SongDb.DefaultRoot;
        log.AppendLine("biblioteca: " + lib);
        SongDb.Load(lib);
        foreach (var g in SongDb.Games)
        {
            log.AppendLine($"jogo {g.Id} ({g.Name}, {g.Serial}): {g.TierSongs.Sum(t => t.Count)} na carreira, {g.BonusSongs.Count} bônus");
            for (int t = 0; t < g.TierSongs.Count; t++) log.AppendLine($"  {g.Tiers[t].Name}: " + string.Join(", ", g.TierSongs[t].Select(s => s.Name)));
        }
        log.AppendLine($"songs: {SongDb.Songs.Count}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int bad = 0;
        foreach (var s in SongDb.Songs)
        {
            try
            {
                var c = Chart.Parse(File.ReadAllBytes(s.ChartPath));
                log.AppendLine($"{s.Id,-26} {s.Name,-34} {s.Artist,-22} notes E/M/H/X={string.Join("/", c.Notes.Select(n => n.Count))} hopo={c.Notes[3].Count(n => n.Hopo)} sus={c.Notes[3].Count(n => n.Sustain)} sp={c.PhraseCount[3]} last={c.LastNoteTime:0.0}s");
            }
            catch (Exception e) { bad++; log.AppendLine("FAIL " + s.Id + " " + e.Message); }
        }
        // rules test: bot plays every chart at a simulated 120 fps
        var settings = new Settings();
        foreach (var s in SongDb.Songs)
        {
            var c = Chart.Parse(File.ReadAllBytes(s.ChartPath));
            var line = new StringBuilder($"bot {s.Id,-26}");
            for (int d = 0; d < 4; d++)
            {
                var p = new Player(Chart.Parse(File.ReadAllBytes(s.ChartPath)), d);
                var bot = new Bot();
                double dt = 1 / 120.0;
                bool everStar = false;
                for (double t = -1; t < p.Chart.LastNoteTime + 1; t += dt)
                {
                    bot.Drive(p, t);
                    bool ch = false; for (int i = 0; i < 5; i++) ch |= Input.FretPressed[i] || Input.FretReleased[i];
                    p.Update(t, dt, Input.Strum, ch, Player.Held(), Input.Whammy, Input.StarPower, false);
                    everStar |= p.StarActive;
                    p.HitFx.Clear(); p.MissFx.Clear();
                }
                line.Append($" {Chart.DiffNames[d]}:{p.NotesHit}/{p.NotesTotal} ovs={p.OverstrumCount} {p.Score} {p.Stars}*{(everStar ? " SP" : "")}");
                if (p.NotesHit != p.NotesTotal || p.OverstrumCount > 0) bad++;
            }
            log.AppendLine(line.ToString());
        }

        // auto-strum test: press frets only (no strum), ±40 ms human jitter, release between notes
        var rng = new Random(1);
        foreach (var s in SongDb.Songs)
        {
            var line = new StringBuilder($"tap {s.Id,-26}");
            for (int d = 0; d < 4; d++)
            {
                var p = new Player(Chart.Parse(File.ReadAllBytes(s.ChartPath)), d);
                var press = p.Notes.Select(n => (n, at: n.Time + (rng.NextDouble() - 0.5) * 0.08)).ToList();
                int k = 0; int mask = 0; double releaseAt = 0;
                double dt = 1 / 120.0;
                for (double t = -1; t < p.Chart.LastNoteTime + 1; t += dt)
                {
                    int newMask = mask;
                    if (mask != 0 && t >= releaseAt) newMask = 0;
                    bool pressNow = k < press.Count && t >= press[k].at;
                    if (pressNow)
                    {
                        // release first if the same fret must be re-pressed
                        if ((mask & press[k].n.Mask) != 0 && mask != 0) { newMask = 0; }
                        else { newMask = press[k].n.Mask; releaseAt = press[k].n.Sustain ? press[k].n.EndTime : t + 0.05; k++; }
                    }
                    bool anyPress = false, ch = false;
                    for (int i = 0; i < 5; i++)
                    {
                        bool down = (newMask & (1 << i)) != 0;
                        Input.FretPressed[i] = down && !Input.Fret[i];
                        Input.FretReleased[i] = !down && Input.Fret[i];
                        Input.Fret[i] = down;
                        anyPress |= Input.FretPressed[i]; ch |= Input.FretPressed[i] || Input.FretReleased[i];
                    }
                    mask = newMask;
                    p.Update(t, dt, false, ch, Player.Held(), 0, false, true, true, anyPress);
                    p.HitFx.Clear(); p.MissFx.Clear();
                }
                double pct = 100.0 * p.NotesHit / p.NotesTotal;
                line.Append($" {Chart.DiffNames[d]}:{pct:0.0}%");
                if (pct < 97) bad++;
            }
            log.AppendLine(line.ToString());
        }

        // whammy DSP test: 440 Hz tone, measure instantaneous frequency range with and without whammy
        foreach (float target in new[] { 0f, 1f })
        {
            Audio.WhammyTarget = target;
            int n = Audio.Rate * 3; var y = new float[n];
            for (int i = 0; i < n; i++) { float l = MathF.Sin(2 * MathF.PI * 440 * i / Audio.Rate), r = l; Audio.ProcessWhammy(ref l, ref r); y[i] = l; }
            var cross = new List<double>();
            for (int i = Audio.Rate * 2; i < n - 1; i++) if (y[i] <= 0 && y[i + 1] > 0) cross.Add(i + y[i] / (y[i] - y[i + 1]));
            var f = new List<double>(); for (int i = 1; i < cross.Count; i++) f.Add(Audio.Rate / (cross[i] - cross[i - 1]));
            log.AppendLine($"whammy={target}: freq min {f.Min():0.0} Hz  max {f.Max():0.0} Hz  ({12 * Math.Log2(f.Max() / f.Min()):0.00} semitons)");
        }
        Audio.WhammyTarget = 0;

        var song = SongDb.Songs.First(s => s.Id == "ironman");
        sw.Restart();
        var pcm = SongAudio.Load(song); int rate = Audio.Rate;
        log.AppendLine($"decode ironman: {pcm.Length}ch {rate}Hz {pcm[0].Length / (double)rate:0.0}s in {sw.ElapsedMilliseconds}ms");
        for (int c = 0; c < pcm.Length; c++)
        {
            double rms = 0; int peak = 0;
            for (int i = 0; i < pcm[c].Length; i += 7) { rms += pcm[c][i] * (double)pcm[c][i]; peak = Math.Max(peak, Math.Abs((int)pcm[c][i])); }
            log.AppendLine($"  ch{c} rms={Math.Sqrt(rms / (pcm[c].Length / 7)):0} peak={peak}");
        }
        // dump 20s of the mix as WAV for listening checks
        string wav = Path.Combine(AppContext.BaseDirectory, "selftest_ironman.wav");
        using (var bw = new BinaryWriter(File.Create(wav)))
        {
            int n = Math.Min(pcm[0].Length, rate * 20), off = rate * 10;
            bw.Write("RIFF"u8); bw.Write(36 + n * 4); bw.Write("WAVEfmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)2);
            bw.Write(rate); bw.Write(rate * 4); bw.Write((short)4); bw.Write((short)16); bw.Write("data"u8); bw.Write(n * 4);
            for (int i = off; i < off + n; i++)
            {
                bw.Write((short)Math.Clamp((pcm[0][i] + pcm[2][i]) / 2, -32768, 32767));
                bw.Write((short)Math.Clamp((pcm[1][i] + pcm[3][i]) / 2, -32768, 32767));
            }
        }
        log.AppendLine("wav: " + wav);
        string outp = Path.Combine(AppContext.BaseDirectory, "selftest.txt");
        File.WriteAllText(outp, log.ToString());
        Console.WriteLine(log);
        return bad;
    }
}
