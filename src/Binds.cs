using Raylib_cs;

namespace Riffstorm;

/// <summary>Game actions the player can rebind (keyboard key + controller input each).</summary>
public enum Act { Fret0, Fret1, Fret2, Fret3, Fret4, StrumUp, StrumDown, Star, Whammy, Pause, PrevGame, NextGame, React1, React2, React3, React4 }

/// <summary>
/// Key/controller bindings. Controller codes: "B&lt;n&gt;" button, "A&lt;n&gt;+"/"A&lt;n&gt;-" axis direction,
/// "W&lt;n&gt;:&lt;rest&gt;" whammy on an axis (resting value), "SR"/"SL" right/left stick (any direction), "" none.
/// </summary>
public static class Binds
{
    public const int Count = 16;

    public static readonly string[] Names =
    {
        "Verde", "Vermelho", "Amarelo", "Azul", "Laranja",
        "Palhetada (cima)", "Palhetada (baixo)", "Modo Fúria", "Alavanca (whammy)", "Pausa", "Jogo anterior (na lista)", "Próximo jogo (na lista)",
        "Reação: Fogo", "Reação: Risada", "Reação: Caveira", "Reação: Palmas",
    };

    static readonly int[] DefKeys =
    {
        (int)KeyboardKey.A, (int)KeyboardKey.S, (int)KeyboardKey.D, (int)KeyboardKey.F, (int)KeyboardKey.G,
        (int)KeyboardKey.Up, (int)KeyboardKey.Down, (int)KeyboardKey.Space, (int)KeyboardKey.K, (int)KeyboardKey.P, (int)KeyboardKey.Q, (int)KeyboardKey.E,
        (int)KeyboardKey.One, (int)KeyboardKey.Two, (int)KeyboardKey.Three, (int)KeyboardKey.Four,
    };

    static string B(GamepadButton b) => "B" + (int)b;

    /// <summary>Controller presets: 0 = joypad (PS2 style), 1 = USB/Xbox guitar.</summary>
    public static string[] DefaultPad(int preset) => preset == 1
        ? new[]
        {
            B(GamepadButton.RightFaceDown), B(GamepadButton.RightFaceRight), B(GamepadButton.RightFaceUp), B(GamepadButton.RightFaceLeft), B(GamepadButton.LeftTrigger1),
            B(GamepadButton.LeftFaceUp), B(GamepadButton.LeftFaceDown), B(GamepadButton.MiddleLeft), "W" + (int)GamepadAxis.RightX + ":-1", B(GamepadButton.MiddleRight),
            B(GamepadButton.LeftTrigger1), B(GamepadButton.RightTrigger1),
            B(GamepadButton.LeftFaceLeft), B(GamepadButton.LeftFaceRight), "", "",
        }
        : new[]
        {
            B(GamepadButton.LeftTrigger2), B(GamepadButton.LeftTrigger1), B(GamepadButton.RightTrigger1), B(GamepadButton.RightTrigger2), B(GamepadButton.RightFaceDown),
            B(GamepadButton.LeftFaceUp), B(GamepadButton.LeftFaceDown), B(GamepadButton.RightFaceUp), "SR", B(GamepadButton.MiddleRight),
            B(GamepadButton.LeftTrigger1), B(GamepadButton.RightTrigger1),
            B(GamepadButton.LeftFaceLeft), B(GamepadButton.LeftFaceRight), B(GamepadButton.LeftThumb), B(GamepadButton.RightThumb),
        };

    public static int[] DefaultKeys() => (int[])DefKeys.Clone();

    /// <summary>Fills missing/old bindings in the settings.</summary>
    public static void Ensure(Settings s)
    {
        // keep what the player already changed; new actions get their default
        var dk = DefaultKeys(); var dp = DefaultPad(s.Controller);
        if (s.KeyBinds == null || s.KeyBinds.Length != Count)
        { var k = dk; if (s.KeyBinds != null) Array.Copy(s.KeyBinds, k, Math.Min(Count, s.KeyBinds.Length)); s.KeyBinds = k; }
        if (s.PadBinds == null || s.PadBinds.Length != Count)
        { var p = dp; if (s.PadBinds != null) Array.Copy(s.PadBinds, p, Math.Min(Count, s.PadBinds.Length)); s.PadBinds = p; }
    }

    // ------------------------------------------------------------------ reading
    public static bool KeyDown(int k) => k > 0 && Raylib.IsKeyDown((KeyboardKey)k);
    public static bool KeyPressed(int k) => k > 0 && Raylib.IsKeyPressed((KeyboardKey)k);

    /// <summary>Analog value 0..1 of a controller binding (buttons are 0 or 1).</summary>
    public static float PadValue(int pad, string code)
    {
        if (pad < 0 || string.IsNullOrEmpty(code)) return 0;
        try
        {
            switch (code[0])
            {
                case 'B': return Raylib.IsGamepadButtonDown(pad, (GamepadButton)int.Parse(code[1..])) ? 1 : 0;
                case 'A':
                {
                    int a = int.Parse(code[1..^1]); float v = Raylib.GetGamepadAxisMovement(pad, (GamepadAxis)a);
                    return Math.Clamp(code[^1] == '-' ? -v : v, 0, 1);
                }
                case 'W':
                {
                    var parts = code[1..].Split(':');
                    int a = int.Parse(parts[0]); float rest = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    float v = Raylib.GetGamepadAxisMovement(pad, (GamepadAxis)a);
                    return Math.Clamp(Math.Abs(v - rest) / (1 + Math.Abs(rest)), 0, 1);
                }
                case 'S':
                {
                    var (ax, ay) = code == "SL" ? (GamepadAxis.LeftX, GamepadAxis.LeftY) : (GamepadAxis.RightX, GamepadAxis.RightY);
                    float x = Raylib.GetGamepadAxisMovement(pad, ax), y = Raylib.GetGamepadAxisMovement(pad, ay);
                    float m = MathF.Sqrt(x * x + y * y);
                    return m > 0.15f ? Math.Clamp((m - 0.15f) / 0.85f, 0, 1) : 0;
                }
            }
        }
        catch { }
        return 0;
    }

    public static bool PadDown(int pad, string code) => PadValue(pad, code) > 0.5f;

    // ------------------------------------------------------------------ capturing a new binding
    static float[] _base = new float[6];
    static bool _waitRelease;

    public static void BeginPadCapture(int pad)
    {
        for (int a = 0; a < 6; a++) _base[a] = pad >= 0 ? Raylib.GetGamepadAxisMovement(pad, (GamepadAxis)a) : 0;
        _waitRelease = true;
    }

    /// <summary>The controller input pressed now (null if none). For the whammy an axis is stored with its resting value.</summary>
    public static string CapturePad(int pad, bool whammy)
    {
        if (pad < 0) return null;
        bool any = false;
        for (int b = 1; b <= 17; b++) if (Raylib.IsGamepadButtonDown(pad, (GamepadButton)b)) any = true;
        if (_waitRelease) { if (!any) _waitRelease = false; return null; }
        for (int a = 0; a < 6; a++)
        {
            float v = Raylib.GetGamepadAxisMovement(pad, (GamepadAxis)a);
            if (Math.Abs(v - _base[a]) > 0.7f)
            {
                if (whammy)
                {
                    if (a is 2 or 3 && Math.Abs(_base[a]) < 0.3f) return "SR";
                    if (a is 0 or 1 && Math.Abs(_base[a]) < 0.3f) return "SL";
                    return "W" + a + ":" + MathF.Round(_base[a]).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return "A" + a + (v > _base[a] ? "+" : "-");
            }
        }
        for (int b = 1; b <= 17; b++) if (Raylib.IsGamepadButtonPressed(pad, (GamepadButton)b)) return "B" + b;
        return null;
    }

    // ------------------------------------------------------------------ names shown on screen
    public static string KeyName(int k)
    {
        if (k <= 0) return "-";
        var key = (KeyboardKey)k;
        return key switch
        {
            KeyboardKey.Space => "Espaço", KeyboardKey.Enter => "Enter", KeyboardKey.Escape => "Esc", KeyboardKey.Backspace => "Backspace",
            KeyboardKey.Tab => "Tab", KeyboardKey.Up => "Seta cima", KeyboardKey.Down => "Seta baixo", KeyboardKey.Left => "Seta esq.", KeyboardKey.Right => "Seta dir.",
            KeyboardKey.LeftShift => "Shift esq.", KeyboardKey.RightShift => "Shift dir.", KeyboardKey.LeftControl => "Ctrl esq.", KeyboardKey.RightControl => "Ctrl dir.",
            KeyboardKey.LeftAlt => "Alt esq.", KeyboardKey.RightAlt => "Alt dir.", KeyboardKey.KpEnter => "Enter (num)", KeyboardKey.CapsLock => "Caps Lock",
            KeyboardKey.PageUp => "Page Up", KeyboardKey.PageDown => "Page Down", KeyboardKey.Home => "Home", KeyboardKey.End => "End",
            KeyboardKey.Insert => "Insert", KeyboardKey.Delete => "Delete", KeyboardKey.Comma => ",", KeyboardKey.Period => ".", KeyboardKey.Slash => "/",
            KeyboardKey.Semicolon => ";", KeyboardKey.Apostrophe => "'", KeyboardKey.LeftBracket => "[", KeyboardKey.RightBracket => "]",
            KeyboardKey.Backslash => "\\", KeyboardKey.Minus => "-", KeyboardKey.Equal => "=", KeyboardKey.Grave => "`",
            >= KeyboardKey.Zero and <= KeyboardKey.Nine => ((char)k).ToString(),
            >= KeyboardKey.A and <= KeyboardKey.Z => ((char)k).ToString(),
            >= KeyboardKey.Kp0 and <= KeyboardKey.Kp9 => "Num " + (k - (int)KeyboardKey.Kp0),
            _ => key.ToString(),
        };
    }

    public static string PadName(string code)
    {
        if (string.IsNullOrEmpty(code)) return "-";
        if (code == "SR") return "Analóg. direito";
        if (code == "SL") return "Analóg. esquerdo";
        try
        {
            if (code[0] == 'B')
                return (GamepadButton)int.Parse(code[1..]) switch
                {
                    GamepadButton.LeftFaceUp => "D-pad cima", GamepadButton.LeftFaceDown => "D-pad baixo",
                    GamepadButton.LeftFaceLeft => "D-pad esq.", GamepadButton.LeftFaceRight => "D-pad dir.",
                    GamepadButton.RightFaceDown => "X / A", GamepadButton.RightFaceRight => "Bola / B",
                    GamepadButton.RightFaceLeft => "Quadrado / X", GamepadButton.RightFaceUp => "Triângulo / Y",
                    GamepadButton.LeftTrigger1 => "L1 / LB", GamepadButton.LeftTrigger2 => "L2 / LT",
                    GamepadButton.RightTrigger1 => "R1 / RB", GamepadButton.RightTrigger2 => "R2 / RT",
                    GamepadButton.MiddleLeft => "Select / Back", GamepadButton.MiddleRight => "Start", GamepadButton.Middle => "Guide",
                    GamepadButton.LeftThumb => "L3", GamepadButton.RightThumb => "R3",
                    var b => "Botão " + (int)b,
                };
            string[] axes = { "Analóg. esq. X", "Analóg. esq. Y", "Analóg. dir. X", "Analóg. dir. Y", "L2 / LT", "R2 / RT" };
            if (code[0] == 'A') { int a = int.Parse(code[1..^1]); return a >= 4 ? axes[a] : axes[a] + (code[^1] == '+' ? " +" : " -"); }
            if (code[0] == 'W') { int a = int.Parse(code[1..].Split(':')[0]); return axes[a]; }
        }
        catch { }
        return code;
    }
}
