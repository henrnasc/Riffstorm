using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Riffstorm;

/// <summary>
/// One-time import of songs + charts from the player's OWN disc into the local library.
/// After importing, the game never needs the disc again. Each supported game has its own reader.
/// </summary>
public static class Importer
{
    public sealed record KnownGame(string Id, string Name, string Platform, string Serial, long Size, string Sha1, bool Supported);

    /// <summary>
    /// The exact disc images this project is built against. A disc is only accepted when its SHA-1 matches
    /// (byte-for-byte the same dump), so every player gets identical songs and charts.
    /// Readers are added game by game (Supported = true).
    /// </summary>
    public static readonly KnownGame[] Known =
    {
        new("gh1", "Guitar Hero", "PS2", "SLUS_212.24", 2300313600, "4caacaea582c79605f8c3582dd4b90abdec699f5", true),
        new("gh2", "Guitar Hero II", "PS2", "SLUS_214.47", 4646305792, "e1a3a81827aa4264984b4f3678de58ca3d881528", true),
        new("gh80s", "Guitar Hero Encore: Rocks the 80s", "PS2", "SLUS_215.86", 1901821952, "184f2dc9993d9b1e6edf0125f68e1ce07742567a", true),
        new("gh3", "Guitar Hero III: Legends of Rock", "PS3", null, 6318391296, "a1fe34e1895aab21e7da0dca62bb49edd1f6dffa", true),
    };

    public static KnownGame BySha1(string sha1) => Known.FirstOrDefault(k => k.Sha1 == sha1);
    public static KnownGame BySize(long size) => Known.FirstOrDefault(k => k.Size == size);

    public sealed class Progress
    {
        public volatile int Done, Total;
        public volatile string Current = "";
        public volatile string Error;
        public volatile bool Finished;
        public string GameName = "";
        public int Imported;
    }

    public static string Serial(string discPath)
    {
        var cnf = Disc.ReadSystemCnf(discPath) ?? throw new InvalidDataException("SYSTEM.CNF não encontrado – não parece um disco de PS2.");
        var m = Regex.Match(cnf, @"cdrom0:\\([A-Z]{4}_\d{3}\.\d{2})", RegexOptions.IgnoreCase);
        if (!m.Success) throw new InvalidDataException("Não consegui ler o código do disco.");
        return m.Groups[1].Value.ToUpperInvariant();
    }

    /// <summary>Imports an already verified disc (blocking).</summary>
    public static void Import(string discPath, string libraryRoot, KnownGame kg, Progress pr)
    {
        try
        {
            pr.GameName = kg.Name;
            if (!kg.Supported) throw new InvalidDataException($"{kg.Name} foi reconhecido, mas a importação dele ainda não foi implementada.");
            switch (kg.Id)
            {
                case "gh1": ImportHarmonixArk(discPath, libraryRoot, kg, pr, TierNames, false); break;
                case "gh2":
                case "gh80s": ImportHarmonixArk(discPath, libraryRoot, kg, pr, TierNames, true); break;
                case "gh3": NeversoftImport.Import(discPath, libraryRoot, kg, pr); break;
            }
        }
        catch (Exception e) { pr.Error = e.Message; }
        finally { pr.Finished = true; }
    }

    /// <summary>True when this exact disc (same SHA-1) is already in the library.</summary>
    public static bool IsImported(string libraryRoot, KnownGame kg)
    {
        var f = Path.Combine(libraryRoot, kg.Id, "game.json");
        if (!File.Exists(f)) return false;
        try { return JsonSerializer.Deserialize<GameEntry>(File.ReadAllText(f), SongDb.Json)?.SourceSha1 == kg.Sha1; } catch { return false; }
    }

    // Our own names for the career tiers (the order of the songs comes from the disc).
    static readonly string[] TierNames = { "Garagem do Bairro", "Bar Underground", "Clube da Esquina", "Casa de Shows", "Teatro Municipal", "Festival ao Ar Livre", "Estádio Lotado", "Palco Lendário" };

    /// <summary>
    /// Harmonix PS2 format (GH1): config/gen/songs.dtb (metadata, channel mix), config/gen/campaign.dtb (career order),
    /// songs/&lt;id&gt;/&lt;id&gt;.vgs (multitrack PS-ADPCM audio, copied as-is) and the .mid chart.
    /// </summary>
    static void ImportHarmonixArk(string discPath, string libraryRoot, KnownGame kg, Progress pr, string[] tierNames, bool volsInDb)
    {
        using var disc = Disc.Open(discPath);
        var root = Dtb.Load(disc.Read("config/gen/songs.dtb"));
        var songs = new List<(Song s, string vgs, string mid)>();
        foreach (var a in root.Arrays())
        {
            string id = a.Sym(0);
            if (id == null || id.StartsWith("tutorial") || id.StartsWith("budget")) continue;
            var s = new Song { Id = id, Game = kg.Id, Name = a.Find("name")?.Sym(1) ?? id, Artist = a.Find("artist")?.Sym(1) ?? "" };
            var song = a.Find("song");
            string basePath = song?.Find("name")?.Sym(1) ?? $"songs/{id}/{id}";
            string vgs = basePath + ".vgs", mid = a.Find("midi_file")?.Sym(1) ?? basePath + ".mid";
            if (!disc.Has(vgs) || !disc.Has(mid)) continue;
            float[] Floats(string k)
            {
                var arr = song?.Find(k)?.Arr(1);
                if (arr == null) return Array.Empty<float>();
                var f = new float[arr.Count]; for (int i = 0; i < f.Length; i++) f[i] = arr.Num(i); return f;
            }
            s.Pans = Floats("pans"); s.Vols = Floats("vols");
            if (volsInDb) s.Vols = s.Vols.Select(db => MathF.Pow(10, db / 20f)).ToArray();
            s.Guitar = Floats("cores").Select(x => x > 0).ToArray();
            // GH2-era songs name their stems: (tracks ((guitar (2 3)) (bass 4)))
            var tracks = song?.Find("tracks")?.Arr(1);
            var gtr = tracks?.Find("guitar");
            if (gtr != null)
            {
                var idx = new List<int>();
                if (gtr[1] is int one) idx.Add(one);
                else if (gtr.Arr(1) is DtbArray many) for (int i = 0; i < many.Count; i++) idx.Add((int)many.Num(i));
                int n = Math.Max(s.Pans.Length, idx.Count == 0 ? 0 : idx.Max() + 1);
                s.Guitar = Enumerable.Range(0, n).Select(i => idx.Contains(i)).ToArray();
            }
            if (s.Guitar.Length == 0) s.Guitar = new[] { false, false, true, true };
            var pv = a.Find("preview");
            if (pv != null && pv.Count >= 3) { s.PreviewStart = pv.Num(1) / 1000.0; s.PreviewEnd = pv.Num(2) / 1000.0; }
            var bpm = a.Find("bpm"); if (bpm != null) s.Bpm = (int)bpm.Num(1);
            songs.Add((s, vgs, mid));
        }

        var game = new GameEntry { Id = kg.Id, Name = kg.Name, Serial = kg.Serial, SourceSha1 = kg.Sha1, Imported = DateTime.Now.ToString("yyyy-MM-dd HH:mm") };
        var order = Dtb.Load(disc.Read("config/gen/campaign.dtb")).Find("order");
        var inCareer = new HashSet<string>();
        if (order != null)
        {
            int t = 0;
            foreach (var tier in order.Arrays())
            {
                var te = new TierEntry { Name = t < tierNames.Length ? tierNames[t] : $"Nível {t + 1}" };
                for (int i = 1; i < tier.Count; i++)
                    if (songs.Any(x => x.s.Id == tier.Sym(i))) { te.Songs.Add(tier.Sym(i)); inCareer.Add(tier.Sym(i)); }
                game.Tiers.Add(te); t++;
            }
        }
        game.Bonus = songs.Where(x => !inCareer.Contains(x.s.Id)).Select(x => x.s).OrderBy(s => s.Name).Select(s => s.Id).ToList();

        var gdir = Path.Combine(libraryRoot, kg.Id);
        Directory.CreateDirectory(gdir);
        pr.Total = songs.Count;
        foreach (var (s, vgs, mid) in songs)
        {
            pr.Current = s.Name;
            var sdir = Path.Combine(gdir, s.Id);
            Directory.CreateDirectory(sdir);
            var chart = disc.Read(mid);
            Riffstorm.Chart.Parse(chart);                       // validate before writing
            File.WriteAllBytes(Path.Combine(sdir, s.Chart), chart);
            CopyOut(disc, vgs, Path.Combine(sdir, s.Audio));
            s.ChartHash = Convert.ToHexString(SHA1.HashData(chart))[..16].ToLowerInvariant();
            File.WriteAllText(Path.Combine(sdir, "song.json"), JsonSerializer.Serialize(s, SongDb.Json));
            pr.Done++; pr.Imported++;
        }
        File.WriteAllText(Path.Combine(gdir, "game.json"), JsonSerializer.Serialize(game, SongDb.Json));
    }

    static void CopyOut(Disc disc, string name, string dst)
    {
        int size = disc.Size(name);
        using var o = new FileStream(dst + ".part", FileMode.Create, FileAccess.Write);
        const int chunk = 4 << 20;
        for (long off = 0; off < size; off += chunk) o.Write(disc.Read(name, off, chunk));
        o.Close();
        File.Move(dst + ".part", dst, true);
    }
}
