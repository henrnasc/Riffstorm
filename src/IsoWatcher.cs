using System.Diagnostics;
using System.Security.Cryptography;

namespace Riffstorm;

/// <summary>
/// Watches the "ISOs" folder in the background. Every disc image found is checked against the project's
/// compatibility table by SHA-1 (cached), and compatible, supported discs are imported into the library
/// while the player keeps playing. Nothing is ever downloaded.
/// </summary>
public static class IsoWatcher
{
    public enum State { Checking, Compatible, Importing, Imported, NotYetSupported, Incompatible, Error }

    public sealed class Entry
    {
        public string Path, File;
        public long Size;
        public Importer.KnownGame Game;
        public State State;
        public string Detail = "";
        public float Progress;
    }

    static readonly object Lock = new();
    static readonly List<Entry> _entries = new();
    static Thread _thread;
    static volatile bool _libraryChanged;
    static Settings _cfg;
    public static string Folder { get; private set; }
    public static volatile string Busy;           // short status line while hashing/importing

    public static List<Entry> Entries { get { lock (Lock) return _entries.ToList(); } }

    /// <summary>Returns true once after a background import finished (caller reloads the library).</summary>
    public static bool TakeLibraryChanged() { bool v = _libraryChanged; _libraryChanged = false; return v; }

    public static string ResolveFolder(Settings cfg)
    {
        // the "ISOs" folder that ships next to Riffstorm.exe always wins (the game may have been moved or downloaded again)
        var own = System.IO.Path.Combine(AppContext.BaseDirectory, "ISOs");
        if (Directory.Exists(own))
        {
            if (!string.Equals(cfg.IsoFolder, own, StringComparison.OrdinalIgnoreCase)) { cfg.IsoFolder = own; cfg.Save(); }
            return own;
        }
        if (!string.IsNullOrEmpty(cfg.IsoFolder) && Directory.Exists(cfg.IsoFolder)) return cfg.IsoFolder;
        // an "ISOs" folder in one of the parent folders
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; d != null && up < 7; up++, d = d.Parent)
        {
            var c = System.IO.Path.Combine(d.FullName, "ISOs");
            if (Directory.Exists(c)) { cfg.IsoFolder = c; cfg.Save(); return c; }
        }
        var parent = new DirectoryInfo(AppContext.BaseDirectory).Parent ?? new DirectoryInfo(AppContext.BaseDirectory);
        var created = System.IO.Path.Combine(parent.FullName, "ISOs");
        try { Directory.CreateDirectory(created); cfg.IsoFolder = created; cfg.Save(); } catch { }
        return created;
    }

    public static void Start(Settings cfg)
    {
        _cfg = cfg;
        Folder = ResolveFolder(cfg);
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "iso-watcher", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public static void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Folder + "\"") { UseShellExecute = true }); } catch { }
    }

    /// <summary>A disc dropped on the window goes through the same verification.</summary>
    public static void Offer(string path)
    {
        lock (Lock) _extra.Add(path);
    }
    static readonly List<string> _extra = new();

    static void Loop()
    {
        while (true)
        {
            try { Scan(); } catch { }
            Thread.Sleep(4000);
        }
    }

    static void Scan()
    {
        var files = new List<string>();
        if (Directory.Exists(Folder))
            files.AddRange(Directory.EnumerateFiles(Folder, "*.iso", SearchOption.AllDirectories));
        lock (Lock) { files.AddRange(_extra.Where(System.IO.File.Exists)); }
        files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        lock (Lock) _entries.RemoveAll(e => !files.Contains(e.Path, StringComparer.OrdinalIgnoreCase));
        foreach (var f in files)
        {
            Entry e;
            lock (Lock)
            {
                e = _entries.FirstOrDefault(x => x.Path.Equals(f, StringComparison.OrdinalIgnoreCase));
                if (e == null) { e = new Entry { Path = f, File = System.IO.Path.GetFileName(f), State = State.Checking }; _entries.Add(e); }
            }
            if (e.State is State.Imported or State.Incompatible or State.NotYetSupported or State.Error) continue;
            var fi = new FileInfo(f);
            e.Size = fi.Length;
            string sha = Hash(fi, e);
            if (sha == null) continue;
            var kg = Importer.BySha1(sha);
            if (kg == null)
            {
                var same = Importer.BySize(e.Size);
                e.State = State.Incompatible;
                e.Detail = same != null ? $"Parece {same.Name}, mas não é a mesma versão usada no projeto." : "Não é um dos discos compatíveis.";
                continue;
            }
            e.Game = kg;
            if (!kg.Supported) { e.State = State.NotYetSupported; e.Detail = "Compatível – a importação deste jogo ainda vai ser feita."; continue; }
            var root = _cfg.LibraryPath ?? SongDb.DefaultRoot;
            if (Importer.IsImported(root, kg))
            {
                // songs imported by an older version: still pull the original highways from the disc
                if (!Highways.Has(root, kg.Id))
                {
                    Busy = $"Extraindo as pistas de {kg.Name}...";
                    try { if (Highways.Extract(f, root, kg) > 0) _libraryChanged = true; } catch { }
                    Busy = null;
                }
                e.State = State.Imported; e.Detail = "Na biblioteca"; continue;
            }

            e.State = State.Importing; e.Detail = "Importando...";
            var pr = new Importer.Progress();
            var t = new Thread(() => Importer.Import(f, root, kg, pr)) { IsBackground = true };
            t.Start();
            while (!pr.Finished)
            {
                e.Progress = pr.Total > 0 ? pr.Done / (float)pr.Total : 0;
                e.Detail = pr.Total > 0 ? $"Importando {pr.Done}/{pr.Total}: {pr.Current}" : "Importando...";
                Busy = $"{kg.Name}: {e.Detail}";
                Thread.Sleep(200);
            }
            Busy = null;
            if (pr.Error != null) { e.State = State.Error; e.Detail = pr.Error; }
            else { e.State = State.Imported; e.Detail = $"{pr.Imported} músicas importadas"; e.Progress = 1; _libraryChanged = true; }
        }
    }

    static string Hash(FileInfo fi, Entry e)
    {
        string key = $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        lock (_cfg) { if (_cfg.IsoHashes.TryGetValue(key, out var cached)) return cached; }
        // quick reject: compatible discs all have a known size
        if (Importer.BySize(fi.Length) == null) return "size-mismatch";
        e.State = State.Checking;
        try
        {
            using var sha1 = SHA1.Create();
            using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            var buf = new byte[8 << 20];
            long done = 0; int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0)
            {
                sha1.TransformBlock(buf, 0, n, null, 0);
                done += n;
                e.Progress = done / (float)fi.Length;
                e.Detail = $"Verificando {(int)(e.Progress * 100)}%";
                Busy = $"Verificando {fi.Name} ({(int)(e.Progress * 100)}%)";
            }
            sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            string h = Convert.ToHexString(sha1.Hash).ToLowerInvariant();
            lock (_cfg) { _cfg.IsoHashes[key] = h; }
            _cfg.Save();
            Busy = null;
            return h;
        }
        catch (IOException) { e.Detail = "Arquivo em uso (ainda copiando?)"; Busy = null; return null; }
    }

    public static string Label(State s) => s switch
    {
        State.Checking => "Verificando",
        State.Compatible => "Compatível",
        State.Importing => "Importando",
        State.Imported => "Na biblioteca",
        State.NotYetSupported => "Em breve",
        State.Incompatible => "Incompatível",
        _ => "Erro",
    };
}
