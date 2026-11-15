using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Riffstorm;

/// <summary>
/// Checks the project's GitHub releases for a newer version and installs it in place:
/// download the release zip, rename the running Riffstorm.exe (Windows allows renaming a running exe),
/// copy the new files over the game folder and restart. ISOs, library, records and settings are untouched.
/// </summary>
public static class Updater
{
    public const string Repo = "henrnasc/Riffstorm";
    const string AssetName = "Riffstorm-windows.zip";

    public enum St { Idle, Checking, UpToDate, Available, Downloading, Installing, Ready, Error }
    public static volatile St State = St.Idle;
    public static string Latest, Error, Notes;
    public static volatile float Progress;
    static string _url;

    public static Version Current
    {
        get { var v = typeof(Updater).Assembly.GetName().Version ?? new Version(1, 0); return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
    }

    public static string CurrentText => Current.Build > 0 ? Current.ToString(3) : Current.ToString(2);

    /// <summary>A development build (dotnet build / build.ps1) has its .dll next to the exe and is not updated in place.</summary>
    public static bool CanInstall => !File.Exists(Path.Combine(AppContext.BaseDirectory, "Riffstorm.dll"));

    static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Riffstorm.exe");
    static string OldExe => Path.Combine(AppContext.BaseDirectory, "Riffstorm.old.exe");

    static HttpClient Http()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Riffstorm-updater/" + CurrentText);
        return h;
    }

    /// <summary>Removes the previous exe left by the last update.</summary>
    public static void CleanupOld()
    {
        for (int i = 0; i < 5; i++)
        {
            try { if (File.Exists(OldExe)) File.Delete(OldExe); return; } catch { Thread.Sleep(300); }
        }
    }

    public static void CheckAsync()
    {
        if (State is St.Checking or St.Downloading or St.Installing) return;
        State = St.Checking; Error = null;
        Task.Run(() =>
        {
            try
            {
                using var http = Http();
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                var json = http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest").Result;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string tag = root.GetProperty("tag_name").GetString() ?? "";
                Notes = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                _url = null;
                foreach (var a in root.GetProperty("assets").EnumerateArray())
                    if (a.GetProperty("name").GetString() == AssetName) _url = a.GetProperty("browser_download_url").GetString();
                var v = ParseVersion(tag);
                Latest = tag.TrimStart('v', 'V');
                State = v != null && v > Current && _url != null ? St.Available : St.UpToDate;
            }
            catch (Exception e) { Error = "Sem conexão com o GitHub (" + (e.InnerException ?? e).Message + ")"; State = St.Error; }
        });
    }

    static Version ParseVersion(string tag)
    {
        var s = tag.TrimStart('v', 'V');
        if (!Version.TryParse(s.Contains('.') ? s : s + ".0", out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    static string _staging, _work;

    public static void InstallAsync()
    {
        if (State != St.Available || _url == null) return;
        if (!CanInstall) { Error = "Esta é uma versão de desenvolvimento: atualize pelo código-fonte."; State = St.Error; return; }
        State = St.Downloading; Progress = 0;
        Task.Run(() =>
        {
            string work = Path.Combine(Path.GetTempPath(), "riffstorm-update");
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                string zip = Path.Combine(work, "update.zip");
                using (var http = Http())
                using (var resp = http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead).Result)
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? 0, got = 0;
                    using var src = resp.Content.ReadAsStreamAsync().Result;
                    using var dst = File.Create(zip);
                    var buf = new byte[1 << 16]; int r;
                    while ((r = src.Read(buf, 0, buf.Length)) > 0) { dst.Write(buf, 0, r); got += r; if (total > 0) Progress = got / (float)total; }
                }
                State = St.Installing;
                string x = Path.Combine(work, "x");
                ZipFile.ExtractToDirectory(zip, x);
                string from = Directory.Exists(Path.Combine(x, "Riffstorm")) ? Path.Combine(x, "Riffstorm") : x;
                if (!File.Exists(Path.Combine(from, "Riffstorm.exe"))) throw new InvalidDataException("pacote sem Riffstorm.exe");
                _staging = from; _work = work;
                State = St.Ready;
            }
            catch (Exception e)
            {
                Error = "Falha ao atualizar: " + (e.InnerException ?? e).Message;
                State = St.Error;
            }
        });
    }

    /// <summary>
    /// The running exe can't replace itself, so a tiny hidden script waits for the game to close, copies the new files
    /// over the game folder and starts the new version. The caller closes the game right after.
    /// </summary>
    public static bool Restart()
    {
        if (_staging == null) return false;
        try
        {
        string dest = AppContext.BaseDirectory.TrimEnd('\\', '/');
        int pid = Environment.ProcessId;
        string script = Path.Combine(_work, "atualizar.cmd");
        string cmd =
            "@echo off\r\nchcp 65001 >nul\r\nset LOG=\"%TEMP%\\riffstorm-update.log\"\r\necho inicio %date% %time% > %LOG%\r\n" +
            ":wait\r\n" +
            $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)\r\n" +
            "echo jogo fechado >> %LOG%\r\nping -n 3 127.0.0.1 >nul\r\n" +
            $"robocopy \"{_staging}\" \"{dest}\" /E /IS /IT /R:20 /W:1 /NFL /NDL /NJH /NJS >> %LOG% 2>&1\r\n" +
            "echo robocopy %errorlevel% >> %LOG%\r\n" +
            $"start \"\" /D \"{dest}\" \"{dest}\\Riffstorm.exe\"\r\n" +
            $"cd /d \"%TEMP%\"\r\nrmdir /s /q \"{_work}\"\r\n";
        File.WriteAllText(script, cmd, new System.Text.UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        return true;
        }
        catch (Exception e) { Error = "Falha ao reiniciar: " + e.Message; State = St.Error; return false; }
    }
}