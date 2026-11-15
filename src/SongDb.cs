using System.Text.Json;
using System.Text.Json.Serialization;

namespace Riffstorm;

/// <summary>A song in the local library (imported from the player's own disc).</summary>
public sealed class Song
{
    public string Id { get; set; }            // id inside its game (e.g. "ironman")
    public string Game { get; set; }          // game id (e.g. "gh1")
    public string Name { get; set; }
    public string Artist { get; set; }
    public float[] Pans { get; set; } = Array.Empty<float>();
    public float[] Vols { get; set; } = Array.Empty<float>();
    public bool[] Guitar { get; set; } = Array.Empty<bool>();
    public double PreviewStart { get; set; } = 30;
    public double PreviewEnd { get; set; } = 60;
    public int Bpm { get; set; }
    public string Audio { get; set; } = "audio.vgs";      // single multitrack VGS (Harmonix discs)
    public string[] Stems { get; set; }                     // or separate MP3 stems, two channels each (GH3)
    public string Preview { get; set; }                     // optional ready-made preview clip
    public string Chart { get; set; } = "notes.mid";
    public string ChartHash { get; set; }     // identifies the exact chart (used to match players online)

    [JsonIgnore] public string Dir;
    [JsonIgnore] public int Tier = -1;        // index in its game's career, -1 = bonus
    [JsonIgnore] public string Key => Game + "/" + Id;
    [JsonIgnore] public string AudioPath => Audio == null ? null : Path.Combine(Dir, Audio);
    [JsonIgnore] public string ChartPath => Path.Combine(Dir, Chart);
}

/// <summary>One imported game: career tiers in the original order + bonus songs.</summary>
public sealed class GameEntry
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Serial { get; set; }
    public string SourceSha1 { get; set; }
    public string Imported { get; set; }
    public List<TierEntry> Tiers { get; set; } = new();
    public List<string> Bonus { get; set; } = new();

    [JsonIgnore] public List<List<Song>> TierSongs = new();
    [JsonIgnore] public List<Song> BonusSongs = new();
}

public sealed class TierEntry
{
    public string Name { get; set; }
    public List<string> Songs { get; set; } = new();
}

/// <summary>
/// The song library: &lt;root&gt;/&lt;game&gt;/game.json and &lt;root&gt;/&lt;game&gt;/&lt;song&gt;/song.json + audio + chart.
/// The game plays exclusively from here; discs are only needed once, to import.
/// </summary>
public static class SongDb
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static readonly List<Song> Songs = new();
    public static readonly List<GameEntry> Games = new();

    // kept outside the game folder on purpose: sharing the game folder never carries anyone's songs
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Riffstorm", "biblioteca");

    public static void Load(string root)
    {
        Songs.Clear(); Games.Clear();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
        foreach (var gdir in Directory.GetDirectories(root).OrderBy(d => d))
        {
            var gfile = Path.Combine(gdir, "game.json");
            if (!File.Exists(gfile)) continue;
            GameEntry g;
            try { g = JsonSerializer.Deserialize<GameEntry>(File.ReadAllText(gfile), Json); } catch { continue; }
            if (g == null) continue;
            var byId = new Dictionary<string, Song>();
            foreach (var sdir in Directory.GetDirectories(gdir))
            {
                var sfile = Path.Combine(sdir, "song.json");
                if (!File.Exists(sfile)) continue;
                try
                {
                    var s = JsonSerializer.Deserialize<Song>(File.ReadAllText(sfile), Json);
                    if (s == null) continue;
                    s.Dir = sdir; s.Game = g.Id;
                    bool audioOk = s.Stems is { Length: > 0 } ? s.Stems.All(x => File.Exists(Path.Combine(sdir, x))) : s.Audio != null && File.Exists(s.AudioPath);
                    if (!audioOk || !File.Exists(s.ChartPath)) continue;
                    byId[s.Id] = s;
                }
                catch { }
            }
            for (int t = 0; t < g.Tiers.Count; t++)
            {
                var list = new List<Song>();
                foreach (var id in g.Tiers[t].Songs)
                    if (byId.TryGetValue(id, out var s)) { s.Tier = t; list.Add(s); }
                g.TierSongs.Add(list);
            }
            foreach (var id in g.Bonus) if (byId.TryGetValue(id, out var s) && s.Tier < 0) g.BonusSongs.Add(s);
            // songs present on disk but not listed anywhere still show up as bonus
            foreach (var s in byId.Values.Where(s => s.Tier < 0 && !g.BonusSongs.Contains(s)).OrderBy(s => s.Name)) g.BonusSongs.Add(s);
            Games.Add(g);
            Songs.AddRange(g.TierSongs.SelectMany(x => x));
            Songs.AddRange(g.BonusSongs);
        }
        // release order (GH1, GH2, Encore 80s, GH3), unknown games last
        int Order(GameEntry g) { int i = Importer.Known.ToList().FindIndex(k => k.Id == g.Id); return i < 0 ? 99 : i; }
        var sorted = Games.OrderBy(Order).ThenBy(g => g.Id).ToList();
        Games.Clear(); Games.AddRange(sorted);
    }
}

public sealed class Settings
{
    public string LibraryPath { get; set; }
    public string IsoFolder { get; set; }          // folder watched for disc images (default: an "ISOs" folder next to the game)
    public Dictionary<string, string> IsoHashes { get; set; } = new();   // "path|size|mtime" -> sha1 cache
    public int AudioOffsetMs { get; set; } = 0;     // + = notes later
    public int VideoOffsetMs { get; set; } = 0;
    public float NoteSpeed { get; set; } = 1.0f;
    public bool Lefty { get; set; }
    public bool NoFail { get; set; }
    public bool AutoStrum { get; set; } = true;
    public int Controller { get; set; } = 0;        // 0 = joypad (estilo PS2), 1 = guitarra
    public float MusicVol { get; set; } = 0.9f;
    public float SfxVol { get; set; } = 0.7f;
    public bool Fullscreen { get; set; }
    public string PlayerName { get; set; } = "Jogador";
    public int LastDifficulty { get; set; } = 1;
    public Dictionary<string, int> Best { get; set; } = new();
    public Dictionary<string, int> BestStars { get; set; } = new();

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Riffstorm");
    static string FilePath => Path.Combine(Dir, "save.json");

    public static Settings Load()
    {
        Settings s = null;
        try { if (File.Exists(FilePath)) s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)); }
        catch { }
        s ??= new Settings();
        // records saved before the library existed were GH1-only ("ironman#2") -> "gh1/ironman#2"
        foreach (var dict in new[] { s.Best, s.BestStars })
            foreach (var k in dict.Keys.Where(k => !k.Contains('/')).ToList())
            {
                if (!dict.ContainsKey("gh1/" + k)) dict["gh1/" + k] = dict[k];
                dict.Remove(k);
            }
        return s;
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
